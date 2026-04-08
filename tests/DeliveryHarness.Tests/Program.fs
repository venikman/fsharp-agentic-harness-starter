open System
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
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

    let notContains label (needle: string) (haystack: string) =
        if haystack.Contains(needle, StringComparison.Ordinal) then
            fail (sprintf "%s expected not to find %A in %A" label needle haystack)

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
      EvidencePaths: string list
      AttemptNumber: int
      TurnNumber: int
      PerformerRole: string
      PerformerIdentity: string
      MethodDescriptionPaths: string list
      ContextProjectRoot: string
      ContextWorkflowPath: string
      ContextTrackerKind: string
      ContextProjectKey: string
      ValidationVerdict: string
      HookOutcomes: (string * string * string) list }

let loadRunRecord path =
    use document = JsonDocument.Parse(File.ReadAllText path)
    let root = document.RootElement

    let getString (name: string) =
        root.GetProperty(name).GetString() |> Option.ofObj |> Option.defaultValue ""

    let getStringArray (name: string) =
        root.GetProperty(name).EnumerateArray()
        |> Seq.map (fun item -> item.GetString() |> Option.ofObj |> Option.defaultValue "")
        |> Seq.filter (fun item -> not (String.IsNullOrWhiteSpace item))
        |> Seq.toList

    let performer = root.GetProperty("Performer")
    let context = root.GetProperty("Context")

    { Status = getString "Status"
      Summary = getString "Summary"
      EvidencePaths = getStringArray "EvidencePaths"
      AttemptNumber = root.GetProperty("AttemptNumber").GetInt32()
      TurnNumber = root.GetProperty("TurnNumber").GetInt32()
      PerformerRole = performer.GetProperty("Role").GetString() |> Option.ofObj |> Option.defaultValue ""
      PerformerIdentity = performer.GetProperty("Identity").GetString() |> Option.ofObj |> Option.defaultValue ""
      MethodDescriptionPaths = getStringArray "MethodDescriptionPaths"
      ContextProjectRoot = context.GetProperty("ProjectRoot").GetString() |> Option.ofObj |> Option.defaultValue ""
      ContextWorkflowPath = context.GetProperty("WorkflowPath").GetString() |> Option.ofObj |> Option.defaultValue ""
      ContextTrackerKind = context.GetProperty("TrackerKind").GetString() |> Option.ofObj |> Option.defaultValue ""
      ContextProjectKey = context.GetProperty("ProjectKey").GetString() |> Option.ofObj |> Option.defaultValue ""
      ValidationVerdict = getString "ValidationVerdict"
      HookOutcomes =
        root.GetProperty("HookOutcomes").EnumerateArray()
        |> Seq.map (fun item ->
            let getHookString (name: string) =
                item.GetProperty(name).GetString() |> Option.ofObj |> Option.defaultValue ""

            getHookString "Name", getHookString "Status", getHookString "Summary")
        |> Seq.toList }

let singleRunRecordPath root =
    Directory.EnumerateFiles(Path.Combine(root, ".harness", "runs"), "*.json")
    |> Seq.exactlyOne
let runRecordPaths root =
    let runsRoot = Path.Combine(root, ".harness", "runs")

    if Directory.Exists runsRoot then
        Directory.EnumerateFiles(runsRoot, "*.json")
        |> Seq.sort
        |> Seq.toList
    else
        []

let loadAllRunRecords root =
    runRecordPaths root |> List.map loadRunRecord

let hasFileName (name: string) (path: string) =
    String.Equals(Path.GetFileName(path), name, StringComparison.Ordinal)

let waitUntil label (timeoutMs: int) (pollMs: int) predicate =
    let deadline = DateTime.UtcNow.AddMilliseconds(float timeoutMs)

    let rec loop () =
        if predicate () then
            ()
        elif DateTime.UtcNow >= deadline then
            Assert.fail (sprintf "%s timed out after %d ms" label timeoutMs)
        else
            Thread.Sleep pollMs
            loop ()

    loop ()

let readHostStatus workflow =
    match Orchestrator.tryReadHostStatus workflow with
    | Ok (Some snapshot) -> snapshot
    | Ok None -> Assert.fail "Expected host status snapshot to exist."
    | Error error -> Assert.fail (sprintf "Host status read failed: %s" error)

let tryReadHostStatusSnapshot workflow =
    match Orchestrator.tryReadHostStatus workflow with
    | Ok snapshot -> snapshot
    | Error error -> Assert.fail (sprintf "Host status read failed: %s" error)

let withStartedHost workflow action =
    use cancellation = new CancellationTokenSource()
    let hostTask = Task.Run(fun () -> Orchestrator.serve workflow cancellation.Token)

    try
        action cancellation
    finally
        if not cancellation.IsCancellationRequested then
            cancellation.Cancel()

        let hostResult =
            try
                hostTask.GetAwaiter().GetResult()
            with ex ->
                Assert.fail (sprintf "Host task failed unexpectedly: %s" ex.Message)
                Ok ()

        match hostResult with
        | Ok () -> ()
        | Error error -> Assert.fail (sprintf "Host returned an error: %s" error)

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
        Assert.isTrue
            "expected max_attempts to be accepted when >= 1"
            (errors |> List.exists (fun error -> error.Contains("orchestrator.max_attempts", StringComparison.Ordinal)) |> not)
        Assert.any
            "expected continuation-only turns error"
            (fun (error: string) ->
                error.Contains("agent.max_turns > 1 requires a continuation-capable worker runtime", StringComparison.Ordinal))
            errors
        Assert.any "expected hook reference error" (fun (error: string) -> error.Contains("hooks.before_run references", StringComparison.Ordinal)) errors)

let testWorkflowValidationSupportsLinearTrackerConfig () =
    withTempDir "workflow-linear" (fun root ->
        let literalWorkflowPath =
            writeWorkflow
                root
                [ "tracker.kind: linear"
                  "tracker.project_key: ENG"
                  "tracker.api_key: literal-key"
                  "workspace.root: ./.workspaces"
                  "agent.command: dry-run" ]
                "Prompt body"

        let literalWorkflow = loadWorkflow literalWorkflowPath
        let literalErrors = Workflow.validate literalWorkflow

        Assert.equal "linear api url default" (Some "https://api.linear.app/graphql") literalWorkflow.Config.TrackerApiUrl
        Assert.any
            "expected env-backed linear key requirement"
            (fun (error: string) -> error.Contains("tracker.api_key must use an environment-variable reference", StringComparison.Ordinal))
            literalErrors
        Assert.isTrue
            "linear config should not reuse file tracker path validation"
            (literalErrors |> List.exists (fun error -> error.Contains("tracker.path", StringComparison.Ordinal)) |> not)

        let unresolvedWorkflowPath =
            writeWorkflow
                root
                [ "tracker.kind: linear"
                  "tracker.project_key: ENG"
                  "tracker.api_key: $MISSING_LINEAR_API_KEY"
                  "workspace.root: ./.workspaces"
                  "agent.command: dry-run" ]
                "Prompt body"

        let unresolvedErrors = unresolvedWorkflowPath |> loadWorkflow |> Workflow.validate

        Assert.any
            "expected unresolved linear key error"
            (fun (error: string) -> error.Contains("environment reference could not be resolved", StringComparison.Ordinal))
            unresolvedErrors

        withEnvironmentVariable "LINEAR_API_KEY" "linear-secret-value" (fun () ->
            let validWorkflowPath =
                writeWorkflow
                    root
                    [ "tracker.kind: linear"
                      "tracker.project_key: ENG"
                      "tracker.api_key: $LINEAR_API_KEY"
                      "workspace.root: ./.workspaces"
                      "agent.command: dry-run" ]
                    "Prompt body"

            let workflow = loadWorkflow validWorkflowPath

            Assert.equal "resolved linear api key" (Some "linear-secret-value") workflow.Config.TrackerApiKey
            Assert.equal "linear env-backed flag" true workflow.Config.TrackerApiKeyIsEnvBacked
            Assert.equal "valid linear workflow errors" [] (Workflow.validate workflow)))

let testLinearTrackerSupportsPaginationNormalizationAndRefresh () =
    withTempDir "linear-tracker" (fun root ->
        withEnvironmentVariable "LINEAR_API_KEY" "linear-secret-value" (fun () ->
            let workflowPath =
                writeWorkflow
                    root
                    [ "tracker.kind: linear"
                      "tracker.project_key: ENG"
                      "tracker.api_key: $LINEAR_API_KEY"
                      "tracker.active_states:"
                      "  - Todo"
                      "  - In Progress"
                      "tracker.terminal_states:"
                      "  - Done"
                      "workspace.root: ./.workspaces"
                      "agent.command: dry-run" ]
                    "Prompt body"

            let workflow = loadWorkflow workflowPath
            let requests = ResizeArray<string * string * string>()

            let transport url auth body =
                requests.Add(url, auth, body)

                if body.Contains("IssuesByTeam", StringComparison.Ordinal) then
                    if body.Contains("CURSOR-2", StringComparison.Ordinal) then
                        Ok(
                            200,
                            """{"data":{"issues":{"nodes":[{"id":"linear-2","identifier":"ENG-101","title":"Paged second issue","description":null,"priority":0,"updatedAt":"2026-04-08T12:02:00Z","url":"https://linear.app/demo/issue/ENG-101","state":{"name":"Blocked"},"team":{"key":"ENG"}}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}"""
                        )
                    else
                        Ok(
                            200,
                            """{"data":{"issues":{"nodes":[{"id":"linear-1","identifier":"ENG-100","title":"Paged first issue","description":"Issue body","priority":1,"updatedAt":"2026-04-08T12:01:00Z","url":"https://linear.app/demo/issue/ENG-100","state":{"name":"Todo"},"team":{"key":"ENG"}}],"pageInfo":{"hasNextPage":true,"endCursor":"CURSOR-2"}}}}"""
                        )
                elif body.Contains("IssuesByState", StringComparison.Ordinal) then
                    if body.Contains("Done", StringComparison.Ordinal) then
                        Ok(
                            200,
                            """{"data":{"issues":{"nodes":[{"id":"linear-3","identifier":"ENG-102","title":"Terminal issue","description":"Done body","priority":2,"updatedAt":"2026-04-08T12:03:00Z","url":"https://linear.app/demo/issue/ENG-102","state":{"name":"Done"},"team":{"key":"ENG"}}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}"""
                        )
                    else
                        Ok(
                            200,
                            """{"data":{"issues":{"nodes":[{"id":"linear-1","identifier":"ENG-100","title":"Paged first issue","description":"Issue body","priority":1,"updatedAt":"2026-04-08T12:01:00Z","url":"https://linear.app/demo/issue/ENG-100","state":{"name":"Todo"},"team":{"key":"ENG"}}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}"""
                        )
                elif body.Contains("IssueById", StringComparison.Ordinal) then
                    if body.Contains("OTHER-1", StringComparison.Ordinal) then
                        Ok(
                            200,
                            """{"data":{"issue":{"id":"linear-other","identifier":"OTHER-1","title":"Other team issue","description":"","priority":1,"updatedAt":"2026-04-08T12:04:00Z","url":"https://linear.app/demo/issue/OTHER-1","state":{"name":"Todo"},"team":{"key":"OTHER"}}}}"""
                        )
                    else
                        Ok(
                            200,
                            """{"data":{"issue":{"id":"linear-1","identifier":"ENG-100","title":"Paged first issue","description":"Issue body","priority":1,"updatedAt":"2026-04-08T12:01:00Z","url":"https://linear.app/demo/issue/ENG-100","state":{"name":"Todo"},"team":{"key":"ENG"}}}}"""
                        )
                else
                    Error(sprintf "Unexpected Linear tracker request: %s" body)

            let port = LinearTracker.createPortWithTransport transport workflow |> getOk "LinearTracker.createPortWithTransport"
            let listed = port.ListIssues () |> getOk "linear list issues"
            let candidates = port.ListCandidateIssues () |> getOk "linear candidate issues"
            let terminal = port.ListTerminalIssues () |> getOk "linear terminal issues"
            let refreshed = port.TryRefreshById "ENG-100" |> getOk "linear refresh"
            let otherTeam = port.TryFindById "OTHER-1" |> getOk "linear other-team lookup"

            Assert.equal "linear listed issue count" 2 listed.Length
            Assert.equal "linear sorted first issue" "ENG-100" ((listed |> List.item 0).Id)
            Assert.equal "linear sorted second issue" "ENG-101" ((listed |> List.item 1).Id)
            Assert.equal "linear zero priority normalized" 100 ((listed |> List.item 1).Priority)
            Assert.equal "linear source path keeps URL" "https://linear.app/demo/issue/ENG-100" ((listed |> List.item 0).SourcePath)
            Assert.equal "linear candidate count" 1 candidates.Length
            Assert.equal "linear terminal count" 1 terminal.Length
            Assert.equal "linear terminal id" "ENG-102" ((terminal |> List.item 0).Id)
            Assert.equal "linear refresh succeeds" "Paged first issue" (refreshed |> Option.map (fun issue -> issue.Title) |> Option.defaultValue "")
            Assert.equal "linear lookup ignores other teams" None otherTeam

            let requestBodies = requests |> Seq.map (fun (_, _, body) -> body) |> Seq.toList

            Assert.any
                "expected paginated second request"
                (fun (body: string) -> body.Contains("CURSOR-2", StringComparison.Ordinal))
                requestBodies
            Assert.any
                "expected active state filter request"
                (fun (body: string) -> body.Contains("\"stateNames\":[\"Todo\",\"In Progress\"]", StringComparison.Ordinal))
                requestBodies
            Assert.any
                "expected terminal state filter request"
                (fun (body: string) -> body.Contains("\"stateNames\":[\"Done\"]", StringComparison.Ordinal))
                requestBodies
            Assert.any
                "expected authorization header to use raw api key"
                (fun (_, auth, _) -> auth = "linear-secret-value")
                (requests |> Seq.toList)))

let testLinearTrackerMapsGraphQLErrors () =
    withTempDir "linear-tracker-errors" (fun root ->
        withEnvironmentVariable "LINEAR_API_KEY" "linear-secret-value" (fun () ->
            let workflowPath =
                writeWorkflow
                    root
                    [ "tracker.kind: linear"
                      "tracker.project_key: ENG"
                      "tracker.api_key: $LINEAR_API_KEY"
                      "workspace.root: ./.workspaces"
                      "agent.command: dry-run" ]
                    "Prompt body"

            let workflow = loadWorkflow workflowPath

            let failingTransport _ _ _ =
                Ok(200, """{"errors":[{"message":"forbidden"}]}""")

            let port = LinearTracker.createPortWithTransport failingTransport workflow |> getOk "LinearTracker.createPortWithTransport"

            match port.ListIssues () with
            | Ok issues -> Assert.fail (sprintf "Expected Linear GraphQL error, but got %d issues" issues.Length)
            | Error error -> Assert.contains "linear graphql error propagated" "forbidden" error))

let testPromptTemplateValidationAndStrictRendering () =
    withTempDir "prompt-template" (fun root ->
        writeIssue
            root
            "DEMO-TEMPLATE"
            "Prompt template issue"
            1
            "Todo"
            [ "Acceptance bullet" ]
            [ "Validation bullet" ]
            [ "Constraint bullet" ]
            "Prompt template description"
        |> ignore

        let invalidWorkflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "agent.command: dry-run" ]
                "Broken {{issue.unsupported}} marker {{ issue.id "

        let invalidWorkflow = loadWorkflow invalidWorkflowPath
        let invalidErrors = Workflow.validate invalidWorkflow

        Assert.any
            "expected unsupported template variable error"
            (fun (error: string) -> error.Contains("unsupported variable 'issue.unsupported'", StringComparison.Ordinal))
            invalidErrors

        Assert.any
            "expected malformed template marker error"
            (fun (error: string) -> error.Contains("malformed '{{ ... }}' markers", StringComparison.Ordinal))
            invalidErrors

        let validWorkflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "agent.command: dry-run" ]
                (String.concat
                    Environment.NewLine
                    [ "# Prompt"
                      "Issue {{issue.id}}"
                      "Attempt {{ attempt.number }}"
                      "Turn {{turn.number}}"
                      "Validation:"
                      "{{issue.validation}}"
                      "Constraints:"
                      "{{issue.constraints}}" ])

        let workflow = loadWorkflow validWorkflowPath
        Assert.equal "strict template workflow valid" [] (Workflow.validate workflow)

        let recordPath = Orchestrator.runIssueById workflow "DEMO-TEMPLATE" |> getOk "runIssueById"
        let record = loadRunRecord recordPath
        let requestPath = record.EvidencePaths |> List.find (hasFileName "agent-request.md")
        let requestText = File.ReadAllText requestPath

        Assert.contains "strict template issue id rendered" "Issue DEMO-TEMPLATE" requestText
        Assert.contains "strict template attempt rendered" "Attempt 1" requestText
        Assert.contains "strict template turn rendered" "Turn 1" requestText
        Assert.contains "strict template validation bullet rendered" "- Validation bullet" requestText
        Assert.contains "strict template constraint bullet rendered" "- Constraint bullet" requestText
        Assert.notContains "strict template leaves no placeholders" "{{" requestText)

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

        let issues = Tracker.listIssues workflow |> getOk "listIssues"

        Assert.equal "issue count" 3 issues.Length
        Assert.equal "first issue id" "DEMO-0002" ((issues |> List.item 0).Id)
        Assert.equal "second issue id" "DEMO-0001" ((issues |> List.item 1).Id)
        Assert.equal "third issue id" "DEMO-0003" ((issues |> List.item 2).Id)
        Assert.equal "parsed acceptance" [ "B acceptance"; "B acceptance 2" ] ((issues |> List.item 0).Acceptance)
        Assert.equal "parsed validation" [ "B validation" ] ((issues |> List.item 0).Validation)
        Assert.equal "parsed constraint" [ "B constraint" ] ((issues |> List.item 0).Constraints))

let testTrackerSeamSupportsLookupRefreshAndTerminalEnumeration () =
    withTempDir "tracker-seam" (fun root ->
        ensureDirectory (Path.Combine(root, "tracker", "issues")) |> ignore

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "agent.command: dry-run" ]
                "Prompt body"

        let workflow = loadWorkflow workflowPath

        writeIssue root "DEMO-TODO" "Todo issue" 1 "Todo" [ "Acceptance" ] [ "Validation" ] [ "Constraint" ] "Todo body" |> ignore
        writeIssue root "DEMO-BLOCKED" "Blocked issue" 2 "Blocked" [ "Acceptance" ] [ "Validation" ] [ "Constraint" ] "Blocked body" |> ignore
        writeIssue root "DEMO-DONE" "Done issue" 3 "Done" [ "Acceptance" ] [ "Validation" ] [ "Constraint" ] "Done body" |> ignore

        let port = Tracker.create workflow |> getOk "Tracker.create"
        let listed = port.ListIssues () |> getOk "port.ListIssues"
        let candidates = port.ListCandidateIssues () |> getOk "port.ListCandidateIssues"
        let terminal = port.ListTerminalIssues () |> getOk "port.ListTerminalIssues"
        let refreshed = port.TryRefreshById "DEMO-BLOCKED" |> getOk "port.TryRefreshById"

        Assert.equal "tracker seam issue count" 3 listed.Length
        Assert.equal "candidate issue count" 1 candidates.Length
        Assert.equal "candidate issue id" "DEMO-TODO" ((candidates |> List.item 0).Id)
        Assert.equal "terminal issue count" 1 terminal.Length
        Assert.equal "terminal issue id" "DEMO-DONE" ((terminal |> List.item 0).Id)
        Assert.equal "refreshed issue state" "Blocked" (refreshed |> Option.map (fun issue -> issue.State.AsText) |> Option.defaultValue "")

        match port.TryFindById "DEMO-DONE" |> getOk "port.TryFindById" with
        | Some issue -> Assert.equal "found issue title" "Done issue" issue.Title
        | None -> Assert.fail "Expected DEMO-DONE lookup to succeed")

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
        Assert.contains "legacy prompt attempt line" "- attempt: 1" requestText
        Assert.contains "legacy prompt turn line" "- turn: 1" requestText
        Assert.notContains "request hides tracker absolute path" root requestText
        Assert.equal "dry-run attempt number" 1 record.AttemptNumber
        Assert.equal "dry-run turn number" 1 record.TurnNumber
        Assert.equal "dry-run performer role" "ConfiguredAgent#CodingAgent" record.PerformerRole
        Assert.equal "dry-run performer identity" "dry-run" record.PerformerIdentity
        Assert.equal "dry-run validation verdict" "Pending" record.ValidationVerdict
        Assert.isTrue "workflow path recorded" (record.MethodDescriptionPaths |> List.exists (fun path -> path.EndsWith("WORKFLOW.md", StringComparison.Ordinal)))
        Assert.equal "context tracker kind" "file" record.ContextTrackerKind
        Assert.equal "context project key" "DEMO" record.ContextProjectKey
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
        let requestPath = record.EvidencePaths |> List.find (hasFileName "agent-request.md")
        let transcriptText = File.ReadAllText transcriptPath
        let artifactText = File.ReadAllText artifactPath
        let requestText = File.ReadAllText requestPath

        Assert.notContains "external worker request hides tracker absolute path" root requestText
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
        Assert.equal "hook failure validation verdict" "Blocked" record.ValidationVerdict
        Assert.any
            "expected before_run hook outcome"
            (fun (name, status, _) -> name = "before_run" && status = "Failed")
            record.HookOutcomes
        Assert.any "expected preserved hook evidence" (hasFileName "before-run-hook.txt") record.EvidencePaths)

let testAfterRunFailureIsRecordedAndFailsTheRun () =
    withTempDir "after-run-failure" (fun root ->
        writeIssue
            root
            "DEMO-AFTER"
            "After run failure"
            1
            "Todo"
            [ "Acceptance" ]
            [ "Validation" ]
            [ "Constraint" ]
            "After run failure description"
        |> ignore

        let hookScriptPath =
            writeFile
                (Path.Combine(root, "FailAfterRun.fsx"))
                (String.concat
                    Environment.NewLine
                    [ "open System"
                      "open System.IO"
                      "let args = fsi.CommandLineArgs |> Array.toList"
                      "let workspace = args |> List.pairwise |> List.tryPick (fun (flag, value) -> if flag = \"--workspace\" then Some value else None) |> Option.defaultValue \".\" |> Path.GetFullPath"
                      "let harnessDir = Path.Combine(workspace, \".harness\")"
                      "Directory.CreateDirectory(harnessDir) |> ignore"
                      "File.WriteAllText(Path.Combine(harnessDir, \"after-run-hook.txt\"), \"after run hook started\")"
                      "eprintfn \"after_run failed on purpose\""
                      "Environment.Exit 5" ])

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "agent.command: dry-run"
                  sprintf "hooks.after_run: dotnet fsi \"%s\" --workspace ." hookScriptPath ]
                "Prompt body"

        let workflow = loadWorkflow workflowPath
        Assert.equal "workflow valid" [] (Workflow.validate workflow)

        match Orchestrator.runIssueById workflow "DEMO-AFTER" with
        | Ok recordPath -> Assert.fail (sprintf "Expected after_run failure, but run succeeded: %s" recordPath)
        | Error _ -> ()

        let record = loadRunRecord (singleRunRecordPath root)

        Assert.equal "after_run failure status" "Failed" record.Status
        Assert.contains "after_run failure summary" "After-run hook failed" record.Summary
        Assert.equal "after_run failure validation verdict" "Blocked" record.ValidationVerdict
        Assert.any
            "expected after_run hook outcome"
            (fun (name, status, _) -> name = "after_run" && status = "Failed")
            record.HookOutcomes
        Assert.any "expected after_run hook evidence" (hasFileName "after-run-hook.txt") record.EvidencePaths)

let testRunIssueRejectsNonActiveIssueState () =
    withTempDir "non-active-run" (fun root ->
        writeIssue
            root
            "DEMO-BLOCKED"
            "Blocked issue"
            1
            "Blocked"
            [ "Acceptance" ]
            [ "Validation" ]
            [ "Constraint" ]
            "Blocked issue description"
        |> ignore

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "agent.command: dry-run" ]
                "Prompt body"

        let workflow = loadWorkflow workflowPath

        match Orchestrator.runIssueById workflow "DEMO-BLOCKED" with
        | Ok recordPath -> Assert.fail (sprintf "Expected blocked issue rejection, but run succeeded: %s" recordPath)
        | Error error ->
            Assert.contains "non-active issue rejection" "not runnable" error
            Assert.contains "blocked state reported" "Blocked" error

        Assert.isTrue
            "non-active issue should not create workspace"
            (not (Directory.Exists(Path.Combine(root, ".workspaces", "DEMO-BLOCKED"))))

        Assert.isTrue
            "non-active issue should not create run records"
            (not (Directory.Exists(Path.Combine(root, ".harness", "runs")))))

let testRunPathAllowsRetriesButRejectsUnsupportedTurns () =
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
            Assert.notContains "max attempts should now be allowed" "orchestrator.max_attempts" error
            Assert.contains
                "unsupported turns reported"
                "agent.max_turns > 1 requires a continuation-capable worker runtime"
                error

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

        let result = ProcessRunner.run root 3000 "dotnet" [ "fsi"; scriptPath ] CancellationToken.None

        Assert.isTrue "process timed out" result.TimedOut
        Assert.contains "stdout captured before timeout" "stdout-before-timeout" result.StdOut
        Assert.contains "stderr captured before timeout" "stderr-before-timeout" result.StdErr
        Assert.contains "timeout message preserved" "Process timed out after 3000 ms." result.StdErr)

let testSecretRedactionProtectsAgentTranscriptAndRequestFile () =
    withTempDir "secret-redaction-agent" (fun root ->
        writeIssue
            root
            "DEMO-SECRET"
            "Secret redaction"
            1
            "Todo"
            [ "Acceptance" ]
            [ "Validation" ]
            [ "Constraint" ]
            "Secret redaction description"
        |> ignore

        let secretValue = "super-secret-value-123"

        withEnvironmentVariable "DEMO_SECRET_TOKEN" secretValue (fun () ->
            let agentScriptPath =
                writeFile
                    (Path.Combine(root, "LeakAgent.fsx"))
                    (String.concat
                        Environment.NewLine
                        [ "open System"
                          "let args = fsi.CommandLineArgs |> Array.toList"
                          "let secret = args |> List.last"
                          "printfn \"stdout secret=%s\" secret"
                          "eprintfn \"stderr secret=%s\" secret"
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
                      "  - $DEMO_SECRET_TOKEN" ]
                    "Prompt body"

            let workflow = loadWorkflow workflowPath
            Assert.any "sensitive values captured from workflow" ((=) secretValue) workflow.Config.SensitiveValues

            match Orchestrator.runIssueById workflow "DEMO-SECRET" with
            | Ok recordPath -> Assert.fail (sprintf "Expected agent failure, but run succeeded: %s" recordPath)
            | Error error ->
                Assert.notContains "agent error summary redacts secret" secretValue error

            let record = loadRunRecord (singleRunRecordPath root)
            let requestPath = record.EvidencePaths |> List.find (hasFileName "agent-request.md")
            let transcriptPath = record.EvidencePaths |> List.find (hasFileName "agent-output.txt")
            let requestText = File.ReadAllText requestPath
            let transcriptText = File.ReadAllText transcriptPath

            Assert.notContains "request file redacts secret by omission" secretValue requestText
            Assert.notContains "run summary redacts secret" secretValue record.Summary
            Assert.notContains "transcript redacts secret" secretValue transcriptText
            Assert.contains "transcript shows redaction token" "[REDACTED]" transcriptText))

let testSecretRedactionProtectsHookFailureSummary () =
    withTempDir "secret-redaction-hook" (fun root ->
        writeIssue
            root
            "DEMO-HOOK-SECRET"
            "Hook secret redaction"
            1
            "Todo"
            [ "Acceptance" ]
            [ "Validation" ]
            [ "Constraint" ]
            "Hook secret description"
        |> ignore

        let secretValue = "hook-secret-value-456"

        withEnvironmentVariable "DEMO_SECRET_TOKEN" secretValue (fun () ->
            let hookScriptPath =
                writeFile
                    (Path.Combine(root, "LeakHook.fsx"))
                    (String.concat
                        Environment.NewLine
                        [ "open System"
                          "open System.IO"
                          "let args = fsi.CommandLineArgs |> Array.toList"
                          "let workspace = args |> List.pairwise |> List.tryPick (fun (flag, value) -> if flag = \"--workspace\" then Some value else None) |> Option.defaultValue \".\" |> Path.GetFullPath"
                          "let secret = args |> List.last"
                          "let harnessDir = Path.Combine(workspace, \".harness\")"
                          "Directory.CreateDirectory(harnessDir) |> ignore"
                          "File.WriteAllText(Path.Combine(harnessDir, \"hook-redaction.txt\"), \"hook ran\")"
                          "eprintfn \"hook secret=%s\" secret"
                          "Environment.Exit 4" ])

            let workflowPath =
                writeWorkflow
                    root
                    [ "tracker.path: ./tracker/issues"
                      "workspace.root: ./.workspaces"
                      "agent.command: dry-run"
                      sprintf "hooks.before_run: dotnet fsi \"%s\" --workspace . $DEMO_SECRET_TOKEN" hookScriptPath ]
                    "Prompt body"

            let workflow = loadWorkflow workflowPath

            match Orchestrator.runIssueById workflow "DEMO-HOOK-SECRET" with
            | Ok recordPath -> Assert.fail (sprintf "Expected hook failure, but run succeeded: %s" recordPath)
            | Error error ->
                Assert.notContains "hook error redacts secret" secretValue error
                Assert.contains "hook error shows redaction token" "[REDACTED]" error

            let record = loadRunRecord (singleRunRecordPath root)
            Assert.notContains "hook run summary redacts secret" secretValue record.Summary
            Assert.contains "hook run summary shows redaction token" "[REDACTED]" record.Summary))

let testPollOnceDispatchesUpToMaxConcurrencyInParallel () =
    withTempDir "poll-once-concurrency" (fun root ->
        writeIssue root "DEMO-P1" "Parallel one" 1 "Todo" [ "Acceptance" ] [ "Validation" ] [ "Constraint" ] "Parallel one" |> ignore
        writeIssue root "DEMO-P2" "Parallel two" 1 "Todo" [ "Acceptance" ] [ "Validation" ] [ "Constraint" ] "Parallel two" |> ignore
        writeIssue root "DEMO-P3" "Queued third" 2 "Todo" [ "Acceptance" ] [ "Validation" ] [ "Constraint" ] "Queued third" |> ignore

        let signalDir = ensureDirectory (Path.Combine(root, "coordination"))

        let agentScriptPath =
            writeFile
                (Path.Combine(root, "ParallelAgent.fsx"))
                (String.concat
                    Environment.NewLine
                    [ "open System"
                      "open System.IO"
                      "open System.Threading"
                      "let args = fsi.CommandLineArgs |> Array.toList"
                      "let tryGet flag = args |> List.pairwise |> List.tryPick (fun (current, next) -> if current = flag then Some next else None)"
                      "let workspace = tryGet \"--workspace\" |> Option.defaultValue \".\" |> Path.GetFullPath"
                      "let issue = tryGet \"--issue\" |> Option.defaultValue \"UNKNOWN\""
                      "let signalDir ="
                      "    match tryGet \"--signal-dir\" with"
                      "    | Some value -> value"
                      "    | None -> failwith \"missing signal dir\""
                      "Directory.CreateDirectory(signalDir) |> ignore"
                      "File.WriteAllText(Path.Combine(signalDir, issue + \".started\"), workspace)"
                      "let deadline = DateTime.UtcNow.AddSeconds(6.0)"
                      "let mutable ready = false"
                      "while not ready && DateTime.UtcNow < deadline do"
                      "    ready <- Directory.EnumerateFiles(signalDir, \"*.started\") |> Seq.length >= 2"
                      "    if not ready then Thread.Sleep(50)"
                      "if not ready then"
                      "    eprintfn \"parallel start was not observed\""
                      "    Environment.Exit 9"
                      "let harnessDir = Path.Combine(workspace, \".harness\")"
                      "Directory.CreateDirectory(harnessDir) |> ignore"
                      "File.WriteAllText(Path.Combine(harnessDir, issue + \"-parallel.txt\"), \"parallel dispatch observed\")" ])

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "orchestrator.max_concurrency: 2"
                  "agent.command: dotnet"
                  "agent.args:"
                  "  - fsi"
                  sprintf "  - %s" agentScriptPath
                  "  - --workspace"
                  "  - {workspace}"
                  "  - --issue"
                  "  - {issue_id}"
                  "  - --signal-dir"
                  sprintf "  - %s" signalDir ]
                "Prompt body"

        let workflow = loadWorkflow workflowPath
        Assert.equal "workflow valid" [] (Workflow.validate workflow)

        let recordPaths = Orchestrator.pollOnce workflow |> getOk "pollOnce"

        Assert.equal "pollOnce should dispatch two issues" 2 recordPaths.Length
        Assert.isTrue "first concurrent marker exists" (File.Exists(Path.Combine(signalDir, "DEMO-P1.started")))
        Assert.isTrue "second concurrent marker exists" (File.Exists(Path.Combine(signalDir, "DEMO-P2.started")))
        Assert.isTrue "third issue should remain queued" (not (File.Exists(Path.Combine(signalDir, "DEMO-P3.started"))))
        Assert.isTrue "third workspace should not be created" (not (Directory.Exists(Path.Combine(root, ".workspaces", "DEMO-P3")))))

let testHostServeRetriesWritesStatusAndAvoidsRedispatchAfterSuccess () =
    withTempDir "host-retry" (fun root ->
        writeIssue root "DEMO-RETRY" "Retry issue" 1 "Todo" [ "Acceptance" ] [ "Validation" ] [ "Constraint" ] "Retry body"
        |> ignore

        let agentScriptPath =
            writeFile
                (Path.Combine(root, "RetryAgent.fsx"))
                (String.concat
                    Environment.NewLine
                    [ "open System"
                      "open System.IO"
                      "let args = fsi.CommandLineArgs |> Array.toList"
                      "let tryGet flag = args |> List.pairwise |> List.tryPick (fun (current, next) -> if current = flag then Some next else None)"
                      "let workspace = tryGet \"--workspace\" |> Option.defaultValue \".\" |> Path.GetFullPath"
                      "let issue = tryGet \"--issue\" |> Option.defaultValue \"UNKNOWN\""
                      "let harnessDir = Path.Combine(workspace, \".harness\")"
                      "Directory.CreateDirectory(harnessDir) |> ignore"
                      "let attemptsPath = Path.Combine(harnessDir, \"attempt-count.txt\")"
                      "let previous = if File.Exists(attemptsPath) then Int32.Parse(File.ReadAllText(attemptsPath)) else 0"
                      "let currentAttempt = previous + 1"
                      "File.WriteAllText(attemptsPath, string currentAttempt)"
                      "printfn \"attempt=%d\" currentAttempt"
                      "if currentAttempt < 2 then"
                      "    eprintfn \"failing attempt %d\" currentAttempt"
                      "    Environment.Exit 7"
                      "File.WriteAllText(Path.Combine(harnessDir, issue + \"-success.txt\"), sprintf \"success-attempt=%d\" currentAttempt)" ])

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "orchestrator.poll_interval_seconds: 1"
                  "orchestrator.max_attempts: 2"
                  "agent.command: dotnet"
                  "agent.args:"
                  "  - fsi"
                  sprintf "  - %s" agentScriptPath
                  "  - --workspace"
                  "  - {workspace}"
                  "  - --issue"
                  "  - {issue_id}" ]
                "Retry prompt"

        let workflow = loadWorkflow workflowPath
        Assert.equal "host retry workflow valid" [] (Workflow.validate workflow)

        withStartedHost workflow (fun cancellation ->
            waitUntil "host retry run records" 12000 100 (fun () -> runRecordPaths root |> List.length >= 2)
            waitUntil "host retry snapshot becomes idle" 12000 100 (fun () ->
                let snapshot = readHostStatus workflow
                List.isEmpty snapshot.RunningIssues && List.isEmpty snapshot.RetryingIssues)

            Thread.Sleep 1300
            Assert.equal "host should not redispatch unchanged successful issue" 2 (runRecordPaths root |> List.length)
            cancellation.Cancel())

        let records =
            loadAllRunRecords root |> List.sortBy (fun record -> record.AttemptNumber, record.Status)

        Assert.equal "retry run count" 2 records.Length
        Assert.equal "first retry attempt number" 1 (records |> List.item 0).AttemptNumber
        Assert.equal "first retry status" "Failed" (records |> List.item 0).Status
        Assert.equal "second retry attempt number" 2 (records |> List.item 1).AttemptNumber
        Assert.equal "second retry status" "Succeeded" (records |> List.item 1).Status
        Assert.equal "second retry turn number" 1 (records |> List.item 1).TurnNumber

        let logText = File.ReadAllText(Observability.hostLogPath workflow)
        Assert.contains "host retry log contains retry scheduling" "\"EventType\":\"retry_scheduled\"" logText)

let testHostReconciliationCancelsTerminalIssueAndCleansWorkspace () =
    withTempDir "host-reconcile" (fun root ->
        writeIssue root "DEMO-CANCEL" "Terminal transition" 1 "Todo" [ "Acceptance" ] [ "Validation" ] [ "Constraint" ] "Cancel body"
        |> ignore

        let agentScriptPath =
            writeFile
                (Path.Combine(root, "LongRunningAgent.fsx"))
                (String.concat
                    Environment.NewLine
                    [ "open System"
                      "open System.IO"
                      "open System.Threading"
                      "let args = fsi.CommandLineArgs |> Array.toList"
                      "let tryGet flag = args |> List.pairwise |> List.tryPick (fun (current, next) -> if current = flag then Some next else None)"
                      "let workspace = tryGet \"--workspace\" |> Option.defaultValue \".\" |> Path.GetFullPath"
                      "let harnessDir = Path.Combine(workspace, \".harness\")"
                      "Directory.CreateDirectory(harnessDir) |> ignore"
                      "File.WriteAllText(Path.Combine(harnessDir, \"started.txt\"), \"started\")"
                      "printfn \"agent started\""
                      "Console.Out.Flush()"
                      "Thread.Sleep(30000)" ])

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "workspace.cleanup_terminal: true"
                  "orchestrator.poll_interval_seconds: 1"
                  "agent.command: dotnet"
                  "agent.args:"
                  "  - fsi"
                  sprintf "  - %s" agentScriptPath
                  "  - --workspace"
                  "  - {workspace}" ]
                "Cancellation prompt"

        let workflow = loadWorkflow workflowPath
        Assert.equal "host reconcile workflow valid" [] (Workflow.validate workflow)

        withStartedHost workflow (fun cancellation ->
            waitUntil "host sees running issue" 12000 100 (fun () ->
                match tryReadHostStatusSnapshot workflow with
                | Some snapshot -> snapshot.RunningIssues |> List.exists (fun issue -> issue.IssueId = "DEMO-CANCEL")
                | None -> false)

            writeIssue
                root
                "DEMO-CANCEL"
                "Terminal transition"
                1
                "Done"
                [ "Acceptance" ]
                [ "Validation" ]
                [ "Constraint" ]
                "Cancel body"
            |> ignore

            waitUntil "terminal transition produces run record" 12000 100 (fun () -> runRecordPaths root |> List.length = 1)
            waitUntil
                "terminal transition cleans workspace"
                12000
                100
                (fun () -> not (Directory.Exists(Path.Combine(root, ".workspaces", "DEMO-CANCEL"))))

            cancellation.Cancel())

        let record = loadRunRecord (singleRunRecordPath root)
        Assert.equal "terminal reconciliation status" "Cancelled" record.Status
        Assert.contains "terminal reconciliation summary" "terminal state 'Done'" record.Summary)

let testHostReloadAppliesValidChangesAndPreservesLastKnownGoodOnInvalidReload () =
    withTempDir "host-reload" (fun root ->
        ensureDirectory (Path.Combine(root, "tracker", "issues")) |> ignore
        let requestCaptureAgentPath =
            writeFile
                (Path.Combine(root, "CaptureRequestAgent.fsx"))
                (String.concat
                    Environment.NewLine
                    [ "open System"
                      "open System.IO"
                      "let args = fsi.CommandLineArgs |> Array.toList"
                      "let tryGet flag = args |> List.pairwise |> List.tryPick (fun (current, next) -> if current = flag then Some next else None)"
                      "let workspace = tryGet \"--workspace\" |> Option.defaultValue \".\" |> Path.GetFullPath"
                      "let issue = tryGet \"--issue\" |> Option.defaultValue \"UNKNOWN\""
                      "let requestPath ="
                      "    match tryGet \"--request\" with"
                      "    | Some value -> value"
                      "    | None -> failwith \"missing request\""
                      "let harnessDir = Path.Combine(workspace, \".harness\")"
                      "Directory.CreateDirectory(harnessDir) |> ignore"
                      "File.WriteAllText(Path.Combine(harnessDir, issue + \"-captured-request.txt\"), File.ReadAllText(requestPath))" ])

        let workflowPath =
            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "orchestrator.poll_interval_seconds: 1"
                  "agent.command: dotnet"
                  "agent.args:"
                  "  - fsi"
                  sprintf "  - %s" requestCaptureAgentPath
                  "  - --workspace"
                  "  - {workspace}"
                  "  - --issue"
                  "  - {issue_id}"
                  "  - --request"
                  "  - {request_path}" ]
                "Prompt version one"

        let workflow = loadWorkflow workflowPath
        Assert.equal "host reload workflow valid" [] (Workflow.validate workflow)

        writeIssue root "DEMO-RELOAD-1" "Reload one" 1 "Todo" [ "Acceptance" ] [ "Validation" ] [ "Constraint" ] "Reload body one"
        |> ignore

        withStartedHost workflow (fun cancellation ->
            waitUntil "first reload run completes" 12000 100 (fun () -> runRecordPaths root |> List.length >= 1)

            let firstCapturedRequest =
                Path.Combine(root, ".workspaces", "DEMO-RELOAD-1", ".harness", "DEMO-RELOAD-1-captured-request.txt")

            Assert.contains "initial prompt version captured" "Prompt version one" (File.ReadAllText firstCapturedRequest)

            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "orchestrator.poll_interval_seconds: 1"
                  "agent.command: dotnet"
                  "agent.args:"
                  "  - fsi"
                  sprintf "  - %s" requestCaptureAgentPath
                  "  - --workspace"
                  "  - {workspace}"
                  "  - --issue"
                  "  - {issue_id}"
                  "  - --request"
                  "  - {request_path}" ]
                "Prompt version two"
            |> ignore

            waitUntil "valid workflow reload observed" 12000 100 (fun () ->
                let logPath = Observability.hostLogPath workflow

                File.Exists logPath)

            waitUntil "workflow reloaded entry observed" 12000 100 (fun () ->
                File.ReadAllText(Observability.hostLogPath workflow).Contains("\"EventType\":\"workflow_reloaded\"", StringComparison.Ordinal))

            writeIssue root "DEMO-RELOAD-2" "Reload two" 1 "Todo" [ "Acceptance" ] [ "Validation" ] [ "Constraint" ] "Reload body two"
            |> ignore

            waitUntil "second reload run completes" 12000 100 (fun () -> runRecordPaths root |> List.length >= 2)

            let secondCapturedRequest =
                Path.Combine(root, ".workspaces", "DEMO-RELOAD-2", ".harness", "DEMO-RELOAD-2-captured-request.txt")

            Assert.contains "reloaded prompt version applied" "Prompt version two" (File.ReadAllText secondCapturedRequest)

            writeWorkflow
                root
                [ "tracker.path: ./tracker/issues"
                  "workspace.root: ./.workspaces"
                  "orchestrator.poll_interval_seconds: 1"
                  "agent.command: dotnet"
                  "agent.args:"
                  "  - fsi"
                  sprintf "  - %s" requestCaptureAgentPath
                  "  - --workspace"
                  "  - {workspace}"
                  "  - --issue"
                  "  - {issue_id}"
                  "  - --request"
                  "  - {request_path}" ]
                "Broken {{issue.unsupported}} prompt"
            |> ignore

            waitUntil "invalid workflow reload observed" 12000 100 (fun () ->
                let snapshot = readHostStatus workflow

                snapshot.LastReloadError
                |> Option.exists (fun error -> error.Contains("unsupported variable 'issue.unsupported'", StringComparison.Ordinal)))

            writeIssue root "DEMO-RELOAD-3" "Reload three" 1 "Todo" [ "Acceptance" ] [ "Validation" ] [ "Constraint" ] "Reload body three"
            |> ignore

            waitUntil "third reload run completes" 12000 100 (fun () -> runRecordPaths root |> List.length >= 3)

            let thirdCapturedRequest =
                Path.Combine(root, ".workspaces", "DEMO-RELOAD-3", ".harness", "DEMO-RELOAD-3-captured-request.txt")

            let thirdRequestText = File.ReadAllText thirdCapturedRequest
            Assert.contains "last-known-good prompt preserved" "Prompt version two" thirdRequestText
            Assert.notContains "invalid prompt body not applied" "Broken" thirdRequestText

            cancellation.Cancel())
        )

type TestCase =
    { Name: string
      Run: unit -> unit }

let tests =
    [ { Name = "Workflow loading resolves defaults, env, and paths"
        Run = testWorkflowLoadDefaultsAndEnvResolution }
      { Name = "Workflow validation rejects misleading local config"
        Run = testWorkflowValidationRejectsMisleadingLocalConfig }
      { Name = "Workflow validation supports Linear tracker config"
        Run = testWorkflowValidationSupportsLinearTrackerConfig }
      { Name = "Prompt template validation and strict rendering are enforced"
        Run = testPromptTemplateValidationAndStrictRendering }
      { Name = "Issue parsing and ordering is deterministic"
        Run = testIssueParsingAndOrdering }
      { Name = "Tracker seam preserves file-backed lookup, refresh, and terminal behavior"
        Run = testTrackerSeamSupportsLookupRefreshAndTerminalEnumeration }
      { Name = "Linear tracker supports pagination normalization and refresh"
        Run = testLinearTrackerSupportsPaginationNormalizationAndRefresh }
      { Name = "Linear tracker maps GraphQL errors"
        Run = testLinearTrackerMapsGraphQLErrors }
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
      { Name = "After-run failure is recorded and fails the run"
        Run = testAfterRunFailureIsRecordedAndFailsTheRun }
      { Name = "Run path allows retries but rejects unsupported turns"
        Run = testRunPathAllowsRetriesButRejectsUnsupportedTurns }
      { Name = "Run path rejects non-active issue states"
        Run = testRunIssueRejectsNonActiveIssueState }
      { Name = "Agent failure preserves request and transcript evidence"
        Run = testAgentFailurePreservesRequestAndTranscriptEvidence }
      { Name = "Agent timeout preserves request and transcript evidence"
        Run = testAgentTimeoutPreservesRequestAndTranscriptEvidence }
      { Name = "ProcessRunner timeout preserves captured output"
        Run = testProcessRunnerTimeoutPreservesCapturedOutput }
      { Name = "Secret redaction protects agent transcript and request artifacts"
        Run = testSecretRedactionProtectsAgentTranscriptAndRequestFile }
      { Name = "Secret redaction protects hook failure summaries"
        Run = testSecretRedactionProtectsHookFailureSummary }
      { Name = "poll-once dispatches up to max concurrency in parallel"
        Run = testPollOnceDispatchesUpToMaxConcurrencyInParallel }
      { Name = "Host serve retries writes status and avoids redispatch after success"
        Run = testHostServeRetriesWritesStatusAndAvoidsRedispatchAfterSuccess }
      { Name = "Host reconciliation cancels terminal work and cleans workspaces"
        Run = testHostReconciliationCancelsTerminalIssueAndCleansWorkspace }
      { Name = "Host reload applies valid changes and preserves last-known-good on invalid reload"
        Run = testHostReloadAppliesValidChangesAndPreservesLastKnownGoodOnInvalidReload } ]

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
