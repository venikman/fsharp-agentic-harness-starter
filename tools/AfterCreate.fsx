#load "Common.fsx"

open System
open System.IO
open Common

let workspace = getRequired "--workspace" |> Path.GetFullPath
let repoRootInput = getRequired "--repo" |> Path.GetFullPath

let normalizePath path =
    Path.GetFullPath(path) |> Path.TrimEndingDirectorySeparator

let writeOutcome status lines =
    let harnessDir = Path.Combine(workspace, ".harness")
    ensureDirectory harnessDir |> ignore

    writeStamp
        (Path.Combine(harnessDir, "after-create.txt"))
        ([ sprintf "status=%s" status
           sprintf "workspace=%s" workspace
           sprintf "repo_input=%s" repoRootInput
           sprintf "utc=%O" DateTimeOffset.UtcNow ]
         @ lines)

let failWithDetails message lines =
    writeOutcome "failed" ([ sprintf "error=%s" message ] @ lines)
    failwith message

let ensureEmptyDirectory path =
    if Directory.EnumerateFileSystemEntries(path) |> Seq.isEmpty |> not then
        failWithDetails
            (sprintf "Workspace '%s' must be empty before bootstrap." path)
            [ "reuse_policy=leave-existing-workspaces-unchanged" ]

if not (Directory.Exists repoRootInput) then
    failWithDetails (sprintf "Repo root '%s' does not exist." repoRootInput) []

ensureEmptyDirectory workspace

let repoProbe = runProcess repoRootInput "git" [ "rev-parse"; "--show-toplevel" ]

if repoProbe.ExitCode <> 0 then
    failWithDetails
        (sprintf "Repo root '%s' is not a git repository with worktree support." repoRootInput)
        [ sprintf "git_stdout=%s" repoProbe.StdOut
          sprintf "git_stderr=%s" repoProbe.StdErr ]

let repoRoot = repoProbe.StdOut.Trim() |> Path.GetFullPath

if String.Equals(normalizePath workspace, normalizePath repoRoot, StringComparison.OrdinalIgnoreCase) then
    failWithDetails "Workspace path must not equal the repository root." []

let headProbe = runProcess repoRoot "git" [ "rev-parse"; "HEAD" ]

if headProbe.ExitCode <> 0 then
    failWithDetails
        "Could not resolve repository HEAD for workspace bootstrap."
        [ sprintf "git_stdout=%s" headProbe.StdOut
          sprintf "git_stderr=%s" headProbe.StdErr ]

let targetRevision = headProbe.StdOut.Trim()
let addResult = runProcess repoRoot "git" [ "worktree"; "add"; "--detach"; "--force"; workspace; targetRevision ]

if addResult.ExitCode <> 0 then
    failWithDetails
        (sprintf "git worktree bootstrap failed for '%s'." workspace)
        [ sprintf "strategy=git-worktree"
          sprintf "target_revision=%s" targetRevision
          sprintf "git_stdout=%s" addResult.StdOut
          sprintf "git_stderr=%s" addResult.StdErr ]

writeOutcome
    "succeeded"
    [ sprintf "strategy=git-worktree"
      sprintf "repo=%s" repoRoot
      sprintf "target_revision=%s" targetRevision
      "reuse_policy=leave-existing-workspaces-unchanged"
      "reset_policy=none" ]
