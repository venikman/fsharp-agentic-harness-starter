namespace DeliveryHarness.Core

open System
open System.IO

module Workspace =

    let sanitizeIdentifier (value: string) =
        value.ToCharArray()
        |> Array.map (fun ch ->
            if Char.IsLetterOrDigit ch || ch = '.' || ch = '_' || ch = '-' then
                ch
            else
                '-')
        |> String

    let private ensureInsideRoot (rootPath: string) (candidatePath: string) =
        let normalizedRoot =
            let path = Path.GetFullPath rootPath
            if path.EndsWith(Path.DirectorySeparatorChar) then path else path + string Path.DirectorySeparatorChar

        let normalizedCandidate = Path.GetFullPath candidatePath
        normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)

    let workspacePathForIssue (workflow: WorkflowDefinition) (issueId: string) =
        let key = sanitizeIdentifier issueId
        Path.GetFullPath(Path.Combine(workflow.Config.WorkspaceRoot, key))

    let runHook label cwd timeoutMs scriptOption =
        match scriptOption with
        | None -> Ok ()
        | Some script ->
            let result = ProcessRunner.runShell cwd timeoutMs script

            if result.TimedOut then
                Error (sprintf "Hook '%s' timed out. stderr: %s" label result.StdErr)
            elif result.ExitCode <> 0 then
                Error (sprintf "Hook '%s' failed. stderr: %s" label result.StdErr)
            else
                Ok ()

    let runHookBestEffort label cwd timeoutMs scriptOption =
        match runHook label cwd timeoutMs scriptOption with
        | Ok () -> ()
        | Error _ -> ()

    let createOrReuse (workflow: WorkflowDefinition) (issue: TrackerIssue) : Result<WorkspaceInfo, string> =
        let key = sanitizeIdentifier issue.Id
        let workspaceRoot = Path.GetFullPath workflow.Config.WorkspaceRoot
        let path = workspacePathForIssue workflow issue.Id

        if not (ensureInsideRoot workspaceRoot path) then
            Error (sprintf "Workspace path '%s' is outside workspace root '%s'." path workspaceRoot)
        else
            Directory.CreateDirectory workspaceRoot |> ignore
            let createdNow = not (Directory.Exists path)
            Directory.CreateDirectory path |> ignore

            match createdNow with
            | true ->
                match runHook "after_create" path workflow.Config.Hooks.TimeoutMs workflow.Config.Hooks.AfterCreate with
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
        let path = workspacePathForIssue workflow issueId

        if not (Directory.Exists path) then
            Ok ()
        else
            match runHook "before_remove" path workflow.Config.Hooks.TimeoutMs workflow.Config.Hooks.BeforeRemove with
            | Error error -> Error error
            | Ok () ->
                Directory.Delete(path, true)
                Ok ()
