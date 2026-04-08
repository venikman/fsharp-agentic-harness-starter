namespace DeliveryHarness.Core

open System

module OperatorOutput =

    let private redactionToken = "[REDACTED]"

    let private normalizeSensitiveValues (values: string list) =
        values
        |> List.filter (fun value -> not (String.IsNullOrWhiteSpace value))
        |> List.distinct
        |> List.sortByDescending String.length

    let redactSensitiveValues (sensitiveValues: string list) (text: string) =
        if String.IsNullOrEmpty text then
            text
        else
            normalizeSensitiveValues sensitiveValues
            |> List.fold (fun (state: string) value -> state.Replace(value, redactionToken, StringComparison.Ordinal)) text

    let redactForWorkflow (workflow: WorkflowDefinition) (text: string) =
        redactSensitiveValues workflow.Config.SensitiveValues text
