#load "Common.fsx"

open System
open System.IO
open Common

let workspace = getRequired "--workspace" |> Path.GetFullPath
let repoRoot = getRequired "--repo" |> Path.GetFullPath
let harnessDir = Path.Combine(workspace, ".harness")

let writeBeforeRemove lines =
    ensureDirectory harnessDir |> ignore

    writeStamp
        (Path.Combine(harnessDir, "before-remove.txt"))
        ([ sprintf "workspace=%s" workspace
           sprintf "repo=%s" repoRoot
           sprintf "utc=%O" DateTimeOffset.UtcNow ]
         @ lines)

let gitMetadataPath = Path.Combine(workspace, ".git")

if File.Exists gitMetadataPath || Directory.Exists gitMetadataPath then
    let removeResult = runProcess repoRoot "git" [ "worktree"; "remove"; "--force"; workspace ]

    if removeResult.ExitCode <> 0 then
        writeBeforeRemove
            [ "strategy=git-worktree"
              sprintf "git_stdout=%s" removeResult.StdOut
              sprintf "git_stderr=%s" removeResult.StdErr ]

        failwithf "git worktree removal failed for '%s'." workspace
else
    writeBeforeRemove
        [ "strategy=directory-delete"
          "note=workspace was not a git worktree; the orchestrator will remove the directory." ]
