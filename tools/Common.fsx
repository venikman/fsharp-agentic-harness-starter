module Common

open System
open System.Diagnostics
open System.IO

type ProcessResult =
    { ExitCode: int
      StdOut: string
      StdErr: string }

let args =
    fsi.CommandLineArgs
    |> Array.skip 1
    |> Array.toList

let rec private findPair name values =
    match values with
    | flag :: value :: rest when String.Equals(flag, name, StringComparison.OrdinalIgnoreCase) -> Some value
    | _ :: rest -> findPair name rest
    | [] -> None

let getOptional name =
    findPair name args

let getRequired name =
    match getOptional name with
    | Some value -> value
    | None -> failwithf "Missing required argument %s" name

let ensureDirectory path =
    Directory.CreateDirectory path |> ignore
    path

let writeStamp (path: string) lines =
    let directory = Path.GetDirectoryName(path)

    if not (String.IsNullOrWhiteSpace directory) then
        ensureDirectory directory |> ignore

    File.WriteAllText(path, String.concat Environment.NewLine lines)

let runProcess (cwd: string) (fileName: string) (arguments: string list) =
    let startInfo = ProcessStartInfo()
    startInfo.FileName <- fileName
    startInfo.WorkingDirectory <- cwd
    startInfo.UseShellExecute <- false
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true

    for argument in arguments do
        startInfo.ArgumentList.Add(argument)

    use proc = new Process()
    proc.StartInfo <- startInfo

    if not (proc.Start()) then
        failwithf "Failed to start process '%s'." fileName

    let stdoutTask = proc.StandardOutput.ReadToEndAsync()
    let stderrTask = proc.StandardError.ReadToEndAsync()
    proc.WaitForExit()

    { ExitCode = proc.ExitCode
      StdOut = stdoutTask.GetAwaiter().GetResult()
      StdErr = stderrTask.GetAwaiter().GetResult() }
