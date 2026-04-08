namespace DeliveryHarness.Core

open System
open System.IO
open System.Threading

module Workspace =

    let sanitizeIdentifier (value: string) =
        value.ToCharArray()
        |> Array.map (fun ch ->
            if Char.IsLetterOrDigit ch || ch = '.' || ch = '_' || ch = '-' then
                ch
            else
                '-')
        |> String

    let private normalizePath (path: string) =
        Path.GetFullPath path |> Path.TrimEndingDirectorySeparator

    let isPathInsideRoot (rootPath: string) (candidatePath: string) =
        let normalizedRoot = normalizePath rootPath
        let normalizedCandidate = normalizePath candidatePath

        String.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase)
        || normalizedCandidate.StartsWith(normalizedRoot + string Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)

    let ensureWorkspacePath (workflow: WorkflowDefinition) (candidatePath: string) =
        let workspaceRoot = Path.GetFullPath workflow.Config.WorkspaceRoot
        let normalizedCandidate = Path.GetFullPath candidatePath

        if isPathInsideRoot workspaceRoot normalizedCandidate then
            Ok normalizedCandidate
        else
            Error (sprintf "Workspace path '%s' is outside workspace root '%s'." normalizedCandidate workspaceRoot)

    let workspacePathForIssue (workflow: WorkflowDefinition) (issueId: string) =
        let key = sanitizeIdentifier issueId
        Path.GetFullPath(Path.Combine(workflow.Config.WorkspaceRoot, key))

    let collectEvidencePaths (workspacePath: string) =
        let harnessDir = Path.Combine(Path.GetFullPath workspacePath, ".harness")

        if not (Directory.Exists harnessDir) then
            []
        else
            Directory.EnumerateFiles(harnessDir, "*", SearchOption.AllDirectories)
            |> Seq.map Path.GetFullPath
            |> Seq.sort
            |> Seq.toList

    let private runHook (workflow: WorkflowDefinition) label cwd timeoutMs scriptOption =
        match scriptOption with
        | None -> Ok ()
        | Some script ->
            let result = ProcessRunner.runShell cwd timeoutMs script CancellationToken.None
            let sanitizedStdErr = OperatorOutput.redactForWorkflow workflow result.StdErr

            if result.TimedOut then
                Error (sprintf "Hook '%s' timed out. stderr: %s" label sanitizedStdErr)
            elif result.ExitCode <> 0 then
                Error (sprintf "Hook '%s' failed. stderr: %s" label sanitizedStdErr)
            else
                Ok ()

    let runHookInWorkspace (workflow: WorkflowDefinition) label workspacePath timeoutMs scriptOption =
        match ensureWorkspacePath workflow workspacePath with
        | Error error -> Error error
        | Ok normalizedWorkspacePath -> runHook workflow label normalizedWorkspacePath timeoutMs scriptOption

    let runHookBestEffortInWorkspace (workflow: WorkflowDefinition) label workspacePath timeoutMs scriptOption =
        match runHookInWorkspace workflow label workspacePath timeoutMs scriptOption with
        | Ok () -> ()
        | Error _ -> ()

    let createOrReuse (workflow: WorkflowDefinition) (issue: TrackerIssue) : Result<WorkspaceInfo, string> =
        let key = sanitizeIdentifier issue.Id
        let workspaceRoot = Path.GetFullPath workflow.Config.WorkspaceRoot

        match ensureWorkspacePath workflow (workspacePathForIssue workflow issue.Id) with
        | Error error -> Error error
        | Ok path ->
            Directory.CreateDirectory workspaceRoot |> ignore
            let createdNow = not (Directory.Exists path)
            Directory.CreateDirectory path |> ignore

            match createdNow with
            | true ->
                match runHookInWorkspace workflow "after_create" path workflow.Config.Hooks.TimeoutMs workflow.Config.Hooks.AfterCreate with
                | Ok () ->
                    Ok
                        { Key = key
                          Path = path
                          CreatedNow = true }
                | Error error -> Error error
            | false ->
                Ok
                    { Key = key
                      Path = path
                      CreatedNow = false }

    let removeIfExists (workflow: WorkflowDefinition) (issueId: string) : Result<unit, string> =
        match ensureWorkspacePath workflow (workspacePathForIssue workflow issueId) with
        | Error error -> Error error
        | Ok path ->
            if not (Directory.Exists path) then
                Ok ()
            else
                match runHookInWorkspace workflow "before_remove" path workflow.Config.Hooks.TimeoutMs workflow.Config.Hooks.BeforeRemove with
                | Error error -> Error error
                | Ok () ->
                    if Directory.Exists path then
                        Directory.Delete(path, true)

                    Ok ()
