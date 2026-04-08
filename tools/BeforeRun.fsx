#load "Common.fsx"

open System
open System.IO
open Common

let workspace = getRequired "--workspace" |> Path.GetFullPath
let harnessDir = Path.Combine(workspace, ".harness")
ensureDirectory harnessDir |> ignore

let afterCreateStamp = Path.Combine(harnessDir, "after-create.txt")

if File.Exists afterCreateStamp then
    let stamp = File.ReadAllText afterCreateStamp

    if stamp.Contains("status=failed", StringComparison.Ordinal) then
        failwith "Workspace bootstrap previously failed. Remove the workspace and rerun the issue."

writeStamp
    (Path.Combine(harnessDir, "before-run.txt"))
    [ sprintf "workspace=%s" workspace
      sprintf "utc=%O" DateTimeOffset.UtcNow ]
