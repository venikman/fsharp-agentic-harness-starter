open System
open System.IO
open System.Text.Json
open DeliveryHarness.Core

module Assert =

    let fail message =
        raise (InvalidOperationException message)

    let equal label expected actual =
        if actual <> expected then
            fail (sprintf "%s expected %A but got %A" label expected actual)

    let isTrue label condition =
        if not condition then
            fail (sprintf "%s expected true" label)

    let contains label (needle: string) (haystack: string) =
        if not (haystack.Contains(needle, StringComparison.Ordinal)) then
            fail (sprintf "%s expected to find %A in %A" label needle haystack)

    let any label predicate items =
        if not (List.exists predicate items) then
            fail label

let repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

let toolPath name =
    Path.Combine(repoRoot, "tools", name)

let ensureDirectory path =
    Directory.CreateDirectory path |> ignore
    path

let writeFile (path: string) content =
    let directory = Path.GetDirectoryName(path)

    if not (String.IsNullOrWhiteSpace directory) then
        Directory.CreateDirectory directory |> ignore

    File.WriteAllText(path, content)
    path

let withTempDir name action =
    let root =
        Path.Combine(Path.GetTempPath(), "DeliveryHarness.Tests", sprintf "%s-%s" name (Guid.NewGuid().ToString("N")))

    Directory.CreateDirectory root |> ignore

    try
        action root
    finally
        try
            Directory.Delete(root, true)
        with _ ->
            ()

let withEnvironmentVariable name value action =
    let original = Environment.GetEnvironmentVariable name
    Environment.SetEnvironmentVariable(name, value)

    try
        action ()
    finally
        Environment.SetEnvironmentVariable(name, original)

let writeWorkflow root frontMatterLines promptBody =
    writeFile
        (Path.Combine(root, "WORKFLOW.md"))
        (String.concat
            Environment.NewLine
            ([ "---" ]
             @ frontMatterLines
             @ [ "---"; promptBody ]))

let writeIssue root issueId title priority state acceptance validation constraints description =
    let lines = ResizeArray<string>()
    lines.Add "---"
    lines.Add(sprintf "id: %s" issueId)
    lines.Add(sprintf "title: %s" title)
    lines.Add(sprintf "state: %s" state)
    lines.Add(sprintf "priority: %d" priority)
    lines.Add "acceptance:"
    acceptance |> List.iter (fun item -> lines.Add(sprintf "  - %s" item))
    lines.Add "validation:"
    validation |> List.iter (fun item -> lines.Add(sprintf "  - %s" item))
    lines.Add "constraints:"
    constraints |> List.iter (fun item -> lines.Add(sprintf "  - %s" item))
    lines.Add "---"
    lines.Add description

    writeFile (Path.Combine(root, "tracker", "issues", issueId + ".md")) (String.concat Environment.NewLine lines)

let loadWorkflow path =
    match Workflow.load path with
    | Ok workflow -> workflow
    | Error error -> Assert.fail (sprintf "Workflow failed to load: %s" error)

let getOk label = function
    | Ok value -> value
    | Error error -> Assert.fail (sprintf "%s failed: %s" label error)

type RunRecordView =
    { Status: string
      Summary: string
      EvidencePaths: string list }

let loadRunRecord path =
    use document = JsonDocument.Parse(File.ReadAllText path)
    let root = document.RootElement

    let getString (name: string) =
        root.GetProperty(name).GetString() |> Option.ofObj |> Option.defaultValue ""

    { Status = getString "Status"
      Summary = getString "Summary"
      EvidencePaths =
        root.GetProperty("EvidencePaths").EnumerateArray()
        |> Seq.map (fun item -> item.GetString() |> Option.ofObj |> Option.defaultValue "")
        |> Seq.filter (fun item -> not (String.IsNullOrWhiteSpace item))
        |> Seq.toList }

let singleRunRecordPath root =
    Directory.EnumerateFiles(Path.Combine(root, ".harness", "runs"), "*.json")
    |> Seq.exactlyOne

let hasFileName (name: string) (path: string) =
    String.Equals(Path.GetFileName(path), name, StringComparison.Ordinal)

let testWorkflowLoadDefaultsAndEnvResolution () =
    withTempDir "workflow-load" (fun root ->
        ensureDirectory (Path.Combine(root, "env-tracker", "issues")) |> ignore

        withEnvironmentVariable "HARNESS_TRACKER_PATH" "env-tracker/issues" (fun () ->
            let workflowPath =
                writeWorkflow
                    root
                    [ "tracker.path: $HARNESS_TRACKER_PATH"
                      "workspace.root: ./custom-workspaces"
                      "agent.command: dry-run" ]
                    "Prompt body"

            let workflow = loadWorkflow workflowPath

            Assert.equal "tracker kind default" "file" workflow.Config.TrackerKind
            Assert.equal "tracker path env resolution" (Path.GetFullPath(Path.Combine(root, "env-tracker", "issues"))) workflow.Config.TrackerPath
            Assert.equal "workspace root resolution" (Path.GetFullPath(Path.Combine(root, "custom-workspaces"))) workflow.Config.WorkspaceRoot
            Assert.equal "max attempts default" 1 workflow.Config.MaxAttempts
            Assert.equal "agent max turns default" 1 workflow.Config.AgentMaxTurns
            Assert.equal "active state defaults" [ "Todo"; "In Progress" ] workflow.Config.ActiveStates
            Assert.equal "terminal state defaults" [ "Done"; "Closed"; "Cancelled" ] workflow.Config.TerminalStates))

let testWorkflowValidationRejectsMisleadingLocalConfig () =
    withTempDir "workflow-validate" (fun root ->
        writeFile (Path.Combine(root, "not-a-directory.md")) "marker" |> ignore

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./not-a-directory.md"
                  "workspace.root: ."
                  "orchestrator.max_attempts: 2"
                  "agent.command: dry-run"
                  "agent.max_turns: 3"
                  "hooks.before_run: dotnet fsi missing-hook.fsx --workspace ." ]
                "Prompt body"

        let workflow = loadWorkflow workflowPath
        let errors = Workflow.validate workflow

        Assert.any "expected tracker.path file error" (fun (error: string) -> error.Contains("tracker.path", StringComparison.Ordinal)) errors
        Assert.any "expected workspace.root project-root error" (fun (error: string) -> error.Contains("workspace.root must not be the project root", StringComparison.Ordinal)) errors
        Assert.any "expected max_attempts unsupported error" (fun (error: string) -> error.Contains("orchestrator.max_attempts is not yet enforced", StringComparison.Ordinal)) errors
        Assert.any "expected agent.max_turns unsupported error" (fun (error: string) -> error.Contains("agent.max_turns is not yet enforced", StringComparison.Ordinal)) errors
        Assert.any "expected hook reference error" (fun (error: string) -> error.Contains("hooks.before_run references", StringComparison.Ordinal)) errors)

let testIssueParsingAndOrdering () =
    withTempDir "issue-ordering" (fun root ->
        ensureDirectory (Path.Combine(root, "tracker", "issues")) |> ignore
        writeFile (Path.Combine(root, "tracker", "issues", "README.md")) "ignore me" |> ignore

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "agent.command: dry-run" ]
                "Prompt body"

        let workflow = loadWorkflow workflowPath

        let issueA =
            writeIssue
                root
                "DEMO-0001"
                "Older priority one"
                1
                "Todo"
                [ "A acceptance" ]
                [ "A validation" ]
                [ "A constraint" ]
                "Issue A body"

        let issueB =
            writeIssue
                root
                "DEMO-0002"
                "Newer priority one"
                1
                "In Progress"
                [ "B acceptance"; "B acceptance 2" ]
                [ "B validation" ]
                [ "B constraint" ]
                "Issue B body"

        let issueC =
            writeIssue
                root
                "DEMO-0003"
                "Priority two"
                2
                "Todo"
                [ "C acceptance" ]
                [ "C validation" ]
                [ "C constraint" ]
                "Issue C body"

        File.SetLastWriteTimeUtc(issueA, DateTime(2024, 1, 1, 0, 0, 1, DateTimeKind.Utc))
        File.SetLastWriteTimeUtc(issueB, DateTime(2024, 1, 1, 0, 0, 2, DateTimeKind.Utc))
        File.SetLastWriteTimeUtc(issueC, DateTime(2024, 1, 1, 0, 0, 3, DateTimeKind.Utc))

        let issues = FileTracker.listIssues workflow |> getOk "listIssues"

        Assert.equal "issue count" 3 issues.Length
        Assert.equal "first issue id" "DEMO-0002" ((issues |> List.item 0).Id)
        Assert.equal "second issue id" "DEMO-0001" ((issues |> List.item 1).Id)
        Assert.equal "third issue id" "DEMO-0003" ((issues |> List.item 2).Id)
        Assert.equal "parsed acceptance" [ "B acceptance"; "B acceptance 2" ] ((issues |> List.item 0).Acceptance)
        Assert.equal "parsed validation" [ "B validation" ] ((issues |> List.item 0).Validation)
        Assert.equal "parsed constraint" [ "B constraint" ] ((issues |> List.item 0).Constraints))

let testWorkspaceSanitizationAndContainment () =
    withTempDir "workspace-safety" (fun root ->
        ensureDirectory (Path.Combine(root, "tracker", "issues")) |> ignore

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "agent.command: dry-run" ]
                "Prompt body"

        let workflow = loadWorkflow workflowPath
        let workspacePath = Workspace.workspacePathForIssue workflow "DEMO 1/2"

        Assert.equal "sanitized workspace directory" "DEMO-1-2" (Path.GetFileName workspacePath)
        Assert.isTrue "workspace path stays inside root" (Workspace.isPathInsideRoot workflow.Config.WorkspaceRoot workspacePath)

        match Workspace.ensureWorkspacePath workflow (Path.Combine(root, "outside-workspace-root")) with
        | Ok path -> Assert.fail (sprintf "Expected outside path rejection, but got %s" path)
        | Error error -> Assert.contains "outside path error" "outside workspace root" error)

let testDryRunWritesRunRecordAndArtifacts () =
    withTempDir "dry-run" (fun root ->
        writeIssue
            root
            "DEMO-DRY"
            "Dry run"
            1
            "Todo"
            [ "Acceptance" ]
            [ "Validation" ]
            [ "Constraint" ]
            "Dry run description"
        |> ignore

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "agent.command: dry-run" ]
                "Do the work"

        let workflow = loadWorkflow workflowPath
        Assert.equal "workflow valid" [] (Workflow.validate workflow)

        let recordPath = Orchestrator.runIssueById workflow "DEMO-DRY" |> getOk "runIssueById"
        let record = loadRunRecord recordPath

        Assert.equal "dry-run status" "Succeeded" record.Status
        Assert.any "expected request artifact" (hasFileName "agent-request.md") record.EvidencePaths
        Assert.any "expected dry-run transcript" (hasFileName "dry-run.txt") record.EvidencePaths

        let requestPath = record.EvidencePaths |> List.find (hasFileName "agent-request.md")
        let transcriptPath = record.EvidencePaths |> List.find (hasFileName "dry-run.txt")
        let requestText = File.ReadAllText requestPath
        let transcriptText = File.ReadAllText transcriptPath

        Assert.contains "request contains issue id" "DEMO-DRY" requestText
        Assert.contains "request contains workflow prompt" "Do the work" requestText
        Assert.contains "dry-run transcript marker" "Dry run agent executed." transcriptText)

let testExternalWorkerStubSucceedsAndWritesEvidence () =
    withTempDir "external-worker-success" (fun root ->
        writeIssue
            root
            "DEMO-STUB"
            "External worker success"
            1
            "Todo"
            [ "Acceptance" ]
            [ "Validation" ]
            [ "Constraint" ]
            "External worker success description"
        |> ignore

        let stubWorker = toolPath "StubWorker.fsx"

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "agent.command: dotnet"
                  "agent.args:"
                  "  - fsi"
                  sprintf "  - %s" stubWorker
                  "  - --workspace"
                  "  - {workspace}"
                  "  - --issue"
                  "  - {issue_id}"
                  "  - --request"
                  "  - {request_path}" ]
                "Prompt body"

        let workflow = loadWorkflow workflowPath
        Assert.equal "workflow valid" [] (Workflow.validate workflow)

        let recordPath = Orchestrator.runIssueById workflow "DEMO-STUB" |> getOk "runIssueById"
        let record = loadRunRecord recordPath

        Assert.equal "stub worker status" "Succeeded" record.Status
        Assert.any "expected request artifact" (hasFileName "agent-request.md") record.EvidencePaths
        Assert.any "expected transcript artifact" (hasFileName "agent-output.txt") record.EvidencePaths
        Assert.any "expected stub worker artifact" (hasFileName "stub-worker.txt") record.EvidencePaths

        let transcriptPath = record.EvidencePaths |> List.find (hasFileName "agent-output.txt")
        let artifactPath = record.EvidencePaths |> List.find (hasFileName "stub-worker.txt")
        let transcriptText = File.ReadAllText transcriptPath
        let artifactText = File.ReadAllText artifactPath

        Assert.contains "stub transcript stdout" "stub worker stdout for DEMO-STUB" transcriptText
        Assert.contains "stub transcript stderr" "stub worker stderr for DEMO-STUB" transcriptText
        Assert.contains "stub artifact request reference" "request_path=" artifactText)

let testWorkspaceCreateFailureStillWritesRunRecord () =
    withTempDir "workspace-create-failure" (fun root ->
        writeIssue
            root
            "DEMO-CREATE"
            "Workspace create failure"
            1
            "Todo"
            [ "Acceptance" ]
            [ "Validation" ]
            [ "Constraint" ]
            "Workspace create failure description"
        |> ignore

        ensureDirectory (Path.Combine(root, ".workspaces")) |> ignore
        writeFile (Path.Combine(root, ".workspaces", "DEMO-CREATE")) "occupied by file" |> ignore

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "agent.command: dry-run" ]
                "Prompt body"

        let workflow = loadWorkflow workflowPath
        Assert.equal "workflow valid" [] (Workflow.validate workflow)

        match Orchestrator.runIssueById workflow "DEMO-CREATE" with
        | Ok recordPath -> Assert.fail (sprintf "Expected workspace creation failure, but run succeeded: %s" recordPath)
        | Error _ -> ()

        let record = loadRunRecord (singleRunRecordPath root)

        Assert.equal "workspace create failure status" "Failed" record.Status
        Assert.isTrue "workspace create failure summary exists" (not (String.IsNullOrWhiteSpace record.Summary)))

let testBeforeRunFailureStillWritesRunRecord () =
    withTempDir "before-run-failure" (fun root ->
        writeIssue
            root
            "DEMO-HOOK"
            "Hook failure"
            1
            "Todo"
            [ "Acceptance" ]
            [ "Validation" ]
            [ "Constraint" ]
            "Hook failure description"
        |> ignore

        let hookScriptPath =
            writeFile
                (Path.Combine(root, "FailBeforeRun.fsx"))
                (String.concat
                    Environment.NewLine
                    [ "open System"
                      "open System.IO"
                      "let args = fsi.CommandLineArgs |> Array.toList"
                      "let workspace = args |> List.pairwise |> List.tryPick (fun (flag, value) -> if flag = \"--workspace\" then Some value else None) |> Option.defaultValue \".\" |> Path.GetFullPath"
                      "let harnessDir = Path.Combine(workspace, \".harness\")"
                      "Directory.CreateDirectory(harnessDir) |> ignore"
                      "File.WriteAllText(Path.Combine(harnessDir, \"before-run-hook.txt\"), \"hook started\")"
                      "eprintfn \"before_run failed on purpose\""
                      "Environment.Exit 3" ])

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "agent.command: dry-run"
                  sprintf "hooks.before_run: dotnet fsi \"%s\" --workspace ." hookScriptPath ]
                "Prompt body"

        let workflow = loadWorkflow workflowPath
        Assert.equal "workflow valid" [] (Workflow.validate workflow)

        match Orchestrator.runIssueById workflow "DEMO-HOOK" with
        | Ok recordPath -> Assert.fail (sprintf "Expected hook failure, but run succeeded: %s" recordPath)
        | Error _ -> ()

        let record = loadRunRecord (singleRunRecordPath root)

        Assert.equal "hook failure status" "Failed" record.Status
        Assert.contains "hook failure summary" "Hook 'before_run' failed." record.Summary
        Assert.any "expected preserved hook evidence" (hasFileName "before-run-hook.txt") record.EvidencePaths)

let testRunPathRejectsUnsupportedAttemptAndTurnConfig () =
    withTempDir "invalid-run-config" (fun root ->
        writeIssue
            root
            "DEMO-INVALID"
            "Invalid workflow"
            1
            "Todo"
            [ "Acceptance" ]
            [ "Validation" ]
            [ "Constraint" ]
            "Invalid workflow description"
        |> ignore

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "orchestrator.max_attempts: 2"
                  "agent.command: dry-run"
                  "agent.max_turns: 2" ]
                "Prompt body"

        let workflow = loadWorkflow workflowPath

        match Orchestrator.runIssueById workflow "DEMO-INVALID" with
        | Ok recordPath -> Assert.fail (sprintf "Expected invalid workflow rejection, but run succeeded: %s" recordPath)
        | Error error ->
            Assert.contains "unsupported attempts reported" "orchestrator.max_attempts is not yet enforced" error
            Assert.contains "unsupported turns reported" "agent.max_turns is not yet enforced" error

        Assert.isTrue
            "invalid workflow should not create workspace"
            (not (Directory.Exists(Path.Combine(root, ".workspaces", "DEMO-INVALID"))))

        Assert.isTrue
            "invalid workflow should not create run records"
            (not (Directory.Exists(Path.Combine(root, ".harness", "runs")))))

let testAgentFailurePreservesRequestAndTranscriptEvidence () =
    withTempDir "agent-failure" (fun root ->
        writeIssue
            root
            "DEMO-FAIL"
            "Agent failure"
            1
            "Todo"
            [ "Acceptance" ]
            [ "Validation" ]
            [ "Constraint" ]
            "Agent failure description"
        |> ignore

        let agentScriptPath =
            writeFile
                (Path.Combine(root, "AgentFail.fsx"))
                (String.concat
                    Environment.NewLine
                    [ "open System"
                      "printfn \"stdout marker\""
                      "eprintfn \"stderr marker\""
                      "Environment.Exit 7" ])

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "agent.command: dotnet"
                  "agent.args:"
                  "  - fsi"
                  sprintf "  - %s" agentScriptPath
                  "  - {request_path}" ]
                "Prompt body"

        let workflow = loadWorkflow workflowPath
        Assert.equal "workflow valid" [] (Workflow.validate workflow)

        match Orchestrator.runIssueById workflow "DEMO-FAIL" with
        | Ok recordPath -> Assert.fail (sprintf "Expected agent failure, but run succeeded: %s" recordPath)
        | Error _ -> ()

        let record = loadRunRecord (singleRunRecordPath root)

        Assert.equal "agent failure status" "Failed" record.Status
        Assert.contains "agent failure summary" "Agent command exited with code 7." record.Summary
        Assert.any "expected request artifact" (hasFileName "agent-request.md") record.EvidencePaths
        Assert.any "expected transcript artifact" (hasFileName "agent-output.txt") record.EvidencePaths

        let requestPath = record.EvidencePaths |> List.find (hasFileName "agent-request.md")
        let transcriptPath = record.EvidencePaths |> List.find (hasFileName "agent-output.txt")
        let transcriptText = File.ReadAllText transcriptPath

        Assert.isTrue "request file exists" (File.Exists requestPath)
        Assert.contains "transcript contains stdout" "stdout marker" transcriptText
        Assert.contains "transcript contains stderr" "stderr marker" transcriptText)

let testAgentTimeoutPreservesRequestAndTranscriptEvidence () =
    withTempDir "agent-timeout" (fun root ->
        writeIssue
            root
            "DEMO-TIMEOUT"
            "Agent timeout"
            1
            "Todo"
            [ "Acceptance" ]
            [ "Validation" ]
            [ "Constraint" ]
            "Agent timeout description"
        |> ignore

        let scriptPath =
            writeFile
                (Path.Combine(root, "SleepAgent.fsx"))
                (String.concat
                    Environment.NewLine
                    [ "open System"
                      "open System.Threading"
                      "printfn \"stdout-before-timeout\""
                      "eprintfn \"stderr-before-timeout\""
                      "Console.Out.Flush()"
                      "Console.Error.Flush()"
                      "Thread.Sleep(10000)" ])

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "agent.command: dotnet"
                  "agent.args:"
                  "  - fsi"
                  sprintf "  - %s" scriptPath
                  "agent.timeout_ms: 3000" ]
                "Prompt body"

        let workflow = loadWorkflow workflowPath
        Assert.equal "workflow valid" [] (Workflow.validate workflow)

        match Orchestrator.runIssueById workflow "DEMO-TIMEOUT" with
        | Ok recordPath -> Assert.fail (sprintf "Expected agent timeout, but run succeeded: %s" recordPath)
        | Error _ -> ()

        let record = loadRunRecord (singleRunRecordPath root)

        Assert.equal "agent timeout status" "Failed" record.Status
        Assert.contains "agent timeout summary" "timed out" record.Summary
        Assert.any "expected timeout request artifact" (hasFileName "agent-request.md") record.EvidencePaths
        Assert.any "expected timeout transcript artifact" (hasFileName "agent-output.txt") record.EvidencePaths

        let transcriptPath = record.EvidencePaths |> List.find (hasFileName "agent-output.txt")
        let transcriptText = File.ReadAllText transcriptPath

        Assert.contains "timeout transcript stdout" "stdout-before-timeout" transcriptText
        Assert.contains "timeout transcript stderr" "stderr-before-timeout" transcriptText
        Assert.contains "timeout transcript message" "Process timed out after 3000 ms." transcriptText)

let testProcessRunnerTimeoutPreservesCapturedOutput () =
    withTempDir "process-timeout" (fun root ->
        let scriptPath =
            writeFile
                (Path.Combine(root, "Sleep.fsx"))
                (String.concat
                    Environment.NewLine
                    [ "open System"
                      "open System.Threading"
                      "printfn \"stdout-before-timeout\""
                      "eprintfn \"stderr-before-timeout\""
                      "Console.Out.Flush()"
                      "Console.Error.Flush()"
                      "Thread.Sleep(10000)" ])

        let result = ProcessRunner.run root 3000 "dotnet" [ "fsi"; scriptPath ]

        Assert.isTrue "process timed out" result.TimedOut
        Assert.contains "stdout captured before timeout" "stdout-before-timeout" result.StdOut
        Assert.contains "stderr captured before timeout" "stderr-before-timeout" result.StdErr
        Assert.contains "timeout message preserved" "Process timed out after 3000 ms." result.StdErr)

type TestCase =
    { Name: string
      Run: unit -> unit }

let tests =
    [ { Name = "Workflow loading resolves defaults, env, and paths"
        Run = testWorkflowLoadDefaultsAndEnvResolution }
      { Name = "Workflow validation rejects misleading local config"
        Run = testWorkflowValidationRejectsMisleadingLocalConfig }
      { Name = "Issue parsing and ordering is deterministic"
        Run = testIssueParsingAndOrdering }
      { Name = "Workspace sanitization and containment are enforced"
        Run = testWorkspaceSanitizationAndContainment }
      { Name = "Dry-run writes run record and artifacts"
        Run = testDryRunWritesRunRecordAndArtifacts }
      { Name = "External worker stub succeeds and writes evidence"
        Run = testExternalWorkerStubSucceedsAndWritesEvidence }
      { Name = "Workspace create failure still writes an auditable run record"
        Run = testWorkspaceCreateFailureStillWritesRunRecord }
      { Name = "Before-run failure still writes an auditable run record"
        Run = testBeforeRunFailureStillWritesRunRecord }
      { Name = "Run path rejects unsupported attempt and turn config"
        Run = testRunPathRejectsUnsupportedAttemptAndTurnConfig }
      { Name = "Agent failure preserves request and transcript evidence"
        Run = testAgentFailurePreservesRequestAndTranscriptEvidence }
      { Name = "Agent timeout preserves request and transcript evidence"
        Run = testAgentTimeoutPreservesRequestAndTranscriptEvidence }
      { Name = "ProcessRunner timeout preserves captured output"
        Run = testProcessRunnerTimeoutPreservesCapturedOutput } ]

[<EntryPoint>]
let main _ =
    let failures = ResizeArray<string>()

    for test in tests do
        try
            test.Run ()
            printfn "PASS %s" test.Name
        with ex ->
            failures.Add(sprintf "FAIL %s%s%s" test.Name Environment.NewLine ex.Message)
            eprintfn "FAIL %s" test.Name
            eprintfn "%s" ex.Message

    if failures.Count = 0 then
        printfn "All %d tests passed." tests.Length
        0
    else
        eprintfn "%d test(s) failed." failures.Count
        failures |> Seq.iter (eprintfn "%s")
        1
