namespace DeliveryHarness.Core

open System
open System.IO

type FrontMatterDocument =
    { Fields: Map<string, string list>
      Body: string }

module FrontMatter =

    let private normalizeLineEndings (text: string) =
        text.Replace("\r\n", "\n").Replace("\r", "\n")

    let private trimQuoted (value: string) =
        let trimmed = value.Trim()

        if trimmed.Length >= 2 then
            let first = trimmed.[0]
            let last = trimmed.[trimmed.Length - 1]

            if (first = '"' && last = '"') || (first = '\'' && last = '\'') then
                trimmed.Substring(1, trimmed.Length - 2)
            else
                trimmed
        else
            trimmed

    let parseText (text: string) : Result<FrontMatterDocument, string> =
        let normalized = normalizeLineEndings text

        if not (normalized.StartsWith("---\n", StringComparison.Ordinal)) then
            Ok
                { Fields = Map.empty
                  Body = normalized.Trim() }
        else
            let closingIndex = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal)

            if closingIndex < 0 then
                Error "Front matter started but was not closed with ---"
            else
                let rawFrontMatter = normalized.Substring(4, closingIndex - 4)
                let body = normalized.Substring(closingIndex + 5).Trim()
                let lines = rawFrontMatter.Split('\n')

                let mutable currentListKey: string option = None
                let mutable fields = Map.empty<string, string list>
                let errors = ResizeArray<string>()

                let addListValue key value =
                    let current = fields |> Map.tryFind key |> Option.defaultValue []
                    fields <- fields |> Map.add key (current @ [ value ])

                for index = 0 to lines.Length - 1 do
                    let line = lines.[index].TrimEnd()
                    let trimmed = line.TrimStart()

                    if String.IsNullOrWhiteSpace trimmed || trimmed.StartsWith("#", StringComparison.Ordinal) then
                        ()
                    elif trimmed.StartsWith("- ", StringComparison.Ordinal) then
                        match currentListKey with
                        | Some key -> addListValue key (trimQuoted (trimmed.Substring(2)))
                        | None -> errors.Add(sprintf "Line %d: list item without a key" (index + 1))
                    else
                        let colonIndex = trimmed.IndexOf(':')

                        if colonIndex < 0 then
                            errors.Add(sprintf "Line %d: expected key: value" (index + 1))
                        else
                            let key = trimmed.Substring(0, colonIndex).Trim()
                            let remainder = trimmed.Substring(colonIndex + 1).Trim()

                            if remainder = "" then
                                currentListKey <- Some key

                                if not (fields.ContainsKey key) then
                                    fields <- fields |> Map.add key []
                            else
                                currentListKey <- None
                                fields <- fields |> Map.add key [ trimQuoted remainder ]

                if errors.Count > 0 then
                    Error (String.concat Environment.NewLine errors)
                else
                    Ok
                        { Fields = fields
                          Body = body }

    let parseFile (path: string) =
        File.ReadAllText path |> parseText

    let tryGetOne key (doc: FrontMatterDocument) =
        doc.Fields |> Map.tryFind key |> Option.bind List.tryHead

    let getList key (doc: FrontMatterDocument) =
        doc.Fields |> Map.tryFind key |> Option.defaultValue []

    let getInt key defaultValue (doc: FrontMatterDocument) =
        match tryGetOne key doc with
        | Some value ->
            match Int32.TryParse value with
            | true, parsed -> parsed
            | _ -> defaultValue
        | None -> defaultValue

    let getBool key defaultValue (doc: FrontMatterDocument) =
        match tryGetOne key doc with
        | Some value ->
            match value.Trim().ToLowerInvariant() with
            | "true"
            | "yes"
            | "1" -> true
            | "false"
            | "no"
            | "0" -> false
            | _ -> defaultValue
        | None -> defaultValue
