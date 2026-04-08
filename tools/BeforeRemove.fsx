#load "Common.fsx"

open System
open System.IO
open Common

let workspace = getRequired "--workspace" |> Path.GetFullPath
let harnessDir = Path.Combine(workspace, ".harness")
ensureDirectory harnessDir |> ignore

writeStamp
    (Path.Combine(harnessDir, "before-remove.txt"))
    [ sprintf "workspace=%s" workspace
      sprintf "utc=%O" DateTimeOffset.UtcNow ]
