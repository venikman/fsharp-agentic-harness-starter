open System
open DeliveryHarness.Core

let loadWorkflow path =
    match Workflow.load path with
    | Ok workflow -> workflow
    | Error error ->
        eprintfn "Workflow load failed: %s" error
        Environment.Exit 1
        failwith "unreachable"

let printUsage () =
    printfn "DeliveryHarness CLI"
    printfn ""
    printfn "Usage:"
    printfn "  validate-workflow [WORKFLOW.md]"
    printfn "  list-issues [WORKFLOW.md]"
    printfn "  run-issue <ISSUE-ID> [WORKFLOW.md]"
    printfn "  poll-once [WORKFLOW.md]"

[<EntryPoint>]
let main argv =
    let args = argv |> Array.toList

    match args with
    | [] ->
        printUsage ()
        1

    | [ "validate-workflow" ] ->
        let workflow = loadWorkflow "WORKFLOW.md"
        let errors = Workflow.validate workflow

        if List.isEmpty errors then
            printfn "Workflow is valid enough for the starter harness."
            0
        else
            errors |> List.iter (printfn "ERROR: %s")
            2

    | [ "validate-workflow"; workflowPath ] ->
        let workflow = loadWorkflow workflowPath
        let errors = Workflow.validate workflow

        if List.isEmpty errors then
            printfn "Workflow is valid enough for the starter harness."
            0
        else
            errors |> List.iter (printfn "ERROR: %s")
            2

    | [ "list-issues" ] ->
        let workflow = loadWorkflow "WORKFLOW.md"

        match FileTracker.listIssues workflow with
        | Error error ->
            eprintfn "Issue loading failed: %s" error
            2
        | Ok issues ->
            for issue in issues do
                printfn "%s | %s | %s | p=%d" issue.Id issue.State.AsText issue.Title issue.Priority

            0

    | [ "list-issues"; workflowPath ] ->
        let workflow = loadWorkflow workflowPath

        match FileTracker.listIssues workflow with
        | Error error ->
            eprintfn "Issue loading failed: %s" error
            2
        | Ok issues ->
            for issue in issues do
                printfn "%s | %s | %s | p=%d" issue.Id issue.State.AsText issue.Title issue.Priority

            0

    | [ "run-issue"; issueId ] ->
        let workflow = loadWorkflow "WORKFLOW.md"

        match Orchestrator.runIssueById workflow issueId with
        | Ok recordPath ->
            printfn "Run completed. Record: %s" recordPath
            0
        | Error error ->
            eprintfn "Run failed: %s" error
            2

    | [ "run-issue"; issueId; workflowPath ] ->
        let workflow = loadWorkflow workflowPath

        match Orchestrator.runIssueById workflow issueId with
        | Ok recordPath ->
            printfn "Run completed. Record: %s" recordPath
            0
        | Error error ->
            eprintfn "Run failed: %s" error
            2

    | [ "poll-once" ] ->
        let workflow = loadWorkflow "WORKFLOW.md"

        match Orchestrator.pollOnce workflow with
        | Ok recordPaths ->
            if List.isEmpty recordPaths then
                printfn "No active issues were runnable."
            else
                recordPaths |> List.iter (printfn "Run record: %s")

            0
        | Error error ->
            eprintfn "Polling cycle failed: %s" error
            2

    | [ "poll-once"; workflowPath ] ->
        let workflow = loadWorkflow workflowPath

        match Orchestrator.pollOnce workflow with
        | Ok recordPaths ->
            if List.isEmpty recordPaths then
                printfn "No active issues were runnable."
            else
                recordPaths |> List.iter (printfn "Run record: %s")

            0
        | Error error ->
            eprintfn "Polling cycle failed: %s" error
            2

    | _ ->
        printUsage ()
        1
