namespace DeliveryHarness.Core

open System
open System.IO

module FileTracker =

    let private tryParseIssue (path: string) : Result<TrackerIssue, string> =
        match FrontMatter.parseFile path with
        | Error error ->
            Error (sprintf "Could not parse issue file '%s': %s" path error)
        | Ok doc ->
            let id =
                FrontMatter.tryGetOne "id" doc
                |> Option.defaultValue (Path.GetFileNameWithoutExtension path)

            let title =
                FrontMatter.tryGetOne "title" doc
                |> Option.defaultValue id

            let state =
                FrontMatter.tryGetOne "state" doc
                |> Option.defaultValue "Todo"
                |> IssueState.Parse

            let priority = FrontMatter.getInt "priority" 100 doc

            Ok
                { Id = id
                  Title = title
                  Description = doc.Body
                  State = state
                  Priority = priority
                  Acceptance = FrontMatter.getList "acceptance" doc
                  Validation = FrontMatter.getList "validation" doc
                  Constraints = FrontMatter.getList "constraints" doc
                  UpdatedAtUtc = DateTimeOffset(File.GetLastWriteTimeUtc path)
                  SourcePath = Path.GetFullPath path }

    let listIssues (workflow: WorkflowDefinition) : Result<TrackerIssue list, string> =
        if not (Directory.Exists workflow.Config.TrackerPath) then
            Error (sprintf "Tracker path '%s' does not exist." workflow.Config.TrackerPath)
        else
            let issueFiles =
                Directory.EnumerateFiles(workflow.Config.TrackerPath, "*.md", SearchOption.TopDirectoryOnly)
                |> Seq.filter (fun path -> not (String.Equals(Path.GetFileName path, "README.md", StringComparison.OrdinalIgnoreCase)))
                |> Seq.toList

            let folder (state: Result<TrackerIssue list, string>) (path: string) =
                match state, tryParseIssue path with
                | Error error, _ -> Error error
                | _, Error error -> Error error
                | Ok issues, Ok issue -> Ok (issue :: issues)

            issueFiles
            |> List.fold folder (Ok [])
            |> Result.map (fun issues ->
                issues
                |> List.sortBy (fun issue -> issue.Priority, -issue.UpdatedAtUtc.UtcTicks))

    let tryFindById (workflow: WorkflowDefinition) (issueId: string) : Result<TrackerIssue option, string> =
        listIssues workflow
        |> Result.map (fun issues ->
            issues
            |> List.tryFind (fun issue -> String.Equals(issue.Id, issueId, StringComparison.OrdinalIgnoreCase)))
