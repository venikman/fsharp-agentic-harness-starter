namespace DeliveryHarness.Core

open System
open System.IO
open System.Text.Json

module Orchestrator =

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

    let private finishRun (workflow: WorkflowDefinition) (issue: TrackerIssue) startedAtUtc workspacePath status summary explicitEvidencePaths =
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
              EvidencePaths = evidencePaths }

        let recordPath = writeRunRecord workflow record

        if String.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase) then
            Ok recordPath
        else
            Error (sprintf "%s (run record: %s)" summary recordPath)

    let private ensureRunnableWorkflow (workflow: WorkflowDefinition) =
        let errors = Workflow.validate workflow

        if List.isEmpty errors then
            Ok ()
        else
            Error (String.concat Environment.NewLine errors)

    let runIssue (workflow: WorkflowDefinition) (issue: TrackerIssue) : Result<string, string> =
        match ensureRunnableWorkflow workflow with
        | Error error -> Error error
        | Ok () ->
            let startedAtUtc = DateTimeOffset.UtcNow
            let attemptedWorkspacePath = Workspace.workspacePathForIssue workflow issue.Id

            let fail workspacePath summary explicitEvidencePaths =
                finishRun workflow issue startedAtUtc workspacePath "Failed" summary explicitEvidencePaths

            let succeed workspacePath summary explicitEvidencePaths =
                finishRun workflow issue startedAtUtc workspacePath "Succeeded" summary explicitEvidencePaths

            match tryResult (fun () -> Workspace.createOrReuse workflow issue) with
            | Error error -> fail attemptedWorkspacePath error []
            | Ok workspace ->
                match tryResult (fun () -> Workspace.runHookInWorkspace workflow "before_run" workspace.Path workflow.Config.Hooks.TimeoutMs workflow.Config.Hooks.BeforeRun) with
                | Error error -> fail workspace.Path error []
                | Ok () ->
                    let outcome =
                        match tryResult (fun () -> Ok (Agent.execute workflow issue workspace)) with
                        | Ok outcome -> outcome
                        | Error error ->
                            { Succeeded = false
                              Summary = error
                              EvidencePaths = []
                              TranscriptPath = None }

                    Workspace.runHookBestEffortInWorkspace workflow "after_run" workspace.Path workflow.Config.Hooks.TimeoutMs workflow.Config.Hooks.AfterRun

                    if outcome.Succeeded then
                        succeed workspace.Path outcome.Summary outcome.EvidencePaths
                    else
                        fail workspace.Path outcome.Summary outcome.EvidencePaths

    let runIssueById (workflow: WorkflowDefinition) (issueId: string) =
        match FileTracker.tryFindById workflow issueId with
        | Error error -> Error error
        | Ok None -> Error (sprintf "Issue '%s' was not found." issueId)
        | Ok (Some issue) -> runIssue workflow issue

    let reconcileTerminalWorkspaces (workflow: WorkflowDefinition) =
        if not workflow.Config.CleanupTerminalWorkspaces then
            Ok []
        else
            match FileTracker.listIssues workflow with
            | Error error -> Error error
            | Ok issues ->
                issues
                |> List.filter (Workflow.isTerminal workflow)
                |> List.fold
                    (fun state issue ->
                        match state, Workspace.removeIfExists workflow issue.Id with
                        | Error error, _ -> Error error
                        | _, Error error -> Error error
                        | Ok removed, Ok () -> Ok (issue.Id :: removed))
                    (Ok [])

    let pollOnce (workflow: WorkflowDefinition) =
        match ensureRunnableWorkflow workflow with
        | Error error -> Error error
        | Ok () ->
            match reconcileTerminalWorkspaces workflow with
            | Error error -> Error error
            | Ok _ ->
                match FileTracker.listIssues workflow with
                | Error error -> Error error
                | Ok issues ->
                    let runnable =
                        issues
                        |> List.filter (Workflow.isActive workflow)
                        |> List.truncate workflow.Config.MaxConcurrency

                    let folder state issue =
                        match state, runIssue workflow issue with
                        | Error error, _ -> Error error
                        | _, Error error -> Error error
                        | Ok paths, Ok recordPath -> Ok (recordPath :: paths)

                    runnable |> List.fold folder (Ok [])
