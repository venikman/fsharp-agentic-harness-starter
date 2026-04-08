module Common

open System
open System.IO

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

let writeStamp path lines =
    File.WriteAllText(path, String.concat Environment.NewLine lines)
