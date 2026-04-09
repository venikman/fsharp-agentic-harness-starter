namespace DeliveryHarness.Core

open System
open System.IO

module FileTracker =

    let private tryParseIssue (path: string) : Result<TrackerIssue, string> =
        match FrontMatter.parseFile path with
        | Error error ->
            Error (sprintf "Could not parse issue file '%s': %s" path error)
        | Ok doc ->
            let id =
                FrontMatter.tryGetOne "id" doc
                |> Option.defaultValue (Path.GetFileNameWithoutExtension path)

            let title =
                FrontMatter.tryGetOne "title" doc
                |> Option.defaultValue id

            let state =
                FrontMatter.tryGetOne "state" doc
                |> Option.defaultValue "Todo"
                |> IssueState.Parse

            let priority = FrontMatter.getInt "priority" 100 doc

            Ok
                { Id = id
                  Title = title
                  Description = doc.Body
                  State = state
                  Priority = priority
                  DependsOn = FrontMatter.getList "depends_on" doc
                  Acceptance = FrontMatter.getList "acceptance" doc
                  Validation = FrontMatter.getList "validation" doc
                  Constraints = FrontMatter.getList "constraints" doc
                  UpdatedAtUtc = DateTimeOffset(File.GetLastWriteTimeUtc path)
                  SourcePath = Path.GetFullPath path }

    let listIssues (workflow: WorkflowDefinition) : Result<TrackerIssue list, string> =
        if not (Directory.Exists workflow.Config.TrackerPath) then
            Error (sprintf "Tracker path '%s' does not exist." workflow.Config.TrackerPath)
        else
            let issueFiles =
                Directory.EnumerateFiles(workflow.Config.TrackerPath, "*.md", SearchOption.TopDirectoryOnly)
                |> Seq.filter (fun path -> not (String.Equals(Path.GetFileName path, "README.md", StringComparison.OrdinalIgnoreCase)))
                |> Seq.toList

            let folder (state: Result<TrackerIssue list, string>) (path: string) =
                match state, tryParseIssue path with
                | Error error, _ -> Error error
                | _, Error error -> Error error
                | Ok issues, Ok issue -> Ok(issue :: issues)

            issueFiles
            |> List.fold folder (Ok [])
            |> Result.map (fun issues ->
                issues
                |> List.sortBy (fun issue -> issue.Priority, -issue.UpdatedAtUtc.UtcTicks))

    let tryFindById (workflow: WorkflowDefinition) (issueId: string) : Result<TrackerIssue option, string> =
        listIssues workflow
        |> Result.map (fun issues ->
            issues
            |> List.tryFind (fun issue -> String.Equals(issue.Id, issueId, StringComparison.OrdinalIgnoreCase)))

    let private normalizeLineEndings (text: string) =
        text.Replace("\r\n", "\n").Replace("\r", "\n")

    let private updateStateField (text: string) (newState: string) =
        let normalized = normalizeLineEndings text

        if not (normalized.StartsWith("---\n", StringComparison.Ordinal)) then
            Error "Issue file does not have front matter, so the state cannot be updated automatically."
        else
            let closingIndex = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal)

            if closingIndex < 0 then
                Error "Issue file front matter started but was not closed with ---."
            else
                let rawFrontMatter = normalized.Substring(4, closingIndex - 4)
                let body = normalized.Substring(closingIndex + 5)
                let lines = rawFrontMatter.Split('\n') |> Array.toList

                let rec rewrite found (acc: string list) (remaining: string list) =
                    match remaining with
                    | [] ->
                        let updatedLines =
                            if found then
                                List.rev acc
                            else
                                List.rev (sprintf "state: %s" newState :: acc)

                        updatedLines
                    | line :: rest ->
                        let trimmed = line.TrimStart()
                        let colonIndex = trimmed.IndexOf(':')

                        if colonIndex > 0 then
                            let key = trimmed.Substring(0, colonIndex).Trim()

                            if String.Equals(key, "state", StringComparison.OrdinalIgnoreCase) then
                                rewrite true (sprintf "state: %s" newState :: acc) rest
                            else
                                rewrite found (line :: acc) rest
                        else
                            rewrite found (line :: acc) rest

                let updatedFrontMatter = rewrite false [] lines |> String.concat "\n"
                Ok(String.Concat("---\n", updatedFrontMatter, "\n---\n", body))

    let updateState (workflow: WorkflowDefinition) (issueId: string) (newState: string) : Result<TrackerIssue option, string> =
        tryFindById workflow issueId
        |> Result.bind (function
            | None -> Ok None
            | Some issue when String.Equals(issue.State.AsText, newState, StringComparison.OrdinalIgnoreCase) -> Ok(Some issue)
            | Some issue ->
                if not (File.Exists issue.SourcePath) then
                    Ok None
                else
                    try
                        let updatedText =
                            File.ReadAllText issue.SourcePath
                            |> fun text -> updateStateField text newState

                        match updatedText with
                        | Error error -> Error error
                        | Ok text ->
                            File.WriteAllText(issue.SourcePath, text)
                            tryParseIssue issue.SourcePath |> Result.map Some
                    with ex ->
                        Error(sprintf "Could not update state for issue '%s': %s" issueId ex.Message))
