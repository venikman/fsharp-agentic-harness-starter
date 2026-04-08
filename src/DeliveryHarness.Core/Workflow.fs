namespace DeliveryHarness.Core

open System
open System.IO
open System.Text.RegularExpressions

module Workflow =

    let private defaultLinearApiUrl = "https://api.linear.app/graphql"

    let private expandEnvValue (value: string) =
        if String.IsNullOrWhiteSpace value then
            value
        elif value.StartsWith("$", StringComparison.Ordinal) then
            let variableName = value.Substring(1)
            let resolved = Environment.GetEnvironmentVariable variableName

            if String.IsNullOrWhiteSpace resolved then
                value
            else
                resolved
        else
            value

    let private resolvePath (baseDir: string) (value: string) =
        let expanded = expandEnvValue value

        if Path.IsPathRooted expanded then
            Path.GetFullPath expanded
        else
            Path.GetFullPath(Path.Combine(baseDir, expanded))

    let private getOneOrDefault key defaultValue doc =
        FrontMatter.tryGetOne key doc
        |> Option.map expandEnvValue
        |> Option.defaultValue defaultValue

    let private secretMarkers =
        [ "SECRET"
          "TOKEN"
          "PASSWORD"
          "PASSWD"
          "PRIVATE"
          "CREDENTIAL"
          "KEY" ]

    let private isSensitiveVariableName (variableName: string) =
        let normalized = variableName.Trim().ToUpperInvariant()
        secretMarkers |> List.exists (fun marker -> normalized.Contains(marker, StringComparison.Ordinal))

    let private tryResolveSensitiveValue (value: string) =
        if String.IsNullOrWhiteSpace value || not (value.StartsWith("$", StringComparison.Ordinal)) then
            None
        else
            let variableName = value.Substring(1).Trim()

            if isSensitiveVariableName variableName then
                let resolved = Environment.GetEnvironmentVariable variableName

                if String.IsNullOrWhiteSpace resolved then
                    None
                else
                    Some resolved
            else
                None
    let private tokenizeCommand (command: string) =
        Regex.Matches(command, "\"[^\"]*\"|'[^']*'|\\S+")
        |> Seq.cast<Match>
        |> Seq.map (fun item -> item.Value.Trim().Trim('"').Trim('\''))
        |> Seq.filter (fun item -> not (String.IsNullOrWhiteSpace item))
        |> Seq.toList

    let private collectSensitiveValues (doc: FrontMatterDocument) =
        doc.Fields
        |> Map.toList
        |> List.collect snd
        |> List.collect (fun value -> value :: tokenizeCommand value)
        |> List.choose tryResolveSensitiveValue
        |> List.distinct
        |> List.sortByDescending String.length

    let private normalizePath (path: string) =
        Path.GetFullPath path |> Path.TrimEndingDirectorySeparator

    let private isFileSystemRoot (path: string) =
        let normalized = normalizePath path
        let root = Path.GetPathRoot normalized |> Path.TrimEndingDirectorySeparator

        not (String.IsNullOrWhiteSpace root)
        && String.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)

    let private isAbsoluteHttpUrl (value: string) =
        match Uri.TryCreate(value, UriKind.Absolute) with
        | true, uri ->
            String.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        | _ -> false


    let private tryResolveReferencedScriptPath (workflow: WorkflowDefinition) (command: string) =
        let sampleWorkspacePath = Path.Combine(workflow.Config.WorkspaceRoot, "__validation__")

        tokenizeCommand command
        |> List.tryFind (fun token ->
            [ ".fsx"; ".cmd"; ".bat"; ".ps1"; ".sh" ]
            |> List.exists (fun suffix -> token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        |> Option.map (fun scriptPath ->
            if Path.IsPathRooted scriptPath then
                Path.GetFullPath scriptPath
            else
                Path.GetFullPath(Path.Combine(sampleWorkspacePath, scriptPath)))

    let private validateHook (workflow: WorkflowDefinition) key scriptOption =
        match scriptOption with
        | None -> []
        | Some script ->
            [ if String.IsNullOrWhiteSpace script then
                  yield sprintf "hooks.%s must not be blank." key

              if script.Contains("\n", StringComparison.Ordinal) || script.Contains("\r", StringComparison.Ordinal) then
                  yield sprintf "hooks.%s must be a single-line command." key

              match tryResolveReferencedScriptPath workflow script with
              | Some scriptPath when not (File.Exists scriptPath) ->
                  yield
                      sprintf
                          "hooks.%s references '%s', which does not exist from a workspace under '%s'."
                          key
                          scriptPath
                          workflow.Config.WorkspaceRoot
              | _ -> () ]

    let load (path: string) : Result<WorkflowDefinition, string> =
        let fullPath = Path.GetFullPath path
        let baseDir = Path.GetDirectoryName fullPath

        match FrontMatter.parseFile fullPath with
        | Error error -> Error error
        | Ok doc ->
            let trackerKind = getOneOrDefault "tracker.kind" "file" doc
            let activeStates =
                let configured = FrontMatter.getList "tracker.active_states" doc

                if List.isEmpty configured then
                    [ "Todo"; "In Progress" ]
                else
                    configured

            let terminalStates =
                let configured = FrontMatter.getList "tracker.terminal_states" doc

                if List.isEmpty configured then
                    [ "Done"; "Closed"; "Cancelled" ]
                else
                    configured

            let trackerPath =
                let configured = getOneOrDefault "tracker.path" "tracker/issues" doc

                if String.Equals(trackerKind, "file", StringComparison.OrdinalIgnoreCase) then
                    resolvePath baseDir configured
                else
                    configured

            let trackerApiUrl =
                match FrontMatter.tryGetOne "tracker.api_url" doc |> Option.map expandEnvValue with
                | Some value -> Some value
                | None when String.Equals(trackerKind, "linear", StringComparison.OrdinalIgnoreCase) -> Some defaultLinearApiUrl
                | None -> None

            let trackerApiKeyRaw = FrontMatter.tryGetOne "tracker.api_key" doc

            let trackerApiKeyIsEnvBacked =
                trackerApiKeyRaw
                |> Option.exists (fun value -> value.Trim().StartsWith("$", StringComparison.Ordinal))

            let trackerApiKey = trackerApiKeyRaw |> Option.map expandEnvValue

            let config =
                { ProjectRoot = baseDir
                  TrackerKind = trackerKind
                  ProjectKey = getOneOrDefault "tracker.project_key" "DEMO" doc
                  TrackerPath = trackerPath
                  TrackerApiUrl = trackerApiUrl
                  TrackerApiKey = trackerApiKey
                  TrackerApiKeyIsEnvBacked = trackerApiKeyIsEnvBacked
                  ActiveStates = activeStates
                  TerminalStates = terminalStates
                  WorkspaceRoot = resolvePath baseDir (getOneOrDefault "workspace.root" ".workspaces" doc)
                  CleanupTerminalWorkspaces = FrontMatter.getBool "workspace.cleanup_terminal" false doc
                  PollIntervalSeconds = FrontMatter.getInt "orchestrator.poll_interval_seconds" 60 doc
                  MaxConcurrency = FrontMatter.getInt "orchestrator.max_concurrency" 1 doc
                  MaxAttempts = FrontMatter.getInt "orchestrator.max_attempts" 1 doc
                  AgentCommand = getOneOrDefault "agent.command" "dry-run" doc
                  AgentArgs = FrontMatter.getList "agent.args" doc |> List.map expandEnvValue
                  AgentMaxTurns = FrontMatter.getInt "agent.max_turns" 1 doc
                  AgentTimeoutMs = FrontMatter.getInt "agent.timeout_ms" 120000 doc
                  SensitiveValues = collectSensitiveValues doc
                  Hooks =
                    { AfterCreate = FrontMatter.tryGetOne "hooks.after_create" doc |> Option.map expandEnvValue
                      BeforeRun = FrontMatter.tryGetOne "hooks.before_run" doc |> Option.map expandEnvValue
                      AfterRun = FrontMatter.tryGetOne "hooks.after_run" doc |> Option.map expandEnvValue
                      BeforeRemove = FrontMatter.tryGetOne "hooks.before_remove" doc |> Option.map expandEnvValue
                      TimeoutMs = FrontMatter.getInt "hooks.timeout_ms" 60000 doc } }

            Ok
                { FilePath = fullPath
                  PromptTemplate = doc.Body
                  Config = config }

    let validate (workflow: WorkflowDefinition) =
        [ if String.IsNullOrWhiteSpace workflow.PromptTemplate then
              yield "Workflow prompt body is empty."
          match workflow.Config.TrackerKind.Trim().ToLowerInvariant() with
          | "file" ->
              if File.Exists workflow.Config.TrackerPath then
                  yield sprintf "tracker.path '%s' must be a directory, not a file." workflow.Config.TrackerPath
              elif not (Directory.Exists workflow.Config.TrackerPath) then
                  yield sprintf "tracker.path '%s' does not exist." workflow.Config.TrackerPath
          | "linear" ->
              if String.IsNullOrWhiteSpace workflow.Config.ProjectKey then
                  yield "tracker.project_key is required for tracker.kind: linear."

              match workflow.Config.TrackerApiUrl with
              | Some apiUrl when isAbsoluteHttpUrl apiUrl -> ()
              | Some apiUrl -> yield sprintf "tracker.api_url '%s' must be an absolute http(s) URL." apiUrl
              | None -> yield "tracker.api_url is required for tracker.kind: linear."

              match workflow.Config.TrackerApiKey with
              | None ->
                  yield "tracker.api_key is required for tracker.kind: linear."
              | Some _ when not workflow.Config.TrackerApiKeyIsEnvBacked ->
                  yield "tracker.api_key must use an environment-variable reference such as $LINEAR_API_KEY."
              | Some apiKey when String.IsNullOrWhiteSpace apiKey || apiKey.Trim().StartsWith("$", StringComparison.Ordinal) ->
                  yield "tracker.api_key environment reference could not be resolved."
              | Some _ -> ()
          | _ ->
              yield sprintf "Tracker kind '%s' is not implemented in this starter yet." workflow.Config.TrackerKind

          if String.IsNullOrWhiteSpace workflow.Config.AgentCommand then
              yield "agent.command is required."

          if workflow.Config.MaxConcurrency < 1 then
              yield "orchestrator.max_concurrency must be >= 1."

          if workflow.Config.PollIntervalSeconds < 1 then
              yield "orchestrator.poll_interval_seconds must be >= 1."

          if workflow.Config.MaxAttempts < 1 then
              yield "orchestrator.max_attempts must be >= 1."

          if workflow.Config.AgentMaxTurns < 1 then
              yield "agent.max_turns must be >= 1."
          elif workflow.Config.AgentMaxTurns <> 1 then
              yield "agent.max_turns > 1 requires a continuation-capable worker runtime, which the starter does not implement yet. Set it to 1."

          if workflow.Config.AgentTimeoutMs < 1 then
              yield "agent.timeout_ms must be >= 1."

          if workflow.Config.Hooks.TimeoutMs < 1 then
              yield "hooks.timeout_ms must be >= 1."

          if File.Exists workflow.Config.WorkspaceRoot then
              yield sprintf "workspace.root '%s' must be a directory path, not a file." workflow.Config.WorkspaceRoot

          if isFileSystemRoot workflow.Config.WorkspaceRoot then
              yield sprintf "workspace.root '%s' must not be a filesystem root." workflow.Config.WorkspaceRoot

          if String.Equals(
              normalizePath workflow.Config.WorkspaceRoot,
              normalizePath workflow.Config.ProjectRoot,
              StringComparison.OrdinalIgnoreCase
          ) then
              yield "workspace.root must not be the project root."

          match Workspace.ensureWorkspacePath workflow (Workspace.workspacePathForIssue workflow "__validation__") with
          | Error error -> yield error
          | Ok _ -> ()
          yield! PromptTemplate.validate workflow.PromptTemplate

          yield! validateHook workflow "after_create" workflow.Config.Hooks.AfterCreate
          yield! validateHook workflow "before_run" workflow.Config.Hooks.BeforeRun
          yield! validateHook workflow "after_run" workflow.Config.Hooks.AfterRun
          yield! validateHook workflow "before_remove" workflow.Config.Hooks.BeforeRemove ]

    let isActive (workflow: WorkflowDefinition) (issue: TrackerIssue) =
        workflow.Config.ActiveStates
        |> List.exists (fun state -> String.Equals(state, issue.State.AsText, StringComparison.OrdinalIgnoreCase))

    let isTerminal (workflow: WorkflowDefinition) (issue: TrackerIssue) =
        workflow.Config.TerminalStates
        |> List.exists (fun state -> String.Equals(state, issue.State.AsText, StringComparison.OrdinalIgnoreCase))
