namespace DeliveryHarness.Core

open System
open System.IO
open System.Text.Json

module Observability =

    let private fileLock = obj ()
    let private statusOptions = JsonSerializerOptions(WriteIndented = true)

    let private runtimeRoot (workflow: WorkflowDefinition) =
        let path = Path.Combine(workflow.Config.ProjectRoot, ".harness", "runtime")
        Directory.CreateDirectory path |> ignore
        path

    let hostLogPath (workflow: WorkflowDefinition) =
        Path.Combine(runtimeRoot workflow, "host-events.jsonl")

    let statusPath (workflow: WorkflowDefinition) =
        Path.Combine(runtimeRoot workflow, "status.json")

    let appendLog (workflow: WorkflowDefinition) (entry: RuntimeLogEntry) =
        let path = hostLogPath workflow
        let line = JsonSerializer.Serialize(entry)

        lock fileLock (fun () ->
            File.AppendAllText(path, line + Environment.NewLine))

        path

    let writeStatus (workflow: WorkflowDefinition) (snapshot: HostStatusSnapshot) =
        let path = statusPath workflow

        lock fileLock (fun () ->
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, statusOptions)))

        path

    let tryReadStatus (workflow: WorkflowDefinition) =
        let path = statusPath workflow

        if not (File.Exists path) then
            Ok None
        else
            try
                JsonSerializer.Deserialize<HostStatusSnapshot>(File.ReadAllText path, statusOptions)
                |> Some
                |> Ok
            with ex ->
                Error(sprintf "Could not read host status snapshot '%s': %s" path ex.Message)
