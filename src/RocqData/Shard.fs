/// Writes the three per-file layers. `docs/SCHEMA.md` is the reference for every field.
module RocqData.Shard

open System
open System.Buffers
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Encodings.Web
open System.Text.Json
open RocqData.Source
open RocqData.Lexer
open RocqData.Glob
open RocqData.Structure

/// Incremented on a change that breaks readers.
[<Literal>]
let SchemaMajor = 1

/// Incremented on an addition that existing readers can ignore.
[<Literal>]
let SchemaMinor = 0

/// Part of the incremental key: a new generator re-extracts every file.
[<Literal>]
let GeneratorVersion = "0.1.0"

/// Facts about a file that sit outside its source and `.glob`.
type FileHeader =
    {
        Path: string
        Module: string
        Digest: string
        GlobDigest: string
        /// `ok`, `stale` or `noDigest`. Only `ok` carries `.glob` facts.
        GlobStatus: string
    }

let private writerOptions =
    JsonWriterOptions (Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = true)

let writeSchema (w: Utf8JsonWriter) (layer: string) =
    w.WriteStartObject "schema"
    w.WriteNumber ("major", SchemaMajor)
    w.WriteNumber ("minor", SchemaMinor)
    w.WriteEndObject ()
    w.WriteString ("layer", layer)

let writeInts (w: Utf8JsonWriter) (name: string) (values: seq<int>) =
    w.WriteStartArray name

    for v in values do
        w.WriteNumberValue v

    w.WriteEndArray ()

let writeStrings (w: Utf8JsonWriter) (name: string) (values: seq<string>) =
    w.WriteStartArray name

    for v in values do
        if isNull v then
            w.WriteNullValue ()
        else
            w.WriteStringValue v

    w.WriteEndArray ()

let private writeSpan (w: Utf8JsonWriter) (name: string) (s: Span) =
    w.WriteStartArray name
    w.WriteNumberValue s.Start
    w.WriteNumberValue s.End
    w.WriteEndArray ()

let private writeSpanOpt (w: Utf8JsonWriter) (name: string) (s: Span voption) =
    match s with
    | ValueSome v -> writeSpan w name v
    | ValueNone -> w.WriteNull name

let private writeStringOrNull (w: Utf8JsonWriter) (name: string) (s: string) =
    if isNull s then
        w.WriteNull name
    else
        w.WriteString (name, s)

/// Writes a JSON document atomically: a reader sees the old file or the new one.
let writeJsonFile (path: string) (body: Utf8JsonWriter -> unit) =
    let buffer = ArrayBufferWriter<byte>(64 * 1024)

    do
        use w = new Utf8JsonWriter (buffer, writerOptions)
        w.WriteStartObject ()
        body w
        w.WriteEndObject ()

    let temp = path + ".tmp"

    Directory.CreateDirectory (Path.GetDirectoryName path)
    |> ignore

    File.WriteAllBytes (temp, buffer.WrittenSpan.ToArray ())
    File.Move (temp, path, true)

let private writeFileHeader (w: Utf8JsonWriter) (h: FileHeader) =
    w.WriteString ("module", h.Module)
    w.WriteString ("path", h.Path)
    w.WriteString ("digest", h.Digest)
    writeStringOrNull w "globDigest" h.GlobDigest
    w.WriteString ("globStatus", h.GlobStatus)

let private intern (table: List<string>) (index: Dictionary<string, int>) (value: string) =
    if isNull value then
        -1
    else
        match index.TryGetValue value with
        | true, i -> i
        | _ ->
            let i = table.Count
            table.Add value
            index[value] <- i
            i

let private writeDeclaration (w: Utf8JsonWriter) (d: Declaration) =
    w.WriteStartObject ()
    w.WriteString ("id", d.Id)
    writeStringOrNull w "name" d.Name
    w.WriteString ("nameSource", d.NameSource)
    writeSpanOpt w "nameSpan" d.NameSpan
    w.WriteString ("keyword", d.Keyword)
    writeSpan w "keywordSpan" d.KeywordSpan
    writeStrings w "modifiers" d.Modifiers
    w.WriteString ("category", d.Category)
    w.WriteNumber ("scope", d.Scope)
    w.WriteNumber ("sentence", d.Sentence)
    writeSpan w "sentenceSpan" d.SentenceSpan
    writeSpan w "extent" d.Extent
    writeSpanOpt w "statement" d.Statement
    writeSpanOpt w "binders" d.Binders
    writeSpanOpt w "type" d.Type
    writeSpanOpt w "body" d.Body
    w.WriteStartArray "proofs"

    for p in d.Proofs do
        w.WriteStartObject ()
        w.WriteString ("ending", p.Ending)
        writeSpan w "span" p.Span
        writeSpan w "bodySpan" p.BodySpan
        w.WriteNumber ("opener", p.Opener)
        w.WriteNumber ("firstSentence", p.FirstSentence)
        w.WriteNumber ("lastSentence", p.LastSentence)
        w.WriteNumber ("tacticSentences", p.TacticSentences)
        w.WriteEndObject ()

    w.WriteEndArray ()
    writeInts w "symbols" d.Symbols
    writeInts w "docs" d.Docs
    w.WriteEndObject ()

/// Writes the outline layer: everything except the reference table and the token stream.
let writeOutline (path: string) (h: FileHeader) (m: FileModel) (glob: GlobFile option) =
    writeJsonFile path (fun w ->
        writeSchema w "outline"
        writeFileHeader w h
        w.WriteNumber ("bytes", m.Bytes.Length)
        writeInts w "lineStarts" (lineStarts m.Bytes)

        // Sentences, with low-cardinality columns as indexes into a table.
        let categories = List<string>()
        let categoryIndex = Dictionary<string, int>()
        let keywords = List<string>()
        let keywordIndex = Dictionary<string, int>()

        let categoryColumn =
            m.Sentences
            |> Array.map (fun s -> intern categories categoryIndex s.Category)

        let keywordColumn =
            m.Sentences
            |> Array.map (fun s -> intern keywords keywordIndex s.Keyword)

        w.WriteStartObject "sentences"
        writeStrings w "categories" categories
        writeStrings w "keywords" keywords
        writeInts w "start" (m.Sentences |> Seq.map _.Span.Start)
        writeInts w "end" (m.Sentences |> Seq.map _.Span.End)
        writeInts w "category" categoryColumn
        writeInts w "keyword" keywordColumn
        writeInts w "decl" (m.Sentences |> Seq.map _.Decl)
        writeInts w "firstToken" (m.Sentences |> Seq.map _.FirstToken)
        writeInts w "endToken" (m.Sentences |> Seq.map _.EndToken)
        w.WriteStartArray "modifiers"

        for s in m.Sentences do
            if s.Modifiers.Length > 0 then
                w.WriteStartObject ()
                w.WriteNumber ("sentence", s.Index)
                writeStrings w "values" s.Modifiers
                w.WriteEndObject ()

        w.WriteEndArray ()
        w.WriteEndObject ()

        w.WriteStartArray "comments"

        for c in m.Comments do
            w.WriteStartObject ()
            w.WriteString ("format", c.Format)
            writeSpan w "span" c.Span
            writeSpan w "bodySpan" c.BodySpan
            w.WriteString ("text", text m.Bytes c.Span)
            w.WriteNumber ("sentence", c.Sentence)
            w.WriteNumber ("nextSentence", c.NextSentence)
            w.WriteNumber ("newlinesToNext", c.NewlinesToNext)
            w.WriteNumber ("trailingSentence", c.TrailingSentence)
            w.WriteNumber ("decl", c.Decl)
            w.WriteNumber ("attachedTo", c.AttachedTo)
            w.WriteEndObject ()

        w.WriteEndArray ()

        w.WriteStartArray "headings"

        for x in m.Headings do
            w.WriteStartObject ()
            w.WriteNumber ("level", x.Level)
            w.WriteString ("text", x.Text)
            writeSpan w "span" x.Span
            w.WriteString ("format", x.Format)
            w.WriteNumber ("comment", x.Comment)
            w.WriteEndObject ()

        w.WriteEndArray ()

        w.WriteStartArray "scopes"

        for s in m.Scopes do
            w.WriteStartObject ()
            w.WriteString ("kind", s.Kind)
            writeStringOrNull w "name" s.Name
            w.WriteNumber ("parent", s.Parent)
            w.WriteNumber ("sentence", s.Sentence)
            w.WriteNumber ("endSentence", s.EndSentence)
            writeSpan w "span" s.Span
            writeInts w "docs" s.Docs
            w.WriteEndObject ()

        w.WriteEndArray ()

        w.WriteStartArray "declarations"

        for d in m.Declarations do
            writeDeclaration w d

        w.WriteEndArray ()

        w.WriteStartArray "imports"

        for i in m.Imports do
            w.WriteStartObject ()
            w.WriteNumber ("sentence", i.Sentence)
            w.WriteString ("keyword", i.Keyword)
            writeStringOrNull w "from" i.From
            writeStringOrNull w "mode" i.Mode
            w.WriteStartArray "modules"

            for x in i.Modules do
                w.WriteStartObject ()
                w.WriteString ("text", x.Text)
                writeSpan w "span" x.Span
                writeStringOrNull w "resolved" x.Resolved
                w.WriteEndObject ()

            w.WriteEndArray ()
            w.WriteEndObject ()

        w.WriteEndArray ()

        w.WriteStartArray "notations"

        for n in m.Notations do
            w.WriteStartObject ()
            w.WriteNumber ("sentence", n.Sentence)
            w.WriteNumber ("decl", n.Decl)
            w.WriteString ("keyword", n.Keyword)
            writeStringOrNull w "literal" n.Literal
            writeSpanOpt w "literalSpan" n.LiteralSpan
            w.WriteNumber ("symbol", n.Symbol)
            writeStringOrNull w "entry" n.Entry
            writeStringOrNull w "scope" n.Scope
            writeStringOrNull w "key" n.Key
            w.WriteEndObject ()

        w.WriteEndArray ()

        w.WriteStartObject "symbols"
        writeStrings w "name" (m.Symbols |> Seq.map _.Name)
        writeStrings w "path" (m.Symbols |> Seq.map _.Path)
        writeStrings w "qualified" (m.Symbols |> Seq.map _.Qualified)
        writeStrings w "kind" (m.Symbols |> Seq.map _.Kind)
        writeInts w "start" (m.Symbols |> Seq.map _.Span.Start)
        writeInts w "end" (m.Symbols |> Seq.map _.Span.End)
        writeInts w "line" (m.Symbols |> Seq.map _.Line)
        writeInts w "sentence" (m.Symbols |> Seq.map _.Sentence)
        writeInts w "decl" (m.Symbols |> Seq.map _.Decl)
        w.WriteEndObject ()

        // Uses, with a target table of their own so `xref.json` builds from this layer alone.
        let useTargets = List<Target>()
        let remap = Dictionary<int, int>()

        let targetColumn =
            m.Uses
            |> Array.map (fun u ->
                match remap.TryGetValue u.Target with
                | true, i -> i
                | _ ->
                    let i = useTargets.Count

                    // A target naming a symbol under definition is written as that symbol, so it resolves globally.
                    let t = m.Targets[u.Target]

                    useTargets.Add (
                        if t.Symbol >= 0 then
                            { t with
                                Path = m.Symbols[t.Symbol].Path
                                Name = m.Symbols[t.Symbol].Name
                            }
                        else
                            t
                    )

                    remap[u.Target] <- i
                    i)

        w.WriteStartObject "uses"
        writeInts w "owner" (m.Uses |> Seq.map _.Owner)
        writeInts w "target" targetColumn
        writeInts w "where" (m.Uses |> Seq.map _.Where)
        writeInts w "count" (m.Uses |> Seq.map _.Count)
        w.WriteStartObject "targets"
        writeStrings w "module" (useTargets |> Seq.map _.Module)
        writeStrings w "path" (useTargets |> Seq.map _.Path)
        writeStrings w "name" (useTargets |> Seq.map _.Name)
        writeStrings w "kind" (useTargets |> Seq.map _.Kind)
        w.WriteEndObject ()
        w.WriteEndObject ()

        // Required libraries, and the count of references into each module.
        let requires =
            m.Refs
            |> Seq.map (fun r -> m.Targets[r.Target])
            |> Seq.filter (fun t -> t.Kind = "lib")
            |> Seq.map _.Module
            |> Seq.distinct
            |> Seq.sort
            |> Seq.toArray

        writeStrings w "requires" requires
        let counts = SortedDictionary<string, int>(StringComparer.Ordinal)

        for r in m.Refs do
            let t = m.Targets[r.Target]

            if t.Kind <> "lib" then
                counts[t.Module] <-
                    (match counts.TryGetValue t.Module with
                     | true, c -> c + 1
                     | _ -> 1)

        w.WriteStartObject "referencedModules"
        writeStrings w "module" counts.Keys
        writeInts w "count" counts.Values
        w.WriteEndObject ()

        w.WriteStartObject "globLines"
        w.WriteNumber ("total", m.GlobLineCount)
        w.WriteNumber ("refs", m.Refs.Length)
        w.WriteNumber ("symbols", m.Symbols.Length)
        w.WriteNumber ("binders", m.Binders.Length)
        w.WriteStartArray "unknownKinds"

        match glob with
        | Some g ->
            for (line, kind) in g.UnknownKinds do
                w.WriteStartObject ()
                w.WriteNumber ("line", line)
                w.WriteString ("kind", kind)
                w.WriteEndObject ()
        | None -> ()

        w.WriteEndArray ()
        w.WriteStartArray "unparsed"

        match glob with
        | Some g ->
            for (line, content) in g.Unparsed do
                w.WriteStartObject ()
                w.WriteNumber ("line", line)
                w.WriteString ("text", content)
                w.WriteEndObject ()
        | None -> ()

        w.WriteEndArray ()
        w.WriteEndObject ()

        w.WriteStartArray "diagnostics"

        for d in m.Diagnostics do
            w.WriteStartObject ()
            w.WriteString ("severity", d.Severity)
            w.WriteString ("code", d.Code)
            w.WriteString ("message", d.Message)
            w.WriteNumber ("offset", d.Offset)
            w.WriteEndObject ()

        w.WriteEndArray ())

/// Writes the refs layer: every reference and binder from the `.glob`.
let writeRefs (path: string) (h: FileHeader) (m: FileModel) =
    writeJsonFile path (fun w ->
        writeSchema w "refs"
        writeFileHeader w h
        w.WriteStartObject "targets"
        writeStrings w "module" (m.Targets |> Seq.map _.Module)
        writeStrings w "path" (m.Targets |> Seq.map _.Path)
        writeStrings w "name" (m.Targets |> Seq.map _.Name)
        writeStrings w "kind" (m.Targets |> Seq.map _.Kind)
        writeInts w "binder" (m.Targets |> Seq.map _.Binder)
        writeInts w "symbol" (m.Targets |> Seq.map _.Symbol)
        w.WriteEndObject ()
        w.WriteStartObject "refs"
        writeInts w "start" (m.Refs |> Seq.map _.Span.Start)
        writeInts w "end" (m.Refs |> Seq.map _.Span.End)
        writeInts w "target" (m.Refs |> Seq.map _.Target)
        writeInts w "line" (m.Refs |> Seq.map _.Line)
        w.WriteEndObject ()
        w.WriteStartObject "binders"
        writeInts w "start" (m.Binders |> Seq.map _.Span.Start)
        writeInts w "end" (m.Binders |> Seq.map _.Span.End)
        writeStrings w "name" (m.Binders |> Seq.map _.Name)
        writeStrings w "uid" (m.Binders |> Seq.map _.Uid)
        writeInts w "line" (m.Binders |> Seq.map _.Line)
        w.WriteEndObject ())

/// Writes the tokens layer: the source text, a token partition of it, and links from tokens to `.glob` entries.
let writeTokens (path: string) (h: FileHeader) (m: FileModel) =
    let tokens = m.Tokens
    let starts = tokens |> Array.map _.Start

    // UTF-16 offset of each token, for slicing `source` in JavaScript or .NET.
    let u16 = Array.zeroCreate<int> tokens.Length
    let mutable units = 0

    for i in 0 .. tokens.Length - 1 do
        u16[i] <- units
        let t = tokens[i]

        units <-
            units
            + Encoding.UTF8.GetCharCount (m.Bytes, t.Start, t.End - t.Start)

    let linkToken = List<int>()
    let linkType = List<int>()
    let linkIndex = List<int>()
    let links = List<struct (int * int * int)>()

    let addLinks (linkKind: int) (index: int) (s: Span) =
        let mutable t = Array.BinarySearch (starts, s.Start)

        if t < 0 then
            t <- ~~~t - 1

        while t < tokens.Length && tokens[t].Start < s.End do
            links.Add (struct (t, linkKind, index))
            t <- t + 1

    m.Refs
    |> Array.iteri (fun i r -> addLinks 0 i r.Span)

    m.Symbols
    |> Array.iteri (fun i s -> addLinks 1 i s.Span)

    m.Binders
    |> Array.iteri (fun i b -> addLinks 2 i b.Span)

    links.Sort ()

    for struct (t, kind, index) in links do
        linkToken.Add t
        linkType.Add kind
        linkIndex.Add index

    writeJsonFile path (fun w ->
        writeSchema w "tokens"
        writeFileHeader w h
        w.WriteString ("source", Encoding.UTF8.GetString m.Bytes)
        w.WriteNumber ("bytes", m.Bytes.Length)
        w.WriteNumber ("utf16Length", units)
        writeStrings w "kinds" (Enum.GetNames<TokenKind>())
        writeInts w "kind" (tokens |> Seq.map (fun t -> int t.Kind))
        writeInts w "start" starts
        writeInts w "u16" u16
        w.WriteStartObject "links"
        writeInts w "token" linkToken
        writeInts w "type" linkType
        writeInts w "index" linkIndex
        w.WriteEndObject ())
