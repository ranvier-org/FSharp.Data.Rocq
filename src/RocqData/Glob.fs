/// Reader for Rocq 9.1.0 `.glob` files. Format reference: `interp/dumpglob.ml` and its `.mli`.
module RocqData.Glob

open System
open System.IO
open System.Text
open RocqData.Source

/// A definition line: `<kind> <start>:<end> <path> <name>`.
type GlobDef =
    {
        Line: int
        Span: Span
        Path: string
        Name: string
        Kind: string
    }

/// A reference line: `R<start>:<end> <module> <path> <name> <kind>`.
type GlobRef =
    {
        Line: int
        Span: Span
        Module: string
        Path: string
        Name: string
        Kind: string
    }

type GlobFile =
    {
        /// The hexadecimal digest from the first line; `None` for `DIGEST NO`.
        Digest: string option
        Module: string
        Defs: GlobDef[]
        Refs: GlobRef[]
        /// The total number of lines, header included.
        LineCount: int
        /// Well-formed lines with a kind outside `knownKinds`: line number, kind.
        UnknownKinds: (int * string)[]
        /// Lines matching no known shape: line number, text.
        Unparsed: (int * string)[]
    }

/// The kinds emitted by Rocq 9.1.0, read from every `Dumpglob` caller and `type_of_global_ref`. The `.mli` list is
/// incomplete: it omits `prf`, `sec`, `symb`, `abbrev` and the `var`-prefixed kinds.
let knownKinds =
    set
        [
            "ax"
            "def"
            "coe"
            "subclass"
            "canonstruc"
            "ex"
            "scheme"
            "proj"
            "inst"
            "meth"
            "defax"
            "prfax"
            "thm"
            "prf"
            "prim"
            "symb"
            "class"
            "var"
            "indrec"
            "rec"
            "corec"
            "ind"
            "variant"
            "coind"
            "constr"
            "not"
            "binder"
            "lib"
            "mod"
            "modtype"
            "sec"
            "abbrev"
        ]

let private isKnownKind (kind: string) =
    knownKinds.Contains kind
    || kind.StartsWith "var"
       && knownKinds.Contains (kind.Substring 3)

let private parseRange (s: string) =
    match s.IndexOf ':' with
    | -1 -> ValueNone
    | c ->
        match Int32.TryParse (s.AsSpan (0, c)), Int32.TryParse (s.AsSpan (c + 1)) with
        | (true, a), (true, b) ->
            // Rocq writes an inclusive end offset (`interval` subtracts one). A span here is half-open.
            ValueSome (span a (b + 1))
        | _ -> ValueNone

let private isKindWord (s: string) =
    s.Length > 0
    && Seq.forall Char.IsAsciiLetterLower s

/// Parses `.glob` bytes. Every line ends up in exactly one of `Defs`, `Refs`, `Unparsed` or the header.
let parse (bytes: byte[]) : GlobFile =
    let lines = Encoding.UTF8.GetString(bytes).Split '\n'
    // A trailing newline yields one empty final element, which is no line.
    let count =
        if lines[lines.Length - 1] = "" then
            lines.Length - 1
        else
            lines.Length

    let defs = ResizeArray ()
    let refs = ResizeArray ()
    let unknown = ResizeArray ()
    let unparsed = ResizeArray ()
    let mutable digest = None
    let mutable moduleName = ""

    let note lineNo kind =
        if not (isKnownKind kind) then
            unknown.Add ((lineNo, kind))

    for lineNo in 0 .. count - 1 do
        let line = lines[lineNo].TrimEnd '\r'
        // `Context` binders write `var  <range> ...` with two spaces, so empty fields are dropped.
        let parts = line.Split (' ', StringSplitOptions.RemoveEmptyEntries)

        if
            lineNo = 0
            && parts.Length = 2
            && parts[0] = "DIGEST"
        then
            digest <- if parts[1] = "NO" then None else Some parts[1]
        elif
            line.StartsWith 'F'
            && parts.Length = 1
            && moduleName = ""
        then
            moduleName <- line.Substring 1
        else
            match parts with
            | [| head; m; p; name; kind |] when head.StartsWith 'R' ->
                match parseRange (head.Substring 1) with
                | ValueSome s when isKindWord kind ->
                    note lineNo kind

                    refs.Add
                        {
                            Line = lineNo
                            Span = s
                            Module = m
                            Path = p
                            Name = name
                            Kind = kind
                        }
                | _ -> unparsed.Add ((lineNo, line))
            | [| kind; range; p; name |] when isKindWord kind ->
                match parseRange range with
                | ValueSome s ->
                    note lineNo kind

                    defs.Add
                        {
                            Line = lineNo
                            Span = s
                            Path = p
                            Name = name
                            Kind = kind
                        }
                | ValueNone -> unparsed.Add ((lineNo, line))
            | _ -> unparsed.Add ((lineNo, line))

    {
        Digest = digest
        Module = moduleName
        Defs = defs.ToArray ()
        Refs = refs.ToArray ()
        LineCount = count
        UnknownKinds = unknown.ToArray ()
        Unparsed = unparsed.ToArray ()
    }

let parseFile (path: string) =
    parse (File.ReadAllBytes path)
