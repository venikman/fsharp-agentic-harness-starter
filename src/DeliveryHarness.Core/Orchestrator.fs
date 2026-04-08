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
                (record.StartedAtUtc.ToString("yyyyMMddTHHmmssZ"))
                (Workspace.sanitizeIdentifier record.IssueId)

        let path = Path.Combine(runsRoot workflow, fileName)
        let options = JsonSerializerOptions(WriteIndented = true)
        File.WriteAllText(path, JsonSerializer.Serialize(record, options))
        path

    let runIssue (workflow: WorkflowDefinition) (issue: TrackerIssue) : Result<string, string> =
        let startedAtUtc = DateTimeOffset.UtcNow

        match Workspace.createOrReuse workflow issue with
        | Error error -> Error error
        | Ok workspace ->
            match Workspace.runHook "before_run" workspace.Path workflow.Config.Hooks.TimeoutMs workflow.Config.Hooks.BeforeRun with
            | Error error -> Error error
            | Ok () ->
                let outcomeResult = Agent.execute workflow issue workspace
                Workspace.runHookBestEffort "after_run" workspace.Path workflow.Config.Hooks.TimeoutMs workflow.Config.Hooks.AfterRun

                let finishedAtUtc = DateTimeOffset.UtcNow

                let record =
                    match outcomeResult with
                    | Ok outcome ->
                        { IssueId = issue.Id
                          IssueTitle = issue.Title
                          WorkspacePath = workspace.Path
                          StartedAtUtc = startedAtUtc
                          FinishedAtUtc = finishedAtUtc
                          Status = "Succeeded"
                          Summary = outcome.Summary
                          EvidencePaths = outcome.EvidencePaths }
                    | Error error ->
                        { IssueId = issue.Id
                          IssueTitle = issue.Title
                          WorkspacePath = workspace.Path
                          StartedAtUtc = startedAtUtc
                          FinishedAtUtc = finishedAtUtc
                          Status = "Failed"
                          Summary = error
                          EvidencePaths = [] }

                let recordPath = writeRunRecord workflow record

                match outcomeResult with
                | Ok _ -> Ok recordPath
                | Error error -> Error (sprintf "%s (run record: %s)" error recordPath)

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
