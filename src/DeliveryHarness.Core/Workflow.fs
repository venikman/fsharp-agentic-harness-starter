namespace DeliveryHarness.Core

open System
open System.IO
open System.Text.RegularExpressions

module Workflow =

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

    let private normalizePath (path: string) =
        Path.GetFullPath path |> Path.TrimEndingDirectorySeparator

    let private isFileSystemRoot (path: string) =
        let normalized = normalizePath path
        let root = Path.GetPathRoot normalized |> Path.TrimEndingDirectorySeparator

        not (String.IsNullOrWhiteSpace root)
        && String.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)

    let private tokenizeCommand (command: string) =
        Regex.Matches(command, "\"[^\"]*\"|'[^']*'|\\S+")
        |> Seq.cast<Match>
        |> Seq.map (fun item -> item.Value.Trim().Trim('"').Trim('\''))
        |> Seq.filter (fun item -> not (String.IsNullOrWhiteSpace item))
        |> Seq.toList

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

            let config =
                { ProjectRoot = baseDir
                  TrackerKind = getOneOrDefault "tracker.kind" "file" doc
                  ProjectKey = getOneOrDefault "tracker.project_key" "DEMO" doc
                  TrackerPath = resolvePath baseDir (getOneOrDefault "tracker.path" "tracker/issues" doc)
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

          if not (String.Equals(workflow.Config.TrackerKind, "file", StringComparison.OrdinalIgnoreCase)) then
              yield sprintf "Tracker kind '%s' is not implemented in this starter yet." workflow.Config.TrackerKind

          if String.IsNullOrWhiteSpace workflow.Config.AgentCommand then
              yield "agent.command is required."

          if File.Exists workflow.Config.TrackerPath then
              yield sprintf "tracker.path '%s' must be a directory, not a file." workflow.Config.TrackerPath
          elif not (Directory.Exists workflow.Config.TrackerPath) then
              yield sprintf "tracker.path '%s' does not exist." workflow.Config.TrackerPath

          if workflow.Config.MaxConcurrency < 1 then
              yield "orchestrator.max_concurrency must be >= 1."

          if workflow.Config.PollIntervalSeconds < 1 then
              yield "orchestrator.poll_interval_seconds must be >= 1."

          if workflow.Config.MaxAttempts < 1 then
              yield "orchestrator.max_attempts must be >= 1."
          elif workflow.Config.MaxAttempts <> 1 then
              yield "orchestrator.max_attempts is not yet enforced in the one-shot local starter. Set it to 1."

          if workflow.Config.AgentMaxTurns < 1 then
              yield "agent.max_turns must be >= 1."
          elif workflow.Config.AgentMaxTurns <> 1 then
              yield "agent.max_turns is not yet enforced in the one-shot local starter. Set it to 1."

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
