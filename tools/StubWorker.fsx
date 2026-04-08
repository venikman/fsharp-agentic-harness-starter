#load "Common.fsx"

open System
open System.IO
open Common

let workspace = getRequired "--workspace" |> Path.GetFullPath
let issueId = getRequired "--issue"
let requestPath = getRequired "--request" |> Path.GetFullPath
let harnessDir = Path.Combine(workspace, ".harness")
ensureDirectory harnessDir |> ignore

if not (File.Exists requestPath) then
    failwithf "Request file '%s' does not exist." requestPath

let requestPreview =
    File.ReadLines requestPath
    |> Seq.truncate 5
    |> String.concat Environment.NewLine

writeStamp
    (Path.Combine(harnessDir, "stub-worker.txt"))
    [ sprintf "workspace=%s" workspace
      sprintf "issue=%s" issueId
      sprintf "request_path=%s" requestPath
      sprintf "utc=%O" DateTimeOffset.UtcNow
      "request_preview="
      requestPreview ]

printfn "stub worker stdout for %s" issueId
eprintfn "stub worker stderr for %s" issueId
