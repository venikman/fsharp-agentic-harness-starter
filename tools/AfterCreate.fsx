#load "Common.fsx"

open System
open System.IO
open Common

let workspace = getRequired "--workspace" |> Path.GetFullPath
let repoRoot = getRequired "--repo" |> Path.GetFullPath

let excludedNames =
    set
        [ ".git"
          ".workspaces"
          ".harness"
          "bin"
          "obj"
          ".vs"
          ".idea" ]

let rec copyDirectory source target =
    Directory.CreateDirectory target |> ignore

    for file in Directory.GetFiles source do
        let name = Path.GetFileName file

        if not (excludedNames.Contains name) then
            let destination = Path.Combine(target, name)
            File.Copy(file, destination, true)

    for directory in Directory.GetDirectories source do
        let name = Path.GetFileName directory

        if not (excludedNames.Contains name) then
            let destination = Path.Combine(target, name)
            copyDirectory directory destination

copyDirectory repoRoot workspace

let harnessDir = Path.Combine(workspace, ".harness")
ensureDirectory harnessDir |> ignore

writeStamp
    (Path.Combine(harnessDir, "after-create.txt"))
    [ sprintf "workspace=%s" workspace
      sprintf "repo=%s" repoRoot
      sprintf "utc=%O" DateTimeOffset.UtcNow
      "Replace tools/AfterCreate.fsx with your real workspace bootstrap as soon as possible." ]
