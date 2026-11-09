namespace Rocq.Navigator

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Encodings.Web
open System.Text.Json
open System.Text.RegularExpressions
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Structure

/// <summary>The output of <c>Print Assumptions</c>.</summary>
type Assumptions =
    /// <summary>The symbol is closed under the global context.</summary>
    | Closed
    /// <summary>The axioms the symbol depends on. An admitted theorem depends on itself.</summary>
    | RestsOn of string[]

/// <summary>What Rocq reported about a symbol. An absent field means Rocq gave no answer.</summary>
type SymbolFact =
    {
        /// The type as Rocq prints it.
        Type: string option
        /// The leading quantifier's binders: name to type, in printed form.
        Binders: Map<string, string>
        /// Present for theorems only.
        Assumptions: Assumptions option
    }

/// <summary>The contents of <c>facts.json</c>.</summary>
type Facts =
    {
        RocqVersion: string
        /// Source digest at capture, by project-relative file path.
        Digests: Map<string, string>
        /// Facts by full Rocq name: see <c>Facts.symbolKey</c>.
        Symbols: Map<string, SymbolFact>
    }

/// <summary>Captures, stores and reconciles the facts only a running Rocq can report.</summary>
module Facts =
    type private Command =
        | CheckType of string
        | PrintAssumptions of string

    /// <summary>The full Rocq name of a symbol, which keys <c>Facts.Symbols</c>.</summary>
    let symbolKey (project: RocqProject) (symbol: ProjectSymbol) =
        project.Files[symbol.File].Module
        + "."
        + symbol.Symbol.Qualified

    /// Kinds without a term to check.
    let private uncheckable =
        set [ "not"; "sec"; "mod"; "modtype"; "lib"; "binder"; "ltac"; "tac" ]

    let private isTheorem (project: RocqProject) (symbol: ProjectSymbol) =
        match symbol.Declaration with
        | Some index -> project.Declarations[index].Declaration.Category = Category.Theorem
        | None -> false

    let private commands (project: RocqProject) =
        let seen = HashSet<string>()

        [|
            for symbol in project.Symbols do
                if
                    not (uncheckable.Contains symbol.Symbol.Kind)
                    && seen.Add (symbolKey project symbol)
                then
                    CheckType (symbolKey project symbol)

                    if isTheorem project symbol then
                        PrintAssumptions (symbolKey project symbol)
        |]

    /// <summary>The script for <c>rocq repl</c>: a <c>Fail Check __nav_N.</c> separator precedes command N.</summary>
    let captureScript (project: RocqProject) (config: Config) : string =
        let builder = StringBuilder ()

        let line (text: string) =
            builder.Append(text).Append('\n') |> ignore

        for file in project.Files do
            line $"Require {file.Module}."

        for import in config.CaptureImports do
            line $"Require Import {import}."

        commands project
        |> Array.iteri (fun index command ->
            line $"Fail Check __nav_{index}."

            match command with
            | CheckType name -> line $"Check @{name}."
            | PrintAssumptions name -> line $"Print Assumptions {name}.")

        builder.ToString ()

    let private separator =
        Regex @"The command has indeed failed with message:\s+The reference __nav_(\d+)\s+was\s+not\s+found\s+in\s+the\s+current\s+environment\."

    let private noise = Regex @"(?m)^(Loading ML|Fetching opaque proofs).*\r?\n?"

    /// The output of each command, by command number.
    let private segments (transcript: string) =
        let matches = separator.Matches transcript

        [
            for i in 0 .. matches.Count - 1 do
                let start = matches[i].Index + matches[i].Length

                let stop =
                    if i + 1 < matches.Count then
                        matches[i + 1].Index
                    else
                        transcript.Length

                let text =
                    noise.Replace(transcript.Substring (start, stop - start), "").Replace("\r\n", "\n")

                int matches[i].Groups[1].Value, text
        ]

    /// <summary>The binders of the <c>forall</c> or <c>∀</c> opening <c>checkText</c>: name to type, empty when it has none.</summary>
    let parseBinders (checkText: string) : Map<string, string> =
        let text = checkText.TrimStart ()

        let header =
            if text.StartsWith ("forall ", StringComparison.Ordinal) then
                Some (text.Substring 7)
            elif text.StartsWith ("∀ ", StringComparison.Ordinal) then
                Some (text.Substring 2)
            else
                None

        let isOpen c =
            c = '(' || c = '{' || c = '['

        let isClose c =
            c = ')' || c = '}' || c = ']'

        let groupBinders (group: string) =
            // A group reads `names : type`. `(x := v : T)` is a let-binder, with no quantified name.
            let colon = group.IndexOf ':'

            if colon < 0 || group.Contains ":=" then
                []
            else
                let kind = group.Substring(colon + 1).Trim()

                [
                    for name in group.Substring(0, colon).Split(' ', StringSplitOptions.RemoveEmptyEntries) do
                        if name <> "_" then
                            name, kind
                ]

        match header with
        | None -> Map.empty
        | Some header ->
            let groups = ResizeArray<string>()
            let mutable depth = 0
            let mutable groupStart = 0
            let mutable stop = -1
            let mutable i = 0

            while stop < 0 && i < header.Length do
                let c = header[i]

                if isOpen c then
                    if depth = 0 then
                        groupStart <- i + 1

                    depth <- depth + 1
                elif isClose c then
                    depth <- depth - 1

                    if depth = 0 then
                        groups.Add (header.Substring (groupStart, i - groupStart))
                        groupStart <- -1
                elif c = ',' && depth = 0 then
                    stop <- i

                i <- i + 1

            if stop < 0 then
                Map.empty
            else
                // An unbracketed `a b : T` opens with text at depth 0, and `T` may hold brackets of its own.
                let unbracketed = not (isOpen (header.TrimStart().[0]))

                let all =
                    if unbracketed then
                        [ header.Substring (0, stop) ]
                    else
                        List.ofSeq groups

                all |> List.collect groupBinders |> Map.ofList

    let private parseType (output: string) =
        let m = Regex.Match (output, @"(?m)^\s+: ")

        if m.Success then
            Some (output.Substring(m.Index + m.Length).Trim().Replace("\n       ", "\n"))
        else
            None

    let private parseAssumptions (output: string) =
        if output.Contains "Closed under the global context" then
            Some Closed
        else
            let header = output.IndexOf "Axioms:"

            if header < 0 then
                None
            else
                output.Substring(header + 7).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                |> Array.filter (fun line -> not (Char.IsWhiteSpace line[0]))
                |> Array.map (fun line -> (line.Split ' ').[0])
                |> RestsOn
                |> Some

    /// <summary>Reads the output of <c>captureScript</c> for the same project. Every captured symbol has an entry.</summary>
    let parseTranscript (project: RocqProject) (transcript: string) : Map<string, SymbolFact> =
        let all = commands project

        let empty =
            {
                Type = None
                Binders = Map.empty
                Assumptions = None
            }

        let start =
            all
            |> Array.fold
                (fun facts command ->
                    match command with
                    | CheckType name
                    | PrintAssumptions name -> Map.add name empty facts)
                Map.empty

        segments transcript
        |> List.fold
            (fun facts (index, output) ->
                if index >= all.Length then
                    facts
                else
                    match all[index] with
                    | CheckType name ->
                        let kind = parseType output

                        facts
                        |> Map.add
                            name
                            { facts[name] with
                                Type = kind
                                Binders =
                                    kind
                                    |> Option.map parseBinders
                                    |> Option.defaultValue Map.empty
                            }
                    | PrintAssumptions name ->
                        facts
                        |> Map.add
                            name
                            { facts[name] with
                                Assumptions = parseAssumptions output
                            })
            start

    let private run (rocq: string[]) (workingDirectory: string) (arguments: string list) (input: string) =
        let info = ProcessStartInfo (rocq[0])

        for argument in Seq.append (Seq.skip 1 rocq) arguments do
            info.ArgumentList.Add argument

        info.WorkingDirectory <- workingDirectory
        info.RedirectStandardInput <- true
        info.RedirectStandardOutput <- true
        info.RedirectStandardError <- true
        info.StandardInputEncoding <- UTF8Encoding false
        info.StandardOutputEncoding <- Encoding.UTF8
        info.StandardErrorEncoding <- Encoding.UTF8
        use rocqProcess = Process.Start info
        let output = rocqProcess.StandardOutput.ReadToEndAsync ()
        let errors = rocqProcess.StandardError.ReadToEndAsync ()
        rocqProcess.StandardInput.Write input
        rocqProcess.StandardInput.Close ()
        rocqProcess.WaitForExit ()
        rocqProcess.ExitCode, output.Result, errors.Result

    /// The `-Q` and `-R` mappings of the `_CoqProject` in a directory, as command line words.
    let private loadPath (projectDir: string) =
        let words =
            File.ReadAllText(Path.Combine (projectDir, "_CoqProject")).Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)

        [
            for i in 0 .. words.Length - 3 do
                if words[i] = "-Q" || words[i] = "-R" then
                    yield! [ words[i]; words[i + 1]; words[i + 2] ]
        ]

    /// <summary>Runs Rocq over the compiled project and collects its facts.</summary>
    /// <param name="rocq">The command that starts <c>rocq</c>, followed by any arguments it needs first.</param>
    /// <param name="projectDir">The directory holding <c>_CoqProject</c> and the <c>.vo</c> files.</param>
    /// <param name="project">The project loaded from that directory.</param>
    /// <param name="config">Supplies the modules imported before capture.</param>
    /// <returns><c>Error</c> when Rocq fails to start or exits abnormally, or when a module fails to load.</returns>
    let capture (rocq: string[]) (projectDir: string) (project: RocqProject) (config: Config) : Result<Facts, string> =
        try
            match run rocq projectDir [ "--print-version" ] "" with
            | code, _, errors when code <> 0 -> Result.Error $"rocq --print-version exited with {code}: {errors}"
            | _, version, _ ->
                let script = captureScript project config

                match run rocq projectDir ("repl" :: loadPath projectDir) script with
                | code, _, errors when code <> 0 -> Result.Error $"rocq repl exited with {code}: {errors}"
                | _, _, errors when Regex.IsMatch (errors, @"(?m)^> Require ") ->
                    Result.Error $"rocq repl failed to load a module. Compile the project first.\n{errors}"
                | _, output, _ ->
                    Ok
                        {
                            RocqVersion = (version.Split ' ').[0].Trim()
                            Digests =
                                project.Files
                                |> Array.map (fun file -> file.Path, file.Digest)
                                |> Map.ofArray
                            Symbols = parseTranscript project output
                        }
        with :? ComponentModel.Win32Exception as failure ->
            Result.Error $"{rocq[0]}: {failure.Message}"

    /// <summary>Writes <c>facts.json</c>, with keys in sorted order.</summary>
    let write (path: string) (facts: Facts) : unit =
        let options =
            JsonWriterOptions (Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

        use stream = File.Create path
        use writer = new Utf8JsonWriter (stream, options)

        let writeStrings (name: string) (values: Map<string, string>) =
            writer.WriteStartObject name

            for KeyValue (key, value) in values do
                writer.WriteString (key, value)

            writer.WriteEndObject ()

        writer.WriteStartObject ()
        writer.WriteString ("rocqVersion", facts.RocqVersion)
        writeStrings "digests" facts.Digests
        writer.WriteStartObject "symbols"

        for KeyValue (name, fact) in facts.Symbols do
            writer.WriteStartObject name

            fact.Type
            |> Option.iter (fun text -> writer.WriteString ("type", text))

            writeStrings "binders" fact.Binders

            match fact.Assumptions with
            | None -> ()
            | Some Closed -> writer.WriteString ("assumptions", "closed")
            | Some (RestsOn axioms) ->
                writer.WriteStartArray "assumptions"

                for axiom in axioms do
                    writer.WriteStringValue axiom

                writer.WriteEndArray ()

            writer.WriteEndObject ()

        writer.WriteEndObject ()
        writer.WriteEndObject ()

    /// <summary>Reads <c>facts.json</c>. Returns <c>Error</c> naming the path when it is missing or malformed.</summary>
    let read (path: string) : Result<Facts, string> =
        let strings (element: JsonElement) =
            element.EnumerateObject ()
            |> Seq.map (fun property -> property.Name, property.Value.GetString ())
            |> Map.ofSeq

        let fact (element: JsonElement) =
            {
                Type =
                    match element.TryGetProperty "type" with
                    | true, value when value.ValueKind <> JsonValueKind.Null -> Some (value.GetString ())
                    | _ -> None
                Binders = strings (element.GetProperty "binders")
                Assumptions =
                    match element.TryGetProperty "assumptions" with
                    | true, value when value.ValueKind = JsonValueKind.Null -> None
                    | true, value when
                        value.ValueKind = JsonValueKind.Array
                        && value.GetArrayLength () > 0
                        ->
                        value.EnumerateArray ()
                        |> Seq.map (fun axiom -> axiom.GetString ())
                        |> Array.ofSeq
                        |> RestsOn
                        |> Some
                    | true, _ -> Some Closed
                    | _ -> None
            }

        try
            use document = JsonDocument.Parse (File.ReadAllBytes path)
            let root = document.RootElement

            Ok
                {
                    RocqVersion = root.GetProperty("rocqVersion").GetString()
                    Digests = strings (root.GetProperty "digests")
                    Symbols =
                        root.GetProperty("symbols").EnumerateObject()
                        |> Seq.map (fun property -> property.Name, fact property.Value)
                        |> Map.ofSeq
                }
        with
        | :? IOException
        | :? UnauthorizedAccessException
        | :? JsonException
        | :? KeyNotFoundException
        | :? InvalidOperationException as failure -> Result.Error $"{path}: {failure.Message}"

    /// <summary>
    /// Matches facts to the project. Facts for symbols outside the project are dropped.
    /// A file whose digest differs from the recorded one is an error, or with <c>dropStale</c> loses its facts.
    /// </summary>
    let reconcile (dropStale: bool) (project: RocqProject) (facts: Facts) : Result<Facts, string[]> =
        let stale =
            project.Files
            |> Array.filter (fun file ->
                facts.Digests.TryFind file.Path
                <> Some file.Digest)

        if stale.Length > 0 && not dropStale then
            stale
            |> Array.map (fun file ->
                let recorded =
                    facts.Digests.TryFind file.Path
                    |> Option.defaultValue "none"

                $"{file.Path}: facts record digest {recorded}, and the source has {file.Digest}. Capture the facts again.")
            |> Result.Error
        else
            let stalePaths =
                stale
                |> Array.map (fun file -> file.Path)
                |> Set.ofArray

            let current =
                project.Symbols
                |> Array.filter (fun symbol -> not (stalePaths.Contains project.Files[symbol.File].Path))
                |> Array.map (symbolKey project)
                |> Set.ofArray

            Ok
                { facts with
                    Digests =
                        project.Files
                        |> Array.filter (fun file -> not (stalePaths.Contains file.Path))
                        |> Array.map (fun file -> file.Path, file.Digest)
                        |> Map.ofArray
                    Symbols =
                        facts.Symbols
                        |> Map.filter (fun name _ -> current.Contains name)
                }
