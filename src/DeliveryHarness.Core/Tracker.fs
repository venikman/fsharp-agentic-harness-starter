namespace DeliveryHarness.Core

open System

module Tracker =

    let private createFileTrackerPort (workflow: WorkflowDefinition) : TrackerPort =
        let listIssues () = FileTracker.listIssues workflow

        { ListIssues = listIssues
          ListCandidateIssues = fun () -> listIssues ()
          TryFindById = fun issueId -> FileTracker.tryFindById workflow issueId
          TryRefreshById = fun issueId -> FileTracker.tryFindById workflow issueId
          ListTerminalIssues =
            fun () ->
                listIssues ()
                |> Result.map (List.filter (Workflow.isTerminal workflow))
          TryUpdateState = fun issueId newState -> FileTracker.updateState workflow issueId newState }

    let private createLinearTrackerPort (workflow: WorkflowDefinition) : Result<TrackerPort, string> =
        LinearTracker.createPort workflow

    let create (workflow: WorkflowDefinition) : Result<TrackerPort, string> =
        if String.Equals(workflow.Config.TrackerKind, "file", StringComparison.OrdinalIgnoreCase) then
            Ok(createFileTrackerPort workflow)
        elif String.Equals(workflow.Config.TrackerKind, "linear", StringComparison.OrdinalIgnoreCase) then
            createLinearTrackerPort workflow
        else
            Error(sprintf "Tracker kind '%s' is not implemented in this starter yet." workflow.Config.TrackerKind)

    let private containsState (states: string list) (value: string) =
        states
        |> List.exists (fun state -> String.Equals(state, value, StringComparison.OrdinalIgnoreCase))

    let dependencySatisfied (workflow: WorkflowDefinition) (issue: TrackerIssue) =
        containsState workflow.Config.DependencySatisfiedStates issue.State.AsText

    let private issueMap (issues: TrackerIssue list) =
        issues
        |> List.fold
            (fun state issue -> state |> Map.add (issue.Id.Trim().ToUpperInvariant()) issue)
            Map.empty

    let dependencyBlockers (workflow: WorkflowDefinition) (issues: TrackerIssue list) (issue: TrackerIssue) =
        let issuesById = issueMap issues
        let issueKey = issue.Id.Trim().ToUpperInvariant()

        issue.DependsOn
        |> List.map (fun dependencyId -> dependencyId.Trim())
        |> List.filter (fun dependencyId -> not (String.IsNullOrWhiteSpace dependencyId))
        |> List.distinct
        |> List.choose (fun dependencyId ->
            let dependencyKey = dependencyId.ToUpperInvariant()

            if String.Equals(issueKey, dependencyKey, StringComparison.Ordinal) then
                Some(sprintf "%s (self dependency)" dependencyId)
            else
                match issuesById |> Map.tryFind dependencyKey with
                | None -> Some(sprintf "%s (missing)" dependencyId)
                | Some dependencyIssue when dependencySatisfied workflow dependencyIssue -> None
                | Some dependencyIssue -> Some(sprintf "%s (%s)" dependencyIssue.Id dependencyIssue.State.AsText))

    let canDispatch (workflow: WorkflowDefinition) (issues: TrackerIssue list) (issue: TrackerIssue) =
        Workflow.isActive workflow issue
        && (dependencyBlockers workflow issues issue |> List.isEmpty)

    let listIssues (workflow: WorkflowDefinition) =
        create workflow |> Result.bind (fun port -> port.ListIssues ())

    let listCandidateIssues (workflow: WorkflowDefinition) =
        listIssues workflow
        |> Result.map (fun issues -> issues |> List.filter (canDispatch workflow issues))

    let tryFindById (workflow: WorkflowDefinition) (issueId: string) =
        create workflow |> Result.bind (fun port -> port.TryFindById issueId)

    let tryRefreshById (workflow: WorkflowDefinition) (issueId: string) =
        create workflow |> Result.bind (fun port -> port.TryRefreshById issueId)

    let listTerminalIssues (workflow: WorkflowDefinition) =
        create workflow |> Result.bind (fun port -> port.ListTerminalIssues ())

    let tryUpdateState (workflow: WorkflowDefinition) (issueId: string) (newState: string) =
        create workflow |> Result.bind (fun port -> port.TryUpdateState issueId newState)
