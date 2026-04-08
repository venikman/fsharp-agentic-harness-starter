namespace DeliveryHarness.Core

open System
open System.Net.Http
open System.Text
open System.Text.Json

module LinearTracker =

    let defaultApiUrl = "https://api.linear.app/graphql"

    type GraphQLTransport = string -> string -> string -> Result<int * string, string>

    type private LinearSettings =
        { TeamKey: string
          ApiUrl: string
          ApiKey: string }

    type private ParsedIssue =
        { TeamKey: string
          Issue: TrackerIssue }

    let private issuesByTeamQuery =
        """
query IssuesByTeam($after: String, $teamKey: String!) {
  issues(first: 50, after: $after, orderBy: updatedAt, filter: { team: { key: { eq: $teamKey } } }) {
    nodes {
      id
      identifier
      title
      description
      priority
      updatedAt
      url
      state {
        name
      }
      team {
        key
      }
    }
    pageInfo {
      hasNextPage
      endCursor
    }
  }
}
"""

    let private issuesByStateQuery =
        """
query IssuesByState($after: String, $teamKey: String!, $stateNames: [String!]!) {
  issues(
    first: 50
    after: $after
    orderBy: updatedAt
    filter: {
      team: { key: { eq: $teamKey } }
      state: { name: { in: $stateNames } }
    }
  ) {
    nodes {
      id
      identifier
      title
      description
      priority
      updatedAt
      url
      state {
        name
      }
      team {
        key
      }
    }
    pageInfo {
      hasNextPage
      endCursor
    }
  }
}
"""

    let private issueByIdQuery =
        """
query IssueById($id: String!) {
  issue(id: $id) {
    id
    identifier
    title
    description
    priority
    updatedAt
    url
    state {
      name
    }
    team {
      key
    }
  }
}
"""

    let private defaultTransport (apiUrl: string) (authorization: string) (body: string) =
        try
            use client = new HttpClient()
            use request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            request.Headers.TryAddWithoutValidation("Authorization", authorization) |> ignore
            request.Content <- new StringContent(body, Encoding.UTF8, "application/json")

            use response = client.SendAsync(request).GetAwaiter().GetResult()
            let responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            Ok(int response.StatusCode, responseBody)
        with ex ->
            Error(sprintf "Linear tracker request failed: %s" ex.Message)

    let private tryGetProperty (name: string) (element: JsonElement) =
        let mutable property = Unchecked.defaultof<JsonElement>

        if element.ValueKind = JsonValueKind.Object && element.TryGetProperty(name, &property) then
            Some property
        else
            None

    let private tryGetStringProperty (name: string) (element: JsonElement) =
        match tryGetProperty name element with
        | Some value when value.ValueKind <> JsonValueKind.Null && value.ValueKind <> JsonValueKind.Undefined ->
            value.GetString() |> Option.ofObj
        | _ -> None

    let private tryGetIntProperty (name: string) (element: JsonElement) =
        match tryGetProperty name element with
        | Some value when value.ValueKind = JsonValueKind.Number ->
            let mutable parsed = 0

            if value.TryGetInt32(&parsed) then
                Some parsed
            else
                None
        | _ -> None

    let private requiredStringProperty (name: string) (element: JsonElement) =
        match tryGetStringProperty name element with
        | Some value when not (String.IsNullOrWhiteSpace value) -> Ok value
        | _ -> Error(sprintf "Linear tracker payload is missing '%s'." name)

    let private normalizePriority (priority: int) =
        if priority <= 0 then 100 else priority

    let private isAbsoluteHttpUrl (value: string) =
        match Uri.TryCreate(value, UriKind.Absolute) with
        | true, uri ->
            String.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        | _ -> false

    let private sortIssues (issues: TrackerIssue list) =
        issues
        |> List.distinctBy (fun issue -> issue.Id)
        |> List.sortBy (fun issue -> issue.Priority, -issue.UpdatedAtUtc.UtcTicks)

    let private tryReadGraphQLErrors (body: string) =
        try
            use document = JsonDocument.Parse(body)

            match tryGetProperty "errors" document.RootElement with
            | Some errors when errors.ValueKind = JsonValueKind.Array ->
                errors.EnumerateArray()
                |> Seq.choose (tryGetStringProperty "message")
                |> Seq.filter (fun message -> not (String.IsNullOrWhiteSpace message))
                |> Seq.toList
            | _ -> []
        with _ ->
            []

    let private executeGraphQL (transport: GraphQLTransport) (settings: LinearSettings) (body: string) =
        match transport settings.ApiUrl settings.ApiKey body with
        | Error error -> Error error
        | Ok (statusCode, responseBody) ->
            let graphQLErrors = tryReadGraphQLErrors responseBody

            if statusCode < 200 || statusCode >= 300 then
                if List.isEmpty graphQLErrors then
                    Error(sprintf "Linear tracker request failed with HTTP %d." statusCode)
                else
                    Error(sprintf "Linear tracker query failed: %s" (String.concat " | " graphQLErrors))
            elif not (List.isEmpty graphQLErrors) then
                Error(sprintf "Linear tracker query failed: %s" (String.concat " | " graphQLErrors))
            else
                try
                    use document = JsonDocument.Parse(responseBody)

                    match tryGetProperty "data" document.RootElement with
                    | Some data -> Ok(data.Clone())
                    | None -> Error "Linear tracker response did not contain a 'data' payload."
                with ex ->
                    Error(sprintf "Linear tracker returned invalid JSON: %s" ex.Message)

    let private parseIssueEnvelope (element: JsonElement) =
        match requiredStringProperty "identifier" element with
        | Error error -> Error error
        | Ok identifier ->
            match requiredStringProperty "title" element with
            | Error error -> Error error
            | Ok title ->
                match requiredStringProperty "updatedAt" element with
                | Error error -> Error error
                | Ok updatedAtText ->
                    match tryGetProperty "state" element with
                    | None -> Error "Linear tracker payload is missing 'state'."
                    | Some stateElement ->
                        match requiredStringProperty "name" stateElement with
                        | Error error -> Error error
                        | Ok stateName ->
                            match tryGetProperty "team" element with
                            | None -> Error "Linear tracker payload is missing 'team'."
                            | Some teamElement ->
                                match requiredStringProperty "key" teamElement with
                                | Error error -> Error error
                                | Ok teamKey ->
                                    match DateTimeOffset.TryParse(updatedAtText) with
                                    | false, _ ->
                                        Error(sprintf "Linear tracker payload has an invalid updatedAt value '%s'." updatedAtText)
                                    | true, updatedAtUtc ->
                                        let description = tryGetStringProperty "description" element |> Option.defaultValue ""
                                        let priority = tryGetIntProperty "priority" element |> Option.defaultValue 0 |> normalizePriority
                                        let sourcePath = tryGetStringProperty "url" element |> Option.defaultValue identifier

                                        Ok
                                            { TeamKey = teamKey
                                              Issue =
                                                { Id = identifier
                                                  Title = title
                                                  Description = description
                                                  State = IssueState.Parse stateName
                                                  Priority = priority
                                                  Acceptance = []
                                                  Validation = []
                                                  Constraints = []
                                                  UpdatedAtUtc = updatedAtUtc
                                                  SourcePath = sourcePath } }

    let private parseIssuesConnection (data: JsonElement) =
        match tryGetProperty "issues" data with
        | None -> Error "Linear tracker response is missing 'issues'."
        | Some issuesElement ->
            match tryGetProperty "nodes" issuesElement with
            | Some nodesElement when nodesElement.ValueKind = JsonValueKind.Array ->
                let parsedIssues =
                    nodesElement.EnumerateArray()
                    |> Seq.map parseIssueEnvelope
                    |> Seq.fold
                        (fun state next ->
                            match state, next with
                            | Error error, _ -> Error error
                            | _, Error error -> Error error
                            | Ok items, Ok item -> Ok(item :: items))
                        (Ok [])

                match parsedIssues with
                | Error error -> Error error
                | Ok parsedIssueItems ->
                    match tryGetProperty "pageInfo" issuesElement with
                    | None -> Error "Linear tracker response is missing 'issues.pageInfo'."
                    | Some pageInfo ->
                        let hasNextPage =
                            match tryGetProperty "hasNextPage" pageInfo with
                            | Some value when value.ValueKind = JsonValueKind.True -> true
                            | Some value when value.ValueKind = JsonValueKind.False -> false
                            | _ -> false

                        let endCursor = tryGetStringProperty "endCursor" pageInfo

                        Ok(parsedIssueItems |> List.rev |> List.map (fun item -> item.Issue), hasNextPage, endCursor)
            | _ -> Error "Linear tracker response is missing 'issues.nodes'."

    let private fetchIssues
        (transport: GraphQLTransport)
        (settings: LinearSettings)
        (stateNames: string list option)
        =
        let rec loop afterCursor acc =
            let afterValue =
                match afterCursor with
                | Some value -> value
                | None -> null

            let body =
                match stateNames with
                | Some names ->
                    JsonSerializer.Serialize(
                        {| query = issuesByStateQuery
                           variables =
                            {| teamKey = settings.TeamKey
                               after = afterValue
                               stateNames = names |> List.toArray |} |}
                    )
                | None ->
                    JsonSerializer.Serialize(
                        {| query = issuesByTeamQuery
                           variables =
                            {| teamKey = settings.TeamKey
                               after = afterValue |} |}
                    )

            match executeGraphQL transport settings body with
            | Error error -> Error error
            | Ok data ->
                match parseIssuesConnection data with
                | Error error -> Error error
                | Ok (pageIssues, hasNextPage, endCursor) ->
                    let next = acc @ pageIssues

                    if hasNextPage then
                        loop endCursor next
                    else
                        Ok(sortIssues next)

        loop None []

    let private tryFetchIssueById (transport: GraphQLTransport) (settings: LinearSettings) (issueId: string) =
        let body =
            JsonSerializer.Serialize(
                {| query = issueByIdQuery
                   variables = {| id = issueId |} |}
            )

        match executeGraphQL transport settings body with
        | Error error -> Error error
        | Ok data ->
            match tryGetProperty "issue" data with
            | Some issue when issue.ValueKind = JsonValueKind.Null -> Ok None
            | Some issue ->
                match parseIssueEnvelope issue with
                | Error error -> Error error
                | Ok parsedIssue when String.Equals(parsedIssue.TeamKey, settings.TeamKey, StringComparison.OrdinalIgnoreCase) ->
                    Ok(Some parsedIssue.Issue)
                | Ok _ -> Ok None
            | None -> Error "Linear tracker response is missing 'issue'."

    let private createSettings (workflow: WorkflowDefinition) =
        match workflow.Config.TrackerApiUrl, workflow.Config.TrackerApiKey with
        | _, _ when String.IsNullOrWhiteSpace workflow.Config.ProjectKey ->
            Error "tracker.project_key is required for tracker.kind: linear."
        | None, _ ->
            Error "tracker.api_url is required for tracker.kind: linear."
        | Some apiUrl, _ when not (isAbsoluteHttpUrl apiUrl) ->
            Error(sprintf "tracker.api_url '%s' must be an absolute http(s) URL." apiUrl)
        | _, None ->
            Error "tracker.api_key is required for tracker.kind: linear."
        | _, Some _ when not workflow.Config.TrackerApiKeyIsEnvBacked ->
            Error "tracker.api_key must use an environment-variable reference such as $LINEAR_API_KEY."
        | _, Some apiKey when String.IsNullOrWhiteSpace apiKey || apiKey.Trim().StartsWith("$", StringComparison.Ordinal) ->
            Error "tracker.api_key environment reference could not be resolved."
        | Some apiUrl, Some apiKey ->
            Ok
                { TeamKey = workflow.Config.ProjectKey
                  ApiUrl = apiUrl
                  ApiKey = apiKey }

    let createPortWithTransport (transport: GraphQLTransport) (workflow: WorkflowDefinition) : Result<TrackerPort, string> =
        createSettings workflow
        |> Result.map (fun settings ->
            { ListIssues = fun () -> fetchIssues transport settings None
              ListCandidateIssues = fun () -> fetchIssues transport settings (Some workflow.Config.ActiveStates)
              TryFindById = fun issueId -> tryFetchIssueById transport settings issueId
              TryRefreshById = fun issueId -> tryFetchIssueById transport settings issueId
              ListTerminalIssues = fun () -> fetchIssues transport settings (Some workflow.Config.TerminalStates) })

    let createPort (workflow: WorkflowDefinition) =
        createPortWithTransport defaultTransport workflow
