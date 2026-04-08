namespace DeliveryHarness.Core

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks

module Orchestrator =

    type private RunCompletion =
        { Succeeded: bool
          Cancelled: bool
          Status: string
          Issue: TrackerIssue
          Summary: string
          RecordPath: string
          WorkspacePath: string
          AttemptNumber: int
          TurnNumber: int }

    type private RetryEntry =
        { mutable Issue: TrackerIssue
          NextAttemptNumber: int
          NextAttemptAtUtc: DateTimeOffset
          LastSummary: string }

    type private RetiredEntry =
        { IssueUpdatedAtUtc: DateTimeOffset
          LastSummary: string }

    type private StopBehavior =
        | PreserveWorkspace
        | CleanupWorkspace

    type private RunningEntry =
        { mutable Issue: TrackerIssue
          WorkflowAtDispatch: WorkflowDefinition
          AttemptNumber: int
          TurnNumber: int
          StartedAtUtc: DateTimeOffset
          WorkspacePath: string
          CancellationTokenSource: CancellationTokenSource
          StopReason: string option ref
          mutable StopBehavior: StopBehavior
          Task: Task<RunCompletion> }

    type private HostRuntime =
        { mutable Workflow: WorkflowDefinition
          mutable ObservedWorkflowWriteTimeUtc: DateTime
          mutable LastReloadAttemptAtUtc: DateTimeOffset option
          mutable LastReloadError: string option
          Running: Dictionary<string, RunningEntry>
          Retrying: Dictionary<string, RetryEntry>
          Retired: Dictionary<string, RetiredEntry> }

    let private await (task: Task) =
        task.GetAwaiter().GetResult()

    let private awaitResult (task: Task<'T>) =
        task.GetAwaiter().GetResult()

    let private runsRoot (workflow: WorkflowDefinition) =
        let path = Path.Combine(workflow.Config.ProjectRoot, ".harness", "runs")
        Directory.CreateDirectory path |> ignore
        path

    let private writeRunRecord (workflow: WorkflowDefinition) (record: RunRecord) =
        let fileName =
            sprintf
                "%s-%s.json"
                (record.StartedAtUtc.ToString("yyyyMMddTHHmmssfffZ"))
                (Workspace.sanitizeIdentifier record.IssueId)

        let path = Path.Combine(runsRoot workflow, fileName)
        let options = JsonSerializerOptions(WriteIndented = true)
        File.WriteAllText(path, JsonSerializer.Serialize(record, options))
        path

    let private normalizeEvidencePaths (paths: string list) =
        paths
        |> List.choose (fun path ->
            if String.IsNullOrWhiteSpace path then
                None
            else
                let fullPath = Path.GetFullPath path

                if File.Exists fullPath then
                    Some fullPath
                else
                    None)
        |> List.distinct
        |> List.sort

    let private tryResult operation =
        try
            operation ()
        with ex ->
            Error ex.Message

    let private methodDescriptionPaths (workflow: WorkflowDefinition) =
        [ workflow.FilePath
          Path.Combine(workflow.Config.ProjectRoot, "AGENTS.md") ]
        |> List.map Path.GetFullPath
        |> List.filter File.Exists
        |> List.distinct
        |> List.sort

    let private validationVerdictForStatus status =
        if String.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase) then
            "Pending"
        else
            "Blocked"

    let private finishRun
        (workflow: WorkflowDefinition)
        (issue: TrackerIssue)
        startedAtUtc
        workspacePath
        status
        cancelled
        summary
        explicitEvidencePaths
        hookOutcomes
        attemptNumber
        turnNumber
        =
        let evidencePaths =
            explicitEvidencePaths @ Workspace.collectEvidencePaths workspacePath
            |> normalizeEvidencePaths

        let record =
            { IssueId = issue.Id
              IssueTitle = issue.Title
              WorkspacePath = workspacePath
              StartedAtUtc = startedAtUtc
              FinishedAtUtc = DateTimeOffset.UtcNow
              Status = status
              Summary = summary
              EvidencePaths = evidencePaths
              AttemptNumber = attemptNumber
              TurnNumber = turnNumber
              Performer =
                { Role = "ConfiguredAgent#CodingAgent"
                  Identity = workflow.Config.AgentCommand }
              MethodDescriptionPaths = methodDescriptionPaths workflow
              Context =
                { ProjectRoot = workflow.Config.ProjectRoot
                  WorkflowPath = workflow.FilePath
                  TrackerKind = workflow.Config.TrackerKind
                  ProjectKey = workflow.Config.ProjectKey }
              ValidationVerdict = validationVerdictForStatus status
              HookOutcomes = hookOutcomes }

        let recordPath = writeRunRecord workflow record

        { Succeeded = String.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase)
          Cancelled = cancelled
          Status = status
          Issue = issue
          Summary = summary
          RecordPath = recordPath
          WorkspacePath = workspacePath
          AttemptNumber = attemptNumber
          TurnNumber = turnNumber }

    let private ensureRunnableWorkflow (workflow: WorkflowDefinition) =
        let errors = Workflow.validate workflow

        if List.isEmpty errors then
            Ok ()
        else
            Error (String.concat Environment.NewLine errors)

    let private hookOutcome name status summary =
        { Name = name
          Status = status
          Summary = summary }

    let private summarizeAfterRunFailure (runSummary: string) (hookError: string) =
        String.concat
            Environment.NewLine
            [ runSummary
              sprintf "After-run hook failed: %s" hookError ]

    let private runIssueAttempt
        (workflow: WorkflowDefinition)
        (issue: TrackerIssue)
        attemptNumber
        turnNumber
        (getCancellationSummary: unit -> string option)
        (cancellationToken: CancellationToken)
        =
        let startedAtUtc = DateTimeOffset.UtcNow
        let attemptedWorkspacePath = Workspace.workspacePathForIssue workflow issue.Id

        let fail workspacePath cancelled summary explicitEvidencePaths hookOutcomes =
            let resolvedSummary =
                if cancelled then
                    getCancellationSummary () |> Option.defaultValue summary
                else
                    summary

            finishRun
                workflow
                issue
                startedAtUtc
                workspacePath
                (if cancelled then "Cancelled" else "Failed")
                cancelled
                resolvedSummary
                explicitEvidencePaths
                hookOutcomes
                attemptNumber
                turnNumber

        let succeed workspacePath summary explicitEvidencePaths hookOutcomes =
            finishRun
                workflow
                issue
                startedAtUtc
                workspacePath
                "Succeeded"
                false
                summary
                explicitEvidencePaths
                hookOutcomes
                attemptNumber
                turnNumber

        match tryResult (fun () -> Workspace.createOrReuse workflow issue) with
        | Error error -> fail attemptedWorkspacePath false error [] []
        | Ok workspace ->
            match
                tryResult (fun () ->
                    Workspace.runHookInWorkspace
                        workflow
                        "before_run"
                        workspace.Path
                        workflow.Config.Hooks.TimeoutMs
                        workflow.Config.Hooks.BeforeRun)
            with
            | Error error ->
                fail workspace.Path false error [] [ hookOutcome "before_run" "Failed" error ]
            | Ok () ->
                let beforeRunHookOutcomes =
                    match workflow.Config.Hooks.BeforeRun with
                    | Some _ -> [ hookOutcome "before_run" "Succeeded" "Hook completed successfully." ]
                    | None -> []

                let outcome =
                    match tryResult (fun () -> Ok (Agent.execute workflow issue workspace attemptNumber turnNumber cancellationToken)) with
                    | Ok outcome -> outcome
                    | Error error ->
                        { Succeeded = false
                          Cancelled = false
                          Summary = error
                          EvidencePaths = []
                          TranscriptPath = None }

                let afterRunResult =
                    tryResult (fun () ->
                        Workspace.runHookInWorkspace
                            workflow
                            "after_run"
                            workspace.Path
                            workflow.Config.Hooks.TimeoutMs
                            workflow.Config.Hooks.AfterRun)

                let afterRunHookOutcomes =
                    match workflow.Config.Hooks.AfterRun, afterRunResult with
                    | None, _ -> []
                    | Some _, Ok () -> [ hookOutcome "after_run" "Succeeded" "Hook completed successfully." ]
                    | Some _, Error error -> [ hookOutcome "after_run" "Failed" error ]

                let hookOutcomes = beforeRunHookOutcomes @ afterRunHookOutcomes

                match outcome.Succeeded, outcome.Cancelled, afterRunResult with
                | true, false, Ok () ->
                    succeed workspace.Path outcome.Summary outcome.EvidencePaths hookOutcomes
                | true, false, Error error ->
                    fail
                        workspace.Path
                        false
                        (summarizeAfterRunFailure outcome.Summary error)
                        outcome.EvidencePaths
                        hookOutcomes
                | _, true, Ok () ->
                    fail workspace.Path true outcome.Summary outcome.EvidencePaths hookOutcomes
                | _, true, Error error ->
                    fail
                        workspace.Path
                        true
                        (summarizeAfterRunFailure outcome.Summary error)
                        outcome.EvidencePaths
                        hookOutcomes
                | false, false, Ok () ->
                    fail workspace.Path false outcome.Summary outcome.EvidencePaths hookOutcomes
                | false, false, Error error ->
                    fail
                        workspace.Path
                        false
                        (summarizeAfterRunFailure outcome.Summary error)
                        outcome.EvidencePaths
                        hookOutcomes

    let private toUserResult (completion: RunCompletion) =
        if completion.Succeeded then
            Ok completion.RecordPath
        else
            Error (sprintf "%s (run record: %s)" completion.Summary completion.RecordPath)

    let runIssue (workflow: WorkflowDefinition) (issue: TrackerIssue) : Result<string, string> =
        match ensureRunnableWorkflow workflow with
        | Error error -> Error error
        | Ok () ->
            runIssueAttempt workflow issue 1 1 (fun () -> None) CancellationToken.None
            |> toUserResult

    let runIssueById (workflow: WorkflowDefinition) (issueId: string) =
        match Tracker.tryFindById workflow issueId with
        | Error error -> Error error
        | Ok None -> Error (sprintf "Issue '%s' was not found." issueId)
        | Ok (Some issue) ->
            if Workflow.isActive workflow issue then
                runIssue workflow issue
            else
                Error(
                    sprintf
                        "Issue '%s' is in state '%s' and is not runnable. Active states: %s"
                        issue.Id
                        issue.State.AsText
                        (String.concat ", " workflow.Config.ActiveStates)
                )

    let private reconcileTerminalWorkspacesExcept (workflow: WorkflowDefinition) (excludedIds: Set<string>) =
        if not workflow.Config.CleanupTerminalWorkspaces then
            Ok []
        else
            match Tracker.listTerminalIssues workflow with
            | Error error -> Error error
            | Ok issues ->
                issues
                |> List.filter (fun issue -> not (excludedIds.Contains issue.Id))
                |> List.fold
                    (fun state issue ->
                        match state, Workspace.removeIfExists workflow issue.Id with
                        | Error error, _ -> Error error
                        | _, Error error -> Error error
                        | Ok removed, Ok () -> Ok (issue.Id :: removed))
                    (Ok [])

    let reconcileTerminalWorkspaces (workflow: WorkflowDefinition) =
        reconcileTerminalWorkspacesExcept workflow Set.empty

    let pollOnce (workflow: WorkflowDefinition) =
        match ensureRunnableWorkflow workflow with
        | Error error -> Error error
        | Ok () ->
            match reconcileTerminalWorkspaces workflow with
            | Error error -> Error error
            | Ok _ ->
                match Tracker.listCandidateIssues workflow with
                | Error error -> Error error
                | Ok issues ->
                    let runnable = issues |> List.truncate workflow.Config.MaxConcurrency

                    let outcomes =
                        runnable
                        |> List.map (fun issue ->
                            Task.Run(fun () ->
                                try
                                    issue, runIssue workflow issue
                                with ex ->
                                    issue, Error ex.Message))
                        |> List.toArray
                        |> Task.WhenAll
                        |> awaitResult
                        |> Array.toList

                    let recordPaths =
                        outcomes
                        |> List.choose (fun (_, outcome) ->
                            match outcome with
                            | Ok recordPath -> Some recordPath
                            | Error _ -> None)

                    let errors =
                        outcomes
                        |> List.choose (fun (issue, outcome) ->
                            match outcome with
                            | Ok _ -> None
                            | Error error -> Some(sprintf "%s: %s" issue.Id error))

                    if List.isEmpty errors then
                        Ok recordPaths
                    else
                        Error(String.concat Environment.NewLine errors)

    let private createHostRuntime workflow =
        { Workflow = workflow
          ObservedWorkflowWriteTimeUtc =
              if File.Exists workflow.FilePath then
                  File.GetLastWriteTimeUtc workflow.FilePath
              else
                  DateTime.MinValue
          LastReloadAttemptAtUtc = None
          LastReloadError = None
          Running = Dictionary()
          Retrying = Dictionary()
          Retired = Dictionary() }

    let private logEvent
        (workflow: WorkflowDefinition)
        level
        eventType
        message
        issueId
        attemptNumber
        turnNumber
        recordPath
        workspacePath
        =
        let sanitizedMessage = OperatorOutput.redactForWorkflow workflow message

        Observability.appendLog
            workflow
            { TimestampUtc = DateTimeOffset.UtcNow
              Level = level
              EventType = eventType
              Message = sanitizedMessage
              IssueId = issueId
              AttemptNumber = attemptNumber
              TurnNumber = turnNumber
              RecordPath = recordPath
              WorkspacePath = workspacePath }
        |> ignore

    let private writeHostStatus (runtime: HostRuntime) =
        let snapshot =
            { GeneratedAtUtc = DateTimeOffset.UtcNow
              WorkflowPath = runtime.Workflow.FilePath
              LogPath = Observability.hostLogPath runtime.Workflow
              ActiveStates = runtime.Workflow.Config.ActiveStates
              TerminalStates = runtime.Workflow.Config.TerminalStates
              LastReloadAttemptAtUtc = runtime.LastReloadAttemptAtUtc
              LastReloadError = runtime.LastReloadError
              RunningIssues =
                  runtime.Running.Values
                  |> Seq.map (fun entry ->
                      { IssueId = entry.Issue.Id
                        IssueTitle = entry.Issue.Title
                        IssueState = entry.Issue.State.AsText
                        AttemptNumber = entry.AttemptNumber
                        TurnNumber = entry.TurnNumber
                        StartedAtUtc = entry.StartedAtUtc
                        WorkspacePath = entry.WorkspacePath })
                  |> Seq.sortBy (fun entry -> entry.IssueId)
                  |> Seq.toList
              RetryingIssues =
                  runtime.Retrying.Values
                  |> Seq.map (fun entry ->
                      { IssueId = entry.Issue.Id
                        IssueTitle = entry.Issue.Title
                        IssueState = entry.Issue.State.AsText
                        NextAttemptNumber = entry.NextAttemptNumber
                        NextAttemptAtUtc = entry.NextAttemptAtUtc
                        LastSummary = OperatorOutput.redactForWorkflow runtime.Workflow entry.LastSummary })
                  |> Seq.sortBy (fun entry -> entry.IssueId)
                  |> Seq.toList }

        Observability.writeStatus runtime.Workflow snapshot |> ignore

    let tryReadHostStatus (workflow: WorkflowDefinition) =
        Observability.tryReadStatus workflow

    let private workflowRestartRequiredChanges (currentWorkflow: WorkflowDefinition) (candidateWorkflow: WorkflowDefinition) =
        [ if
              not (
                  String.Equals(
                      currentWorkflow.Config.TrackerKind,
                      candidateWorkflow.Config.TrackerKind,
                      StringComparison.OrdinalIgnoreCase
                  )
              )
          then
              yield "tracker.kind"

          if
              not (
                  String.Equals(
                      currentWorkflow.Config.ProjectKey,
                      candidateWorkflow.Config.ProjectKey,
                      StringComparison.Ordinal
                  )
              )
          then
              yield "tracker.project_key"

          if
              not (
                  String.Equals(
                      currentWorkflow.Config.TrackerPath,
                      candidateWorkflow.Config.TrackerPath,
                      StringComparison.OrdinalIgnoreCase
                  )
              )
          then
              yield "tracker.path"

          if currentWorkflow.Config.TrackerApiUrl <> candidateWorkflow.Config.TrackerApiUrl then
              yield "tracker.api_url"

          if currentWorkflow.Config.TrackerApiKey <> candidateWorkflow.Config.TrackerApiKey then
              yield "tracker.api_key"

          if
              not (
                  String.Equals(
                      currentWorkflow.Config.WorkspaceRoot,
                      candidateWorkflow.Config.WorkspaceRoot,
                      StringComparison.OrdinalIgnoreCase
                  )
              )
          then
              yield "workspace.root"

          if
              not (
                  String.Equals(
                      currentWorkflow.Config.AgentCommand,
                      candidateWorkflow.Config.AgentCommand,
                      StringComparison.Ordinal
                  )
              )
          then
              yield "agent.command"

          if currentWorkflow.Config.AgentArgs <> candidateWorkflow.Config.AgentArgs then
              yield "agent.args"

          if currentWorkflow.Config.AgentTimeoutMs <> candidateWorkflow.Config.AgentTimeoutMs then
              yield "agent.timeout_ms"

          if currentWorkflow.Config.AgentMaxTurns <> candidateWorkflow.Config.AgentMaxTurns then
              yield "agent.max_turns"

          if currentWorkflow.Config.Hooks <> candidateWorkflow.Config.Hooks then
              yield "hooks.*" ]

    let private tryReloadWorkflow (runtime: HostRuntime) =
        let observedWriteTimeUtc =
            if File.Exists runtime.Workflow.FilePath then
                File.GetLastWriteTimeUtc runtime.Workflow.FilePath
            else
                DateTime.MinValue

        if observedWriteTimeUtc <= runtime.ObservedWorkflowWriteTimeUtc then
            ()
        else
            runtime.LastReloadAttemptAtUtc <- Some DateTimeOffset.UtcNow
            runtime.ObservedWorkflowWriteTimeUtc <- observedWriteTimeUtc

            match Workflow.load runtime.Workflow.FilePath with
            | Error error ->
                runtime.LastReloadError <- Some error
                logEvent runtime.Workflow "error" "workflow_reload_failed" error None None None None None
            | Ok candidateWorkflow ->
                let validationErrors = Workflow.validate candidateWorkflow

                if not (List.isEmpty validationErrors) then
                    let summary = String.concat Environment.NewLine validationErrors
                    runtime.LastReloadError <- Some summary
                    logEvent runtime.Workflow "error" "workflow_reload_failed" summary None None None None None
                else
                    let restartRequiredChanges =
                        workflowRestartRequiredChanges runtime.Workflow candidateWorkflow

                    if not (List.isEmpty restartRequiredChanges) then
                        let summary =
                            sprintf
                                "Workflow change requires restart to apply: %s"
                                (String.concat ", " restartRequiredChanges)

                        runtime.LastReloadError <- Some summary
                        logEvent runtime.Workflow "warning" "workflow_reload_restart_required" summary None None None None None
                    else
                        runtime.Workflow <- candidateWorkflow
                        runtime.LastReloadError <- None
                        logEvent
                            runtime.Workflow
                            "info"
                            "workflow_reloaded"
                            "Hot-reloadable workflow settings were applied."
                            None
                            None
                            None
                            None
                            None

    let private backoffDelay (workflow: WorkflowDefinition) attemptNumber =
        TimeSpan.FromSeconds(float (max 1 (workflow.Config.PollIntervalSeconds * attemptNumber)))

    let private retireIssue (runtime: HostRuntime) (issue: TrackerIssue) summary =
        runtime.Retired.[issue.Id] <-
            { IssueUpdatedAtUtc = issue.UpdatedAtUtc
              LastSummary = summary }

    let private isRetiredForCurrentIssue (runtime: HostRuntime) (issue: TrackerIssue) =
        let mutable retiredEntry = Unchecked.defaultof<RetiredEntry>

        if runtime.Retired.TryGetValue(issue.Id, &retiredEntry) then
            if retiredEntry.IssueUpdatedAtUtc >= issue.UpdatedAtUtc then
                true
            else
                runtime.Retired.Remove(issue.Id) |> ignore
                false
        else
            false

    let private stopRunningIssue (runtime: HostRuntime) (entry: RunningEntry) reason stopBehavior eventType =
        if not entry.CancellationTokenSource.IsCancellationRequested then
            entry.StopReason := Some reason
            entry.StopBehavior <- stopBehavior
            entry.CancellationTokenSource.Cancel()

            logEvent
                runtime.Workflow
                "warning"
                eventType
                reason
                (Some entry.Issue.Id)
                (Some entry.AttemptNumber)
                (Some entry.TurnNumber)
                None
                (Some entry.WorkspacePath)

    let private startIssueRun (runtime: HostRuntime) (issue: TrackerIssue) attemptNumber =
        let startedAtUtc = DateTimeOffset.UtcNow
        let dispatchedWorkflow = runtime.Workflow
        let workspacePath = Workspace.workspacePathForIssue dispatchedWorkflow issue.Id
        let cancellationSource = new CancellationTokenSource()
        let stopReason = ref None

        let task =
            Task.Run(fun () ->
                try
                    runIssueAttempt
                        dispatchedWorkflow
                        issue
                        attemptNumber
                        1
                        (fun () -> !stopReason)
                        cancellationSource.Token
                with ex ->
                    { Succeeded = false
                      Cancelled = false
                      Status = "Failed"
                      Issue = issue
                      Summary = ex.Message
                      RecordPath = ""
                      WorkspacePath = workspacePath
                      AttemptNumber = attemptNumber
                      TurnNumber = 1 })

        runtime.Running.[issue.Id] <-
            { Issue = issue
              WorkflowAtDispatch = dispatchedWorkflow
              AttemptNumber = attemptNumber
              TurnNumber = 1
              StartedAtUtc = startedAtUtc
              WorkspacePath = workspacePath
              CancellationTokenSource = cancellationSource
              StopReason = stopReason
              StopBehavior = PreserveWorkspace
              Task = task }

        logEvent
            dispatchedWorkflow
            "info"
            "issue_dispatched"
            (sprintf "Dispatched issue '%s'." issue.Id)
            (Some issue.Id)
            (Some attemptNumber)
            (Some 1)
            None
            (Some workspacePath)

    let private processCompletedRuns (runtime: HostRuntime) =
        let completedIds =
            runtime.Running
            |> Seq.filter (fun item -> item.Value.Task.IsCompleted)
            |> Seq.map (fun item -> item.Key)
            |> Seq.toList

        for issueId in completedIds do
            let entry = runtime.Running.[issueId]
            runtime.Running.Remove(issueId) |> ignore

            let completion =
                try
                    entry.Task.GetAwaiter().GetResult()
                with ex ->
                    { Succeeded = false
                      Cancelled = false
                      Status = "Failed"
                      Issue = entry.Issue
                      Summary = ex.Message
                      RecordPath = ""
                      WorkspacePath = entry.WorkspacePath
                      AttemptNumber = entry.AttemptNumber
                      TurnNumber = entry.TurnNumber }

            let issueSnapshot = entry.Issue

            logEvent
                entry.WorkflowAtDispatch
                (if completion.Succeeded then "info" elif completion.Cancelled then "warning" else "error")
                "run_completed"
                completion.Summary
                (Some issueSnapshot.Id)
                (Some completion.AttemptNumber)
                (Some completion.TurnNumber)
                (if String.IsNullOrWhiteSpace completion.RecordPath then None else Some completion.RecordPath)
                (Some completion.WorkspacePath)

            if completion.Succeeded then
                retireIssue runtime issueSnapshot completion.Summary
            elif completion.Cancelled then
                retireIssue runtime issueSnapshot completion.Summary

                match entry.StopBehavior with
                | CleanupWorkspace ->
                    match Workspace.removeIfExists runtime.Workflow issueSnapshot.Id with
                    | Ok () ->
                        logEvent
                            runtime.Workflow
                            "info"
                            "workspace_cleaned"
                            (sprintf "Cleaned workspace for terminal issue '%s'." issueSnapshot.Id)
                            (Some issueSnapshot.Id)
                            (Some completion.AttemptNumber)
                            (Some completion.TurnNumber)
                            None
                            (Some entry.WorkspacePath)
                    | Error error ->
                        logEvent
                            runtime.Workflow
                            "error"
                            "workspace_cleanup_failed"
                            error
                            (Some issueSnapshot.Id)
                            (Some completion.AttemptNumber)
                            (Some completion.TurnNumber)
                            None
                            (Some entry.WorkspacePath)
                | PreserveWorkspace -> ()
            else
                if completion.AttemptNumber < runtime.Workflow.Config.MaxAttempts then
                    let nextAttemptNumber = completion.AttemptNumber + 1
                    let nextAttemptAtUtc = DateTimeOffset.UtcNow + backoffDelay runtime.Workflow completion.AttemptNumber

                    runtime.Retrying.[issueSnapshot.Id] <-
                        { Issue = issueSnapshot
                          NextAttemptNumber = nextAttemptNumber
                          NextAttemptAtUtc = nextAttemptAtUtc
                          LastSummary = completion.Summary }

                    logEvent
                        runtime.Workflow
                        "warning"
                        "retry_scheduled"
                        (sprintf
                            "Retry %d for issue '%s' scheduled at %s."
                            nextAttemptNumber
                            issueSnapshot.Id
                            (nextAttemptAtUtc.ToString("O")))
                        (Some issueSnapshot.Id)
                        (Some nextAttemptNumber)
                        (Some completion.TurnNumber)
                        (if String.IsNullOrWhiteSpace completion.RecordPath then None else Some completion.RecordPath)
                        (Some entry.WorkspacePath)
                else
                    retireIssue runtime issueSnapshot completion.Summary

                    logEvent
                        runtime.Workflow
                        "warning"
                        "retry_exhausted"
                        (sprintf "Issue '%s' exhausted %d attempt(s)." issueSnapshot.Id completion.AttemptNumber)
                        (Some issueSnapshot.Id)
                        (Some completion.AttemptNumber)
                        (Some completion.TurnNumber)
                        (if String.IsNullOrWhiteSpace completion.RecordPath then None else Some completion.RecordPath)
                        (Some entry.WorkspacePath)

    let private reconcileRunningIssues (runtime: HostRuntime) =
        for entry in runtime.Running.Values |> Seq.toList do
            if not entry.Task.IsCompleted then
                match Tracker.tryRefreshById runtime.Workflow entry.Issue.Id with
                | Error error ->
                    logEvent
                        runtime.Workflow
                        "error"
                        "tracker_refresh_failed"
                        error
                        (Some entry.Issue.Id)
                        (Some entry.AttemptNumber)
                        (Some entry.TurnNumber)
                        None
                        (Some entry.WorkspacePath)
                | Ok None ->
                    stopRunningIssue
                        runtime
                        entry
                        (sprintf "Run cancelled because issue '%s' is no longer present in the tracker." entry.Issue.Id)
                        PreserveWorkspace
                        "run_cancelled_missing_issue"
                | Ok (Some refreshedIssue) ->
                    entry.Issue <- refreshedIssue

                    if Workflow.isTerminal runtime.Workflow refreshedIssue then
                        stopRunningIssue
                            runtime
                            entry
                            (sprintf "Run cancelled because issue entered terminal state '%s'." refreshedIssue.State.AsText)
                            (if runtime.Workflow.Config.CleanupTerminalWorkspaces then CleanupWorkspace else PreserveWorkspace)
                            "run_cancelled_terminal"
                    elif not (Workflow.isActive runtime.Workflow refreshedIssue) then
                        stopRunningIssue
                            runtime
                            entry
                            (sprintf "Run cancelled because issue left the active set and is now '%s'." refreshedIssue.State.AsText)
                            PreserveWorkspace
                            "run_cancelled_non_active"

    let private cleanupTerminalWorkspacesBestEffort (runtime: HostRuntime) =
        if runtime.Workflow.Config.CleanupTerminalWorkspaces then
            let excludedIds =
                runtime.Running.Keys
                |> Seq.append runtime.Retrying.Keys
                |> Set.ofSeq

            match reconcileTerminalWorkspacesExcept runtime.Workflow excludedIds with
            | Ok removedIds ->
                removedIds
                |> List.iter (fun issueId ->
                    logEvent
                        runtime.Workflow
                        "info"
                        "workspace_cleaned"
                        (sprintf "Cleaned workspace for terminal issue '%s'." issueId)
                        (Some issueId)
                        None
                        None
                        None
                        None)
            | Error error ->
                logEvent runtime.Workflow "error" "workspace_cleanup_failed" error None None None None None

    let private dispatchRetryingIssues (runtime: HostRuntime) availableSlots =
        let now = DateTimeOffset.UtcNow

        let dueRetryIds =
            runtime.Retrying
            |> Seq.map (fun item -> item.Key, item.Value)
            |> Seq.filter (fun (_, entry) -> entry.NextAttemptAtUtc <= now)
            |> Seq.sortBy (fun (_, entry) -> entry.NextAttemptAtUtc)
            |> Seq.map fst
            |> Seq.toList

        let rec loop remainingSlots pendingIds =
            match remainingSlots, pendingIds with
            | remaining, _ when remaining <= 0 -> remaining
            | _, [] -> remainingSlots
            | remaining, issueId :: rest ->
                let retryEntry = runtime.Retrying.[issueId]
                runtime.Retrying.Remove(issueId) |> ignore

                match Tracker.tryRefreshById runtime.Workflow issueId with
                | Error error ->
                    runtime.Retrying.[issueId] <-
                        { retryEntry with
                            NextAttemptAtUtc = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(float runtime.Workflow.Config.PollIntervalSeconds) }

                    logEvent
                        runtime.Workflow
                        "error"
                        "retry_refresh_failed"
                        error
                        (Some issueId)
                        (Some retryEntry.NextAttemptNumber)
                        (Some 1)
                        None
                        None

                    loop remaining rest
                | Ok None ->
                    retireIssue runtime retryEntry.Issue retryEntry.LastSummary

                    logEvent
                        runtime.Workflow
                        "warning"
                        "retry_abandoned_missing_issue"
                        (sprintf "Retry for issue '%s' was abandoned because the issue disappeared from the tracker." issueId)
                        (Some issueId)
                        (Some retryEntry.NextAttemptNumber)
                        (Some 1)
                        None
                        None

                    loop remaining rest
                | Ok (Some refreshedIssue) ->
                    if Workflow.isTerminal runtime.Workflow refreshedIssue then
                        retireIssue runtime refreshedIssue retryEntry.LastSummary
                        cleanupTerminalWorkspacesBestEffort runtime

                        logEvent
                            runtime.Workflow
                            "info"
                            "retry_abandoned_terminal"
                            (sprintf "Retry for issue '%s' was abandoned because it is terminal." issueId)
                            (Some issueId)
                            (Some retryEntry.NextAttemptNumber)
                            (Some 1)
                            None
                            None

                        loop remaining rest
                    elif not (Workflow.isActive runtime.Workflow refreshedIssue) then
                        retireIssue runtime refreshedIssue retryEntry.LastSummary

                        logEvent
                            runtime.Workflow
                            "info"
                            "retry_abandoned_non_active"
                            (sprintf "Retry for issue '%s' was abandoned because it is no longer active." issueId)
                            (Some issueId)
                            (Some retryEntry.NextAttemptNumber)
                            (Some 1)
                            None
                            None

                        loop remaining rest
                    else
                        startIssueRun runtime refreshedIssue retryEntry.NextAttemptNumber
                        loop (remaining - 1) rest

        loop availableSlots dueRetryIds

    let private dispatchFreshIssues (runtime: HostRuntime) availableSlots =
        if availableSlots > 0 then
            match Tracker.listCandidateIssues runtime.Workflow with
            | Error error ->
                logEvent runtime.Workflow "error" "candidate_listing_failed" error None None None None None
            | Ok issues ->
                let runnable =
                    issues
                    |> List.filter (fun issue ->
                        not (runtime.Running.ContainsKey issue.Id)
                        && not (runtime.Retrying.ContainsKey issue.Id)
                        && not (isRetiredForCurrentIssue runtime issue))
                    |> List.truncate availableSlots

                runnable |> List.iter (fun issue -> startIssueRun runtime issue 1)

    let private runHostTick (runtime: HostRuntime) =
        tryReloadWorkflow runtime
        processCompletedRuns runtime
        reconcileRunningIssues runtime
        cleanupTerminalWorkspacesBestEffort runtime

        let availableSlots =
            max 0 (runtime.Workflow.Config.MaxConcurrency - runtime.Running.Count)

        let slotsAfterRetries = dispatchRetryingIssues runtime availableSlots
        dispatchFreshIssues runtime slotsAfterRetries
        writeHostStatus runtime

    let private stopAllRunningIssues (runtime: HostRuntime) reason =
        for entry in runtime.Running.Values |> Seq.toList do
            stopRunningIssue runtime entry reason PreserveWorkspace "host_shutdown"

    let private waitForRunningIssuesToStop (runtime: HostRuntime) =
        if runtime.Running.Count > 0 then
            runtime.Running.Values
            |> Seq.map (fun entry -> entry.Task)
            |> Seq.toArray
            |> Task.WhenAll
            |> awaitResult
            |> ignore

    let serve (workflow: WorkflowDefinition) (cancellationToken: CancellationToken) =
        match ensureRunnableWorkflow workflow with
        | Error error -> Error error
        | Ok () ->
            let runtime = createHostRuntime workflow
            logEvent workflow "info" "host_started" "Host mode started." None None None None None
            cleanupTerminalWorkspacesBestEffort runtime
            writeHostStatus runtime

            try
                while not cancellationToken.IsCancellationRequested do
                    runHostTick runtime

                    if not cancellationToken.IsCancellationRequested then
                        try
                            Task.Delay(TimeSpan.FromSeconds(float runtime.Workflow.Config.PollIntervalSeconds), cancellationToken)
                            |> await
                        with :? TaskCanceledException ->
                            ()

                stopAllRunningIssues runtime "Host shutdown requested."
                waitForRunningIssuesToStop runtime
                processCompletedRuns runtime
                writeHostStatus runtime
                logEvent runtime.Workflow "info" "host_stopped" "Host mode stopped." None None None None None
                Ok ()
            with ex ->
                Error ex.Message
