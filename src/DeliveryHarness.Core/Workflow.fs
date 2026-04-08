namespace DeliveryHarness.Core

open System
open System.IO

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
                  MaxAttempts = FrontMatter.getInt "orchestrator.max_attempts" 2 doc
                  AgentCommand = getOneOrDefault "agent.command" "dry-run" doc
                  AgentArgs = FrontMatter.getList "agent.args" doc |> List.map expandEnvValue
                  AgentMaxTurns = FrontMatter.getInt "agent.max_turns" 4 doc
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
              "Workflow prompt body is empty."

          if workflow.Config.TrackerKind <> "file" then
              sprintf "Tracker kind '%s' is not implemented in this starter yet." workflow.Config.TrackerKind

          if String.IsNullOrWhiteSpace workflow.Config.AgentCommand then
              "agent.command is required."

          if workflow.Config.MaxConcurrency < 1 then
              "orchestrator.max_concurrency must be >= 1."

          if workflow.Config.AgentTimeoutMs < 1 then
              "agent.timeout_ms must be >= 1." ]

    let isActive (workflow: WorkflowDefinition) (issue: TrackerIssue) =
        workflow.Config.ActiveStates
        |> List.exists (fun state -> String.Equals(state, issue.State.AsText, StringComparison.OrdinalIgnoreCase))

    let isTerminal (workflow: WorkflowDefinition) (issue: TrackerIssue) =
        workflow.Config.TerminalStates
        |> List.exists (fun state -> String.Equals(state, issue.State.AsText, StringComparison.OrdinalIgnoreCase))
