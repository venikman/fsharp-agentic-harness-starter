namespace DeliveryHarness.Core

open System
open System.Text.RegularExpressions

module PromptTemplate =

    let private placeholderPattern =
        Regex("{{\\s*([a-z0-9_.]+)\\s*}}", RegexOptions.Compiled)

    let supportedVariables =
        [ "issue.id"
          "issue.title"
          "issue.state"
          "issue.description"
          "issue.acceptance"
          "issue.validation"
          "issue.constraints"
          "attempt.number"
          "turn.number"
          "tracker.kind" ]

    let private supportedVariableSet = supportedVariables |> Set.ofList

    let private containsTemplateSyntax (template: string) =
        template.Contains("{{", StringComparison.Ordinal)
        || template.Contains("}}", StringComparison.Ordinal)

    let private asBulletList (items: string list) =
        items
        |> List.map (fun item -> sprintf "- %s" item)
        |> String.concat Environment.NewLine

    let private legacyBulletSection title items =
        if List.isEmpty items then
            ""
        else
            sprintf
                "## %s%s%s%s%s"
                title
                Environment.NewLine
                (asBulletList items)
                Environment.NewLine
                Environment.NewLine

    let usesTemplateSyntax (template: string) = containsTemplateSyntax template

    let validate (template: string) =
        if not (containsTemplateSyntax template) then
            []
        else
            let matches =
                placeholderPattern.Matches(template)
                |> Seq.cast<Match>
                |> Seq.toList

            let unknownVariables =
                matches
                |> List.map (fun item -> item.Groups.[1].Value)
                |> List.distinct
                |> List.filter (fun item -> not (supportedVariableSet.Contains item))

            let templateWithoutPlaceholders =
                placeholderPattern.Replace(template, "")

            [ if
                  templateWithoutPlaceholders.Contains("{{", StringComparison.Ordinal)
                  || templateWithoutPlaceholders.Contains("}}", StringComparison.Ordinal)
              then
                  yield "Workflow prompt template contains malformed '{{ ... }}' markers."

              for variableName in unknownVariables do
                  yield sprintf "Workflow prompt template references unsupported variable '%s'." variableName ]

    let private templateValues (workflow: WorkflowDefinition) (issue: TrackerIssue) attemptNumber turnNumber =
        [ "issue.id", issue.Id
          "issue.title", issue.Title
          "issue.state", issue.State.AsText
          "issue.description", issue.Description
          "issue.acceptance", asBulletList issue.Acceptance
          "issue.validation", asBulletList issue.Validation
          "issue.constraints", asBulletList issue.Constraints
          "attempt.number", string attemptNumber
          "turn.number", string turnNumber
          "tracker.kind", workflow.Config.TrackerKind ]
        |> Map.ofList

    let private renderLegacyPrompt (workflow: WorkflowDefinition) (issue: TrackerIssue) attemptNumber turnNumber =
        [ "# Assigned issue"
          sprintf "- id: %s" issue.Id
          sprintf "- title: %s" issue.Title
          sprintf "- state: %s" issue.State.AsText
          sprintf "- attempt: %d" attemptNumber
          sprintf "- turn: %d" turnNumber
          sprintf "- source: %s tracker issue" workflow.Config.TrackerKind
          ""
          "## Problem"
          issue.Description
          ""
          legacyBulletSection "Acceptance" issue.Acceptance
          legacyBulletSection "Validation" issue.Validation
          legacyBulletSection "Constraints" issue.Constraints
          "# Workflow contract"
          workflow.PromptTemplate ]
        |> String.concat Environment.NewLine

    let render (workflow: WorkflowDefinition) (issue: TrackerIssue) attemptNumber turnNumber =
        let template = workflow.PromptTemplate
        let errors = validate template

        if not (List.isEmpty errors) then
            Error(String.concat Environment.NewLine errors)
        elif not (containsTemplateSyntax template) then
            Ok(renderLegacyPrompt workflow issue attemptNumber turnNumber)
        else
            let values = templateValues workflow issue attemptNumber turnNumber

            placeholderPattern.Replace(
                template,
                MatchEvaluator(fun item ->
                    let variableName = item.Groups.[1].Value
                    values |> Map.tryFind variableName |> Option.defaultValue "")
            )
            |> Ok
