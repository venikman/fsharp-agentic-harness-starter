namespace DeliveryHarness.Core

open System
open System.IO
open System.Threading

module Agent =

    let private writeRequestFile (workspace: WorkspaceInfo) (prompt: string) =
        let harnessDir = Path.Combine(workspace.Path, ".harness")
        Directory.CreateDirectory harnessDir |> ignore

        let requestPath = Path.Combine(harnessDir, "agent-request.md")
        File.WriteAllText(requestPath, prompt)
        requestPath

    let private replaceTokens (tokens: (string * string) list) (value: string) =
        tokens
        |> List.fold (fun (state: string) (token, replacement) -> state.Replace(token, replacement)) value

    let private finish succeeded cancelled summary evidencePaths transcriptPath =
        { Succeeded = succeeded
          Cancelled = cancelled
          Summary = summary
          EvidencePaths = evidencePaths
          TranscriptPath = transcriptPath }

    let execute
        (workflow: WorkflowDefinition)
        (issue: TrackerIssue)
        (workspace: WorkspaceInfo)
        attemptNumber
        turnNumber
        (cancellationToken: CancellationToken)
        : AgentOutcome
        =
        match Workspace.ensureWorkspacePath workflow workspace.Path with
        | Error error -> finish false false error [] None
        | Ok workspacePath ->
            let workspace = { workspace with Path = workspacePath }

            if cancellationToken.IsCancellationRequested then
                finish false true "Run cancelled before request generation." [] None
            else
                match PromptTemplate.render workflow issue attemptNumber turnNumber with
                | Error error -> finish false false error [] None
                | Ok prompt ->
                    let requestPath = writeRequestFile workspace prompt
                    let harnessDir = Path.Combine(workspace.Path, ".harness")
                    Directory.CreateDirectory harnessDir |> ignore

                    if cancellationToken.IsCancellationRequested then
                        finish false true "Run cancelled before agent launch." [ requestPath ] None
                    elif String.Equals(workflow.Config.AgentCommand, "dry-run", StringComparison.OrdinalIgnoreCase) then
                        let transcriptPath = Path.Combine(harnessDir, "dry-run.txt")

                        File.WriteAllText(
                            transcriptPath,
                            String.concat
                                Environment.NewLine
                                [ "Dry run agent executed."
                                  sprintf "Issue: %s" issue.Id
                                  sprintf "Attempt: %d" attemptNumber
                                  sprintf "Turn: %d" turnNumber
                                  sprintf "Workspace: %s" workspace.Path
                                  sprintf "Request file: %s" requestPath
                                  "Replace agent.command in WORKFLOW.md with your real coding-agent CLI when ready." ])

                        finish
                            true
                            false
                            "Dry run completed. Replace agent.command with a real coding-agent CLI."
                            [ requestPath; transcriptPath ]
                            (Some transcriptPath)
                    else
                        let tokens =
                            [ "{workspace}", workspace.Path
                              "{issue_id}", issue.Id
                              "{issue_title}", issue.Title
                              "{request_path}", requestPath
                              "{project_root}", workflow.Config.ProjectRoot ]

                        let args = workflow.Config.AgentArgs |> List.map (replaceTokens tokens)

                        let result =
                            ProcessRunner.run
                                workspace.Path
                                workflow.Config.AgentTimeoutMs
                                workflow.Config.AgentCommand
                                args
                                cancellationToken

                        let sanitizedStdOut = OperatorOutput.redactForWorkflow workflow result.StdOut
                        let sanitizedStdErr = OperatorOutput.redactForWorkflow workflow result.StdErr
                        let transcriptPath = Path.Combine(harnessDir, "agent-output.txt")

                        File.WriteAllText(
                            transcriptPath,
                            String.concat
                                Environment.NewLine
                                [ "stdout:"
                                  sanitizedStdOut
                                  ""
                                  "stderr:"
                                  sanitizedStdErr ])

                        let evidencePaths = [ requestPath; transcriptPath ]

                        if result.Cancelled then
                            finish false true (sprintf "Agent command was cancelled. See %s." transcriptPath) evidencePaths (Some transcriptPath)
                        elif result.TimedOut then
                            finish
                                false
                                false
                                (sprintf "Agent command timed out after %d ms. See %s." workflow.Config.AgentTimeoutMs transcriptPath)
                                evidencePaths
                                (Some transcriptPath)
                        elif result.ExitCode <> 0 then
                            finish
                                false
                                false
                                (sprintf "Agent command exited with code %d. See %s." result.ExitCode transcriptPath)
                                evidencePaths
                                (Some transcriptPath)
                        else
                            finish true false "Agent command finished successfully." evidencePaths (Some transcriptPath)
