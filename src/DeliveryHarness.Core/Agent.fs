namespace DeliveryHarness.Core

open System
open System.IO

module Agent =

    let private formatBulletSection title items =
        if List.isEmpty items then
            ""
        else
            let body =
                items
                |> List.map (fun item -> sprintf "- %s" item)
                |> String.concat Environment.NewLine

            sprintf "## %s%s%s%s%s" title Environment.NewLine body Environment.NewLine Environment.NewLine

    let private buildPrompt (workflow: WorkflowDefinition) (issue: TrackerIssue) =
        [ "# Assigned issue"
          sprintf "- id: %s" issue.Id
          sprintf "- title: %s" issue.Title
          sprintf "- state: %s" issue.State.AsText
          sprintf "- source: %s" issue.SourcePath
          ""
          "## Problem"
          issue.Description
          ""
          formatBulletSection "Acceptance" issue.Acceptance
          formatBulletSection "Validation" issue.Validation
          formatBulletSection "Constraints" issue.Constraints
          "# Workflow contract"
          workflow.PromptTemplate ]
        |> String.concat Environment.NewLine

    let private writeRequestFile (workspace: WorkspaceInfo) (prompt: string) =
        let harnessDir = Path.Combine(workspace.Path, ".harness")
        Directory.CreateDirectory harnessDir |> ignore

        let requestPath = Path.Combine(harnessDir, "agent-request.md")
        File.WriteAllText(requestPath, prompt)
        requestPath

    let private replaceTokens (tokens: (string * string) list) (value: string) =
        tokens
        |> List.fold (fun (state: string) (token, replacement) -> state.Replace(token, replacement)) value

    let private finish succeeded summary evidencePaths transcriptPath =
        { Succeeded = succeeded
          Summary = summary
          EvidencePaths = evidencePaths
          TranscriptPath = transcriptPath }

    let execute (workflow: WorkflowDefinition) (issue: TrackerIssue) (workspace: WorkspaceInfo) : AgentOutcome =
        match Workspace.ensureWorkspacePath workflow workspace.Path with
        | Error error -> finish false error [] None
        | Ok workspacePath ->
            let workspace = { workspace with Path = workspacePath }
            let prompt = buildPrompt workflow issue
            let requestPath = writeRequestFile workspace prompt
            let harnessDir = Path.Combine(workspace.Path, ".harness")
            Directory.CreateDirectory harnessDir |> ignore

            if String.Equals(workflow.Config.AgentCommand, "dry-run", StringComparison.OrdinalIgnoreCase) then
                let transcriptPath = Path.Combine(harnessDir, "dry-run.txt")

                File.WriteAllText(
                    transcriptPath,
                    String.concat
                        Environment.NewLine
                        [ "Dry run agent executed."
                          sprintf "Issue: %s" issue.Id
                          sprintf "Workspace: %s" workspace.Path
                          sprintf "Request file: %s" requestPath
                          "Replace agent.command in WORKFLOW.md with your real coding-agent CLI when ready." ])

                finish
                    true
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
                let result = ProcessRunner.run workspace.Path workflow.Config.AgentTimeoutMs workflow.Config.AgentCommand args
                let transcriptPath = Path.Combine(harnessDir, "agent-output.txt")

                File.WriteAllText(
                    transcriptPath,
                    String.concat
                        Environment.NewLine
                        [ "stdout:"
                          result.StdOut
                          ""
                          "stderr:"
                          result.StdErr ])

                let evidencePaths = [ requestPath; transcriptPath ]

                if result.TimedOut then
                    finish
                        false
                        (sprintf "Agent command timed out after %d ms. See %s." workflow.Config.AgentTimeoutMs transcriptPath)
                        evidencePaths
                        (Some transcriptPath)
                elif result.ExitCode <> 0 then
                    finish
                        false
                        (sprintf "Agent command exited with code %d. See %s." result.ExitCode transcriptPath)
                        evidencePaths
                        (Some transcriptPath)
                else
                    finish true "Agent command finished successfully." evidencePaths (Some transcriptPath)
