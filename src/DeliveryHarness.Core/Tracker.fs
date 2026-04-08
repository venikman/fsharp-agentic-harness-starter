namespace DeliveryHarness.Core

open System

module Tracker =

    let private createFileTrackerPort (workflow: WorkflowDefinition) : TrackerPort =
        let listIssues () = FileTracker.listIssues workflow

        { ListIssues = listIssues
          ListCandidateIssues =
            fun () ->
                listIssues ()
                |> Result.map (List.filter (Workflow.isActive workflow))
          TryFindById = fun issueId -> FileTracker.tryFindById workflow issueId
          TryRefreshById = fun issueId -> FileTracker.tryFindById workflow issueId
          ListTerminalIssues =
            fun () ->
                listIssues ()
                |> Result.map (List.filter (Workflow.isTerminal workflow)) }

    let private createLinearTrackerPort (workflow: WorkflowDefinition) : Result<TrackerPort, string> =
        LinearTracker.createPort workflow

    let create (workflow: WorkflowDefinition) : Result<TrackerPort, string> =
        if String.Equals(workflow.Config.TrackerKind, "file", StringComparison.OrdinalIgnoreCase) then
            Ok(createFileTrackerPort workflow)
        elif String.Equals(workflow.Config.TrackerKind, "linear", StringComparison.OrdinalIgnoreCase) then
            createLinearTrackerPort workflow
        else
            Error(sprintf "Tracker kind '%s' is not implemented in this starter yet." workflow.Config.TrackerKind)

    let listIssues (workflow: WorkflowDefinition) =
        create workflow |> Result.bind (fun port -> port.ListIssues ())

    let listCandidateIssues (workflow: WorkflowDefinition) =
        create workflow |> Result.bind (fun port -> port.ListCandidateIssues ())

    let tryFindById (workflow: WorkflowDefinition) (issueId: string) =
        create workflow |> Result.bind (fun port -> port.TryFindById issueId)

    let tryRefreshById (workflow: WorkflowDefinition) (issueId: string) =
        create workflow |> Result.bind (fun port -> port.TryRefreshById issueId)

    let listTerminalIssues (workflow: WorkflowDefinition) =
        create workflow |> Result.bind (fun port -> port.ListTerminalIssues ())
