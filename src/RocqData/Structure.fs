/// Groups tokens into sentences and comments, and derives declarations, proofs, scopes and notations.
module RocqData.Structure

open System
open System.Collections.Generic
open System.Text
open RocqData.Source
open RocqData.Lexer
open RocqData.Glob

type Sentence =
    {
        Index: int
        Span: Span
        /// Index of the sentence's first token.
        FirstToken: int
        /// Index after the sentence's last token.
        EndToken: int
        mutable Category: string
        Keyword: string
        KeywordSpan: Span voption
        Modifiers: string[]
        mutable Decl: int
        /// True when `Span` lacks a terminator, which only happens at the end of a file that does not compile.
        Terminated: bool
    }

type Heading =
    {
        Level: int
        Text: string
        Span: Span
        Format: string
        Comment: int
    }

type Comment =
    {
        Index: int
        Span: Span
        /// `doc` for `(** `, `md` for `(**md`, `plain` otherwise.
        Format: string
        BodySpan: Span
        /// The sentence containing the comment, or -1 at top level.
        Sentence: int
        /// The next sentence starting after the comment, or -1.
        NextSentence: int
        /// Newlines between the comment and `NextSentence`, counting comments between them.
        NewlinesToNext: int
        /// The previous sentence when it ends on the comment's line, or -1.
        TrailingSentence: int
        mutable Decl: int
        mutable AttachedTo: int
    }

type Proof =
    {
        /// The `Proof ...` sentence, or -1.
        Opener: int
        FirstSentence: int
        LastSentence: int
        Span: Span
        BodySpan: Span
        /// `Qed`, `Defined`, `Admitted`, `Abort`, `Save`, `ProofTerm`, or `Unterminated`.
        Ending: string
        TacticSentences: int
    }

type Symbol =
    {
        Line: int
        Name: string
        Path: string
        Qualified: string
        Kind: string
        Span: Span
        Sentence: int
        mutable Decl: int
    }

type Declaration =
    {
        Index: int
        mutable Id: string
        Name: string
        /// `glob` when `.glob` names the declaration, `source` when the sentence's text does, `none` when anonymous.
        NameSource: string
        NameSpan: Span voption
        Keyword: string
        KeywordSpan: Span
        Modifiers: string[]
        Category: string
        Sentence: int
        SentenceSpan: Span
        mutable Extent: Span
        Statement: Span voption
        Binders: Span voption
        Type: Span voption
        Body: Span voption
        Proofs: ResizeArray<Proof>
        Symbols: ResizeArray<int>
        Docs: ResizeArray<int>
        Scope: int
    }

type Scope =
    {
        Index: int
        /// `Section`, `Module` or `Module Type`.
        Kind: string
        Name: string
        Parent: int
        Sentence: int
        /// The `End` sentence; -1 when the scope stays open, and `Sentence` for `Module A := B`.
        mutable EndSentence: int
        mutable Span: Span
        Docs: ResizeArray<int>
    }

type ImportedModule =
    {
        Text: string
        Span: Span
        Resolved: string
    }

type Import =
    {
        Sentence: int
        Keyword: string
        From: string
        Mode: string
        Modules: ImportedModule[]
    }

type Notation =
    {
        Sentence: int
        Decl: int
        Keyword: string
        /// The notation string with its quotes removed and `""` unescaped, or null.
        Literal: string
        LiteralSpan: Span voption
        /// Index into `Symbols`, or -1.
        Symbol: int
        /// Decoded from the `.glob` name `:entry:scope:key`. Null when absent or empty.
        Entry: string
        Scope: string
        Key: string
    }

type Target =
    {
        Module: string
        Path: string
        Name: string
        Kind: string
        /// The binder named by the target, or -1.
        Binder: int
        /// The symbol named by a `name:uid` target with no binder line, or -1. Rocq writes such a target for a name
        /// under definition: a recursive call, a mutual sibling, or a `Context` variable.
        Symbol: int
    }

/// A reference with its target interned. `Ref` arrays are sorted by `(Start, End, Line)`.
[<Struct>]
type Ref = { Line: int; Span: Span; Target: int }

type Binder =
    {
        Line: int
        Span: Span
        Name: string
        Uid: string
    }

/// One aggregated mention of a target by a declaration or a top-level directive.
[<Struct>]
type Use =
    {
        Owner: int
        Target: int
        /// 0 statement, 1 body, 2 proof, 3 top-level directive.
        Where: int
        Count: int
    }

type Diagnostic =
    {
        Severity: string
        Code: string
        Message: string
        Offset: int
    }

type FileModel =
    {
        Bytes: byte[]
        Tokens: Token[]
        Sentences: Sentence[]
        Comments: Comment[]
        Headings: Heading[]
        Declarations: Declaration[]
        Scopes: Scope[]
        Imports: Import[]
        Notations: Notation[]
        Symbols: Symbol[]
        Binders: Binder[]
        Targets: Target[]
        Refs: Ref[]
        Uses: Use[]
        GlobLineCount: int
        Diagnostics: Diagnostic[]
    }

/// Words quoted as tokens in `g_constr.mlg`, `g_vernac.mlg` or `g_tactic.mlg`, marked `Keyword` as a rendering hint.
let private reservedWords =
    HashSet<string>(
        [
            "forall"
            "fun"
            "match"
            "with"
            "end"
            "as"
            "in"
            "return"
            "let"
            "if"
            "then"
            "else"
            "fix"
            "cofix"
            "Prop"
            "Set"
            "SProp"
            "Type"
            "at"
            "for"
            "where"
        ]
    )

let private modifierWords =
    HashSet<string>(
        [
            "Local"
            "Global"
            "Polymorphic"
            "Monomorphic"
            "Cumulative"
            "NonCumulative"
            "Private"
            "Program"
            "Time"
            "Fail"
            "Succeed"
            "Instructions"
            "Profile"
            "Timeout"
            "Redirect"
        ]
    )

let private twoWordKeywords =
    HashSet<string>(
        [
            "Module Type"
            "Reserved Notation"
            "Reserved Infix"
            "Tactic Notation"
            "Combined Scheme"
            "Functional Scheme"
            "Declare Module"
            "Declare Instance"
            "Declare Scope"
            "Declare Custom"
            "Declare ML"
            "Declare Reduction"
            "Existing Instance"
            "Existing Instances"
            "Existing Class"
            "Next Obligation"
            "Final Obligation"
            "Let Fixpoint"
            "Let CoFixpoint"
            "Identity Coercion"
            "Canonical Structure"
            "Abort All"
            "Print Assumptions"
            "Open Scope"
            "Close Scope"
            "Delimit Scope"
            "Bind Scope"
            "Proof Mode"
            "Solve Obligations"
            "Admit Obligations"
            "Solve All"
            "Obligation Tactic"
            "Show Obligation"
        ]
    )

let private categoryOfKeyword (keyword: string) =
    match keyword with
    | "Theorem"
    | "Lemma"
    | "Fact"
    | "Remark"
    | "Corollary"
    | "Proposition"
    | "Property" -> "theorem"
    | "Goal" -> "goal"
    | "Definition"
    | "Example"
    | "Fixpoint"
    | "CoFixpoint"
    | "Let"
    | "Let Fixpoint"
    | "Let CoFixpoint"
    | "Instance"
    | "Function" -> "definition"
    | "Inductive"
    | "CoInductive"
    | "Variant"
    | "Record"
    | "Structure"
    | "Class" -> "inductive"
    | "Axiom"
    | "Axioms"
    | "Parameter"
    | "Parameters"
    | "Conjecture"
    | "Conjectures"
    | "Variable"
    | "Variables"
    | "Hypothesis"
    | "Hypotheses"
    | "Context"
    | "Symbol"
    | "Symbols"
    | "Primitive"
    | "Declare Instance" -> "assumption"
    | "Notation"
    | "Infix"
    | "Reserved Notation"
    | "Reserved Infix"
    | "Tactic Notation" -> "notation"
    | "Ltac"
    | "Ltac2" -> "ltac"
    | "Scheme"
    | "Combined Scheme"
    | "Functional Scheme" -> "scheme"
    | "Section" -> "section"
    | "Module"
    | "Module Type"
    | "Declare Module" -> "module"
    | "End" -> "end"
    | "From"
    | "Require"
    | "Import"
    | "Export" -> "import"
    | "Proof" -> "proofStart"
    | "Qed"
    | "Defined"
    | "Admitted"
    | "Abort"
    | "Abort All"
    | "Save" -> "proofEnd"
    | "Next Obligation"
    | "Final Obligation"
    | "Obligation" -> "obligation"
    | _ -> "directive"

let private isDeclarationCategory (category: string) =
    match category with
    | "theorem"
    | "goal"
    | "definition"
    | "inductive"
    | "assumption"
    | "notation"
    | "ltac"
    | "scheme" -> true
    | _ -> false

/// The newlines in a whitespace token.
let private newlines (src: byte[]) (t: Token) =
    let mutable n = 0

    for i in t.Start .. t.End - 1 do
        if src[i] = 10uy then
            n <- n + 1

    n

let private tokenText (src: byte[]) (t: Token) =
    Encoding.UTF8.GetString (src, t.Start, t.End - t.Start)

let private isSignificant (t: Token) =
    t.Kind <> TokenKind.Whitespace
    && t.Kind <> TokenKind.Comment

type private Header =
    {
        Modifiers: string[]
        Keyword: string
        KeywordSpan: Span voption
        /// Position in the significant-token list after the keyword.
        Next: int
    }

/// Reads modifiers and the keyword from a sentence's significant tokens.
let private parseHeader (src: byte[]) (tokens: Token[]) (sg: int[]) =
    let mods = ResizeArray<string>()
    let mutable p = 0
    let mutable go = true

    let at k =
        tokens[sg[k]]

    while go && p < sg.Length do
        let t = at p
        let s = tokenText src t

        if
            s = "#"
            && p + 1 < sg.Length
            && tokenText src (at (p + 1)) = "["
        then
            let start = t.Start
            let mutable depth = 0
            let mutable q = p + 1
            let mutable closed = false

            while not closed && q < sg.Length do
                match tokenText src (at q) with
                | "[" -> depth <- depth + 1
                | "]" ->
                    depth <- depth - 1

                    if depth = 0 then
                        closed <- true
                | _ -> ()

                q <- q + 1

            mods.Add (Encoding.UTF8.GetString (src, start, (at (q - 1)).End - start))
            p <- q
        elif
            t.Kind = TokenKind.Ident
            && modifierWords.Contains s
        then
            mods.Add s
            p <- p + 1

            if
                (s = "Timeout" || s = "Redirect" || s = "Profile")
                && p < sg.Length
            then
                let arg = at p

                if
                    arg.Kind = TokenKind.Number
                    || arg.Kind = TokenKind.String
                then
                    mods[mods.Count - 1] <- s + " " + tokenText src arg
                    p <- p + 1
        else
            go <- false

    if p < sg.Length && (at p).Kind = TokenKind.Ident then
        let first = at p
        let word = tokenText src first

        if
            p + 1 < sg.Length
            && (at (p + 1)).Kind = TokenKind.Ident
            && twoWordKeywords.Contains (word + " " + tokenText src (at (p + 1)))
        then
            let second = at (p + 1)

            {
                Modifiers = mods.ToArray ()
                Keyword = word + " " + tokenText src second
                KeywordSpan = ValueSome (span first.Start second.End)
                Next = p + 2
            }
        else
            {
                Modifiers = mods.ToArray ()
                Keyword = word
                KeywordSpan = ValueSome (span first.Start first.End)
                Next = p + 1
            }
    else
        {
            Modifiers = mods.ToArray ()
            Keyword = null
            KeywordSpan = ValueNone
            Next = p
        }

/// Finds the first `wanted` token at bracket depth zero in positions `from .. sg.Length - 1`. The `:=` of a top-level
/// `let` in a statement is skipped.
let private findTopLevel (src: byte[]) (tokens: Token[]) (sg: int[]) (from: int) (wanted: string) =
    let mutable depth = 0
    let mutable found = -1
    let mutable p = from
    let mutable pendingLets = 0

    while found < 0 && p < sg.Length do
        let t = tokens[sg[p]]

        if t.Kind = TokenKind.Symbol then
            let s = tokenText src t

            match s with
            | "("
            | "["
            | "{" -> depth <- depth + 1
            | ")"
            | "]"
            | "}" -> depth <- depth - 1
            | ":=" when depth = 0 && pendingLets > 0 -> pendingLets <- pendingLets - 1
            | _ when depth = 0 && s = wanted -> found <- p
            | _ -> ()
        elif
            t.Kind = TokenKind.Ident
            && depth = 0
            && tokenText src t = "let"
        then
            pendingLets <- pendingLets + 1

        p <- p + 1

    found

/// The span from significant position `a` to before position `b`, or none when empty.
let private sigSpan (tokens: Token[]) (sg: int[]) a b =
    if a < b && b <= sg.Length then
        ValueSome (span tokens[sg[a]].Start tokens[sg[b - 1]].End)
    else
        ValueNone

let private commentFormat (src: byte[]) (t: Token) =
    let len = t.End - t.Start

    let at k =
        src[t.Start + k]

    if
        len >= 8
        && at 2 = 42uy
        && at 3 = 109uy
        && at 4 = 100uy
        && (at 5 = 32uy
            || at 5 = 10uy
            || at 5 = 13uy
            || at 5 = 9uy)
    then
        "md", span (t.Start + 5) (t.End - 2)
    elif
        len >= 6
        && at 2 = 42uy
        && at 3 <> 42uy
        && at 3 <> 41uy
    then
        "doc", span (t.Start + 3) (t.End - 2)
    else
        "plain", span (t.Start + 2) (max (t.Start + 2) (t.End - 2))

/// Reads headings from a comment body, line by line.
/// Markdown: 1 to 6 `#` and a space, outside fences. Coqdoc: 1 to 4 `*` and a space, outside `<<` `>>` blocks.
let private headingsOf (src: byte[]) (commentIndex: int) (format: string) (body: Span) =
    let result = ResizeArray<Heading>()
    let mutable lineStart = body.Start
    let mutable verbatim = false

    while lineStart < body.End do
        let mutable lineEnd = lineStart

        while lineEnd < body.End && src[lineEnd] <> 10uy do
            lineEnd <- lineEnd + 1

        let mutable p = lineStart

        while p < lineEnd && (src[p] = 32uy || src[p] = 9uy) do
            p <- p + 1

        let marker = if format = "md" then 35uy else 42uy
        let maxLevel = if format = "md" then 6 else 4
        let line = Encoding.UTF8.GetString(src, p, lineEnd - p).TrimEnd()

        if format = "md" && line.StartsWith "```" then
            verbatim <- not verbatim
        elif format = "doc" && line.StartsWith "<<" then
            verbatim <- true
        elif format = "doc" && line.StartsWith ">>" then
            verbatim <- false
        elif not verbatim then
            let mutable q = p

            while q < lineEnd && src[q] = marker do
                q <- q + 1

            let level = q - p

            if
                level >= 1
                && level <= maxLevel
                && q < lineEnd
                && src[q] = 32uy
            then
                let title = Encoding.UTF8.GetString (src, q + 1, lineEnd - q - 1)
                let trimmed = title.Trim ()
                let leading = title.Length - title.TrimStart().Length
                let start = q + 1 + leading
                let stop = start + Encoding.UTF8.GetByteCount trimmed

                if trimmed <> "" then
                    result.Add
                        {
                            Level = level
                            Text = trimmed
                            Span = span start stop
                            Format = format
                            Comment = commentIndex
                        }

        lineStart <- lineEnd + 1

    result

/// True when the token ends a sentence. A `...` split by `.glob` spans counts once.
let private endsSentence (tokens: Token[]) (i: int) =
    match tokens[i].Kind with
    | TokenKind.Terminator ->
        i + 1 >= tokens.Length
        || tokens[i + 1].Kind <> TokenKind.Terminator
    | TokenKind.Bullet
    | TokenKind.Focus -> true
    | _ -> false

let private qualify (path: string) (name: string) =
    match path, name with
    | "<>", n -> n
    | p, "<>" -> p
    | p, n -> p + "." + n

/// Modules, sections and terms occupy separate namespaces in Rocq, so each resolves separately.
let namespaceOf (kind: string) =
    match kind with
    | "mod"
    | "modtype" -> "m"
    | "sec" -> "s"
    | _ -> "t"

let private isBinderKind (kind: string) =
    kind = "binder"

let private lastIndexAtOrBefore (starts: int[]) (value: int) =
    let i = Array.BinarySearch (starts, value)
    if i >= 0 then i else ~~~i - 1

/// Builds the structure of one file from its source bytes and its parsed `.glob`, when one is available.
let analyze (moduleName: string) (src: byte[]) (glob: GlobFile option) : FileModel =
    let diagnostics = ResizeArray<Diagnostic>()

    let diag severity code message offset =
        diagnostics.Add
            {
                Severity = severity
                Code = code
                Message = message
                Offset = offset
            }

    let defs =
        glob
        |> Option.map _.Defs
        |> Option.defaultValue [||]

    let rawRefs =
        glob
        |> Option.map _.Refs
        |> Option.defaultValue [||]

    let validSpan line (s: Span) =
        let ok =
            s.Start >= 0
            && s.End <= src.Length
            && s.Start < s.End
            && isCharBoundary src s.Start
            && isCharBoundary src s.End

        if not ok then
            diag "error" "glob-span-invalid" $"line {line + 1}: span {s.Start}:{s.End} is outside the file or inside a character" (max s.Start 0)

        ok

    let defs =
        defs
        |> Array.filter (fun d -> validSpan d.Line d.Span)

    let rawRefs =
        rawRefs
        |> Array.filter (fun r -> validSpan r.Line r.Span)

    let boundaries =
        seq {
            for d in defs do
                yield d.Span.Start
                yield d.Span.End

            for r in rawRefs do
                yield r.Span.Start
                yield r.Span.End
        }
        |> Seq.toArray
        |> Array.distinct
        |> Array.sort

    let rawTokens, lexIssues = lex src

    for issue in lexIssues do
        diag "error" "lex" issue.Message issue.Offset

    let tokens = refine rawTokens boundaries

    // Segment the file. Whitespace and comments outside a sentence stay at top level.
    let sentenceBuild = ResizeArray<struct (int * int * bool)>()
    let commentTokens = ResizeArray<struct (int * int)>() // token index, sentence index or -1
    let mutable open' = -1

    for i in 0 .. tokens.Length - 1 do
        match tokens[i].Kind with
        | TokenKind.Whitespace -> ()
        | TokenKind.Comment -> commentTokens.Add (struct (i, (if open' >= 0 then sentenceBuild.Count else -1)))
        | _ ->
            if open' < 0 then
                open' <- i

            if endsSentence tokens i then
                sentenceBuild.Add (struct (open', i + 1, true))
                open' <- -1

    if open' >= 0 then
        let mutable last = tokens.Length

        while last > open'
              && not (isSignificant tokens[last - 1]) do
            last <- last - 1

        sentenceBuild.Add (struct (open', last, false))
        diag "error" "sentence-unterminated" "the file ends inside a sentence" tokens[open'].Start

    let sentenceCount = sentenceBuild.Count
    let starts = Array.zeroCreate<int> sentenceCount
    let sigTokens = Array.zeroCreate<int[]> sentenceCount
    let headers = Array.zeroCreate<Header> sentenceCount

    let sentences =
        Array.init sentenceCount (fun k ->
            let struct (first, stop, terminated) = sentenceBuild[k]

            let sg =
                [|
                    for i in first .. stop - 1 do
                        if isSignificant tokens[i] then
                            i
                |]

            starts[k] <- tokens[first].Start

            let content =
                if
                    terminated
                    && tokens[stop - 1].Kind = TokenKind.Terminator
                then
                    Array.sub sg 0 (sg.Length - 1)
                else
                    sg

            // Drop a split terminator's first half too.
            let content =
                if
                    terminated
                    && content.Length > 0
                    && tokens[content[content.Length - 1]].Kind = TokenKind.Terminator
                then
                    Array.sub content 0 (content.Length - 1)
                else
                    content

            sigTokens[k] <- content
            let header = parseHeader src tokens content
            headers[k] <- header

            {
                Index = k
                Span = span tokens[first].Start tokens[stop - 1].End
                FirstToken = first
                EndToken = stop
                Category = "directive"
                Keyword = header.Keyword
                KeywordSpan = header.KeywordSpan
                Modifiers = header.Modifiers
                Decl = -1
                Terminated = terminated
            })

    // Symbols and binders, in `.glob` line order.
    let binders = ResizeArray<Binder>()
    let binderByUid = Dictionary<string, int>()
    let symbols = ResizeArray<Symbol>()

    let sentenceOf (offset: int) =
        let k = lastIndexAtOrBefore starts offset

        if k >= 0 && offset < sentences[k].Span.End then k else -1

    for d in defs do
        if isBinderKind d.Kind then
            let colon = d.Name.LastIndexOf ':'
            let name = if colon > 0 then d.Name.Substring (0, colon) else d.Name
            binderByUid[d.Name] <- binders.Count

            binders.Add
                {
                    Line = d.Line
                    Span = d.Span
                    Name = name
                    Uid = d.Name
                }
        else
            symbols.Add
                {
                    Line = d.Line
                    Name = d.Name
                    Path = d.Path
                    Qualified = qualify d.Path d.Name
                    Kind = d.Kind
                    Span = d.Span
                    Sentence = sentenceOf d.Span.Start
                    Decl = -1
                }

    let symbolsBySentence = Dictionary<int, ResizeArray<int>>()

    for i in 0 .. symbols.Count - 1 do
        let s = symbols[i]

        if s.Sentence < 0 then
            diag "warning" "symbol-outside-sentence" $"{s.Kind} {s.Name} lies outside every sentence" s.Span.Start
        else
            match symbolsBySentence.TryGetValue s.Sentence with
            | true, l -> l.Add i
            | _ -> symbolsBySentence[s.Sentence] <- ResizeArray [ i ]

    // Comments.
    let commentData =
        commentTokens.ToArray ()
        |> Array.mapi (fun index (struct (ti, sentence)) ->
            let t = tokens[ti]
            let format, body = commentFormat src t
            let next = lastIndexAtOrBefore starts t.Start + 1
            let next = if next < sentenceCount then next else -1
            let inner = sentence >= 0

            let next = if inner then -1 else next

            let mutable gap = 0

            if next >= 0 then
                for k in t.End .. starts[next] - 1 do
                    if src[k] = 10uy then
                        gap <- gap + 1

            let prev = lastIndexAtOrBefore starts t.Start

            let trailing =
                if inner || prev < 0 then
                    -1
                else
                    let mutable sameLine = true

                    for k in sentences[prev].Span.End .. t.Start - 1 do
                        if src[k] = 10uy then
                            sameLine <- false

                    if sameLine then prev else -1

            {
                Index = index
                Span = span t.Start t.End
                Format = format
                BodySpan = body
                Sentence = sentence
                NextSentence = next
                NewlinesToNext = gap
                TrailingSentence = trailing
                Decl = -1
                AttachedTo = -1
            })

    let headings =
        commentData
        |> Array.collect (fun c ->
            if c.Format = "plain" then
                [||]
            else
                (headingsOf src c.Index c.Format c.BodySpan).ToArray())

    let declarations = ResizeArray<Declaration>()
    let scopes = ResizeArray<Scope>()
    let scopeStack = Stack<int>()
    let imports = ResizeArray<Import>()
    let notationSentences = ResizeArray<int>()

    // An open proof: its owner, first sentence, opener and tactic count.
    let proofStack = Stack<struct (int * int * int ref * int ref)>()
    let mutable lastProgramDecl = -1

    let closeProof (k: int) (ending: string) =
        if proofStack.Count = 0 then
            diag "warning" "proof-end-without-proof" $"{ending} with no open proof" sentences[k].Span.Start
        else
            let struct (owner, first, opener, tactics) = proofStack.Pop ()
            let startSentence = sentences[first]

            let bodyStart =
                if opener.Value >= 0 && opener.Value < k then
                    sentences[opener.Value + 1].Span.Start
                else
                    startSentence.Span.Start

            let proof =
                {
                    Opener = opener.Value
                    FirstSentence = first
                    LastSentence = k
                    Span = span startSentence.Span.Start sentences[k].Span.End
                    BodySpan = span bodyStart (max bodyStart sentences[k].Span.Start)
                    Ending = ending
                    TacticSentences = tactics.Value
                }

            if owner >= 0 then
                let d = declarations[owner]
                d.Proofs.Add proof
                d.Extent <- span d.Extent.Start (max d.Extent.End proof.Span.End)

    let currentOwner () =
        if proofStack.Count > 0 then
            let struct (owner, _, _, _) = proofStack.Peek ()
            owner
        else
            -1

    let attachDocs (k: int) (docs: ResizeArray<int>) (declIndex: int) =
        // Walks back from the sentence over comments separated by at most one newline.
        let collected = ResizeArray<int>()
        let mutable c = 0
        let mutable hi = commentData.Length - 1
        let mutable lo = 0

        while lo <= hi do
            let mid = (lo + hi) / 2

            if commentData[mid].Span.Start < sentences[k].Span.Start then
                c <- mid + 1
                lo <- mid + 1
            else
                hi <- mid - 1

        let mutable prevStart = sentences[k].Span.Start
        let mutable i = c - 1
        let mutable go = true
        let previousEnd = if k > 0 then sentences[k - 1].Span.End else 0

        while go && i >= 0 do
            let comment = commentData[i]

            if
                comment.Sentence >= 0
                || comment.Span.End <= previousEnd
                || comment.TrailingSentence >= 0
            then
                go <- false
            else
                let mutable nl = 0

                for q in comment.Span.End .. prevStart - 1 do
                    if src[q] = 10uy then
                        nl <- nl + 1

                if nl > 1 then
                    go <- false
                else
                    collected.Add i
                    prevStart <- comment.Span.Start
                    i <- i - 1

        for j in collected.Count - 1 .. -1 .. 0 do
            docs.Add collected[j]

            if declIndex >= 0 then
                commentData[collected[j]].AttachedTo <- declIndex

    let currentScope () =
        if scopeStack.Count > 0 then scopeStack.Peek () else -1

    for k in 0 .. sentenceCount - 1 do
        let s = sentences[k]
        let sg = sigTokens[k]
        let header = headers[k]
        let failing = Array.contains "Fail" s.Modifiers
        let inProof = proofStack.Count > 0

        let keywordCategory =
            if isNull s.Keyword then
                "directive"
            else
                categoryOfKeyword s.Keyword

        let firstKind = tokens[s.FirstToken].Kind

        if
            firstKind = TokenKind.Bullet
            || firstKind = TokenKind.Focus
        then
            s.Category <- (if firstKind = TokenKind.Bullet then "bullet" else "focus")
            s.Decl <- currentOwner ()
        elif
            sg.Length > 0
            && tokens[sg[sg.Length - 1]].Kind = TokenKind.Focus
        then
            s.Category <- "focus"
            s.Decl <- currentOwner ()
        elif failing then
            s.Category <- "directive"
            s.Decl <- currentOwner ()
        elif not s.Terminated then
            s.Category <- "unterminated"
        else
            let hasSymbols =
                match symbolsBySentence.TryGetValue k with
                | true, l ->
                    l
                    |> Seq.exists (fun i -> namespaceOf symbols[i].Kind = "t")
                | _ -> false

            let isDecl =
                isDeclarationCategory keywordCategory
                || (not inProof
                    && hasSymbols
                    && keywordCategory = "directive")

            if isDecl then
                let category =
                    if isDeclarationCategory keywordCategory then
                        keywordCategory
                    else
                        "other"

                s.Category <- category

                let own =
                    match symbolsBySentence.TryGetValue k with
                    | true, l -> l
                    | _ -> ResizeArray ()

                let primary =
                    own
                    |> Seq.filter (fun i ->
                        match symbols[i].Kind with
                        | "constr"
                        | "proj"
                        | "scheme"
                        | "not" -> false
                        | _ -> true)
                    |> Seq.tryHead
                    |> Option.orElse (Seq.tryHead own)

                let afterKeyword = header.Next

                let name, nameSource, nameSpan =
                    match primary with
                    // A `Goal`'s `.glob` span covers its whole statement, and its name is generated.
                    | Some i when category = "goal" -> symbols[i].Name, "glob", ValueNone
                    | Some i -> symbols[i].Name, "glob", ValueSome symbols[i].Span
                    | None when category = "ltac" && afterKeyword < sg.Length ->
                        let t = tokens[sg[afterKeyword]]
                        tokenText src t, "source", ValueSome (span t.Start t.End)
                    | None -> null, "none", ValueNone

                // Position after the name: past a whole string for a notation, whose span is the opening quote.
                let mutable afterName = afterKeyword

                match nameSpan with
                | ValueSome ns ->
                    while afterName < sg.Length
                          && tokens[sg[afterName]].End <= ns.End do
                        afterName <- afterName + 1
                | ValueNone -> ()

                while afterName < sg.Length
                      && afterName > 0
                      && tokens[sg[afterName]].Kind = TokenKind.String
                      && tokens[sg[afterName - 1]].Kind = TokenKind.String
                      && tokens[sg[afterName - 1]].End = tokens[sg[afterName]].Start do
                    afterName <- afterName + 1

                // An assumption's names sit among its binders, so its statement starts at the keyword's end.
                if category = "assumption" then
                    afterName <- afterKeyword

                let coloneq = findTopLevel src tokens sg afterName ":="
                let stmtEnd = if coloneq >= 0 then coloneq else sg.Length
                let colon = findTopLevel src tokens (Array.sub sg 0 stmtEnd) afterName ":"
                let index = declarations.Count

                let decl =
                    {
                        Index = index
                        Id = null
                        Name = name
                        NameSource = nameSource
                        NameSpan = nameSpan
                        Keyword = s.Keyword
                        KeywordSpan = s.KeywordSpan |> ValueOption.defaultValue s.Span
                        Modifiers = s.Modifiers
                        Category = category
                        Sentence = k
                        SentenceSpan = s.Span
                        Extent = s.Span
                        Statement = sigSpan tokens sg afterName stmtEnd
                        Binders =
                            if colon >= 0 then
                                sigSpan tokens sg afterName colon
                            else
                                sigSpan tokens sg afterName stmtEnd
                        Type =
                            if colon >= 0 then
                                sigSpan tokens sg (colon + 1) stmtEnd
                            else
                                ValueNone
                        Body =
                            if coloneq >= 0 then
                                sigSpan tokens sg (coloneq + 1) sg.Length
                            else
                                ValueNone
                        Proofs = ResizeArray ()
                        Symbols = own
                        Docs = ResizeArray ()
                        Scope = currentScope ()
                    }

                declarations.Add decl
                s.Decl <- index

                for i in own do
                    symbols[i].Decl <- index

                attachDocs k decl.Docs index

                if Array.contains "Program" s.Modifiers then
                    lastProgramDecl <- index

                if category = "notation" then
                    notationSentences.Add k

                let opensProof =
                    category = "theorem"
                    || category = "goal"
                    || category = "definition" && coloneq < 0

                if opensProof then
                    proofStack.Push (struct (index, k + 1, ref -1, ref 0))
            else
                match keywordCategory with
                | "proofStart" when s.Keyword = "Proof" ->
                    s.Category <- "proofStart"
                    s.Decl <- currentOwner ()

                    if proofStack.Count = 0 then
                        diag "warning" "proof-without-declaration" "Proof with no open goal" s.Span.Start
                    else
                        let struct (_, _, opener, _) = proofStack.Peek ()
                        opener.Value <- k

                        let follow = if sg.Length > 1 then tokenText src tokens[sg[1]] else ""

                        if
                            sg.Length > 1
                            && follow <> "using"
                            && follow <> "with"
                        then
                            closeProof k "ProofTerm"
                | "proofEnd" ->
                    s.Category <- "proofEnd"
                    s.Decl <- currentOwner ()

                    match s.Keyword with
                    | "Abort All" ->
                        while proofStack.Count > 0 do
                            closeProof k "Abort"
                    | word -> closeProof k word
                | "obligation" ->
                    s.Category <- "obligation"
                    s.Decl <- lastProgramDecl
                    proofStack.Push (struct (lastProgramDecl, k, ref k, ref 0))
                | _ when inProof ->
                    s.Category <-
                        (if keywordCategory = "directive" then
                             "tactic"
                         else
                             keywordCategory)

                    s.Decl <- currentOwner ()

                    if s.Category = "tactic" then
                        let struct (_, _, _, tactics) = proofStack.Peek ()
                        tactics.Value <- tactics.Value + 1
                | "section" ->
                    s.Category <- "section"

                    let name =
                        if header.Next < sg.Length then
                            tokenText src tokens[sg[header.Next]]
                        else
                            null

                    let index = scopes.Count

                    let scope =
                        {
                            Index = index
                            Kind = "Section"
                            Name = name
                            Parent = currentScope ()
                            Sentence = k
                            EndSentence = -1
                            Span = s.Span
                            Docs = ResizeArray ()
                        }

                    scopes.Add scope
                    attachDocs k scope.Docs -1
                    scopeStack.Push index
                | "module" ->
                    s.Category <- "module"
                    let declared = s.Keyword = "Declare Module"
                    let mutable p = header.Next

                    while p < sg.Length
                          && (let w = tokenText src tokens[sg[p]] in w = "Import" || w = "Export") do
                        p <- p + 1

                    let name = if p < sg.Length then tokenText src tokens[sg[p]] else null
                    let isAlias = findTopLevel src tokens sg p ":=" >= 0
                    let index = scopes.Count

                    let scope =
                        {
                            Index = index
                            Kind =
                                (if s.Keyword = "Module Type" then
                                     "Module Type"
                                 else
                                     "Module")
                            Name = name
                            Parent = currentScope ()
                            Sentence = k
                            EndSentence = (if isAlias || declared then k else -1)
                            Span = s.Span
                            Docs = ResizeArray ()
                        }

                    scopes.Add scope
                    attachDocs k scope.Docs -1

                    if not (isAlias || declared) then
                        scopeStack.Push index
                | "end" ->
                    s.Category <- "end"

                    let name =
                        if header.Next < sg.Length then
                            tokenText src tokens[sg[header.Next]]
                        else
                            null

                    if scopeStack.Count = 0 then
                        diag "warning" "end-without-scope" $"End {name} closes no scope" s.Span.Start
                    else
                        let index = scopeStack.Pop ()
                        let scope = scopes[index]

                        if scope.Name <> name then
                            diag "warning" "end-name-mismatch" $"End {name} closes {scope.Name}" s.Span.Start

                        scope.EndSentence <- k
                        scope.Span <- span scope.Span.Start s.Span.End
                | "import" ->
                    s.Category <- "import"
                    let words = sg |> Array.map (fun i -> tokens[i])
                    let mutable p = header.Next - 1
                    let mutable from = null
                    let mutable mode = null

                    if s.Keyword = "From" && p + 1 < words.Length then
                        from <- tokenText src words[p + 1]
                        p <- p + 2

                    if
                        p < words.Length
                        && tokenText src words[p] = "Require"
                    then
                        p <- p + 1

                    if s.Keyword = "Import" || s.Keyword = "Export" then
                        mode <- s.Keyword
                        p <- header.Next
                    elif p < words.Length then
                        let w = tokenText src words[p]

                        if w = "Import" || w = "Export" then
                            mode <- w
                            p <- p + 1

                    let modules =
                        [|
                            for q in p .. words.Length - 1 do
                                let t = words[q]

                                if t.Kind = TokenKind.Ident then
                                    let resolved =
                                        rawRefs
                                        |> Array.tryFind (fun r ->
                                            r.Span.Start = t.Start
                                            && r.Span.End = t.End
                                            && (r.Kind = "lib"
                                                || r.Kind = "mod"
                                                || r.Kind = "modtype"))
                                        |> Option.map (fun r ->
                                            if r.Kind = "lib" then
                                                r.Module
                                            else
                                                r.Module + "." + qualify r.Path r.Name)

                                    {
                                        Text = tokenText src t
                                        Span = span t.Start t.End
                                        Resolved = Option.toObj resolved
                                    }
                        |]

                    imports.Add
                        {
                            Sentence = k
                            Keyword = s.Keyword
                            From = from
                            Mode = mode
                            Modules = modules
                        }
                | _ -> s.Category <- "directive"

    // Proofs open at the end of the file.
    while proofStack.Count > 0 do
        let struct (owner, first, opener, tactics) = proofStack.Pop ()
        diag "warning" "proof-unterminated" "the file ends inside a proof" src.Length

        if owner >= 0 && first < sentenceCount then
            let d = declarations[owner]
            let e = sentences[sentenceCount - 1].Span.End

            d.Proofs.Add
                {
                    Opener = opener.Value
                    FirstSentence = first
                    LastSentence = sentenceCount - 1
                    Span = span sentences[first].Span.Start e
                    BodySpan = span sentences[first].Span.Start e
                    Ending = "Unterminated"
                    TacticSentences = tactics.Value
                }

            d.Extent <- span d.Extent.Start e

    for scope in scopes do
        if scope.EndSentence = -1 then
            diag "warning" "scope-unterminated" $"{scope.Kind} {scope.Name} has no End" sentences[scope.Sentence].Span.Start

    // Stable identifiers.
    let usedIds = HashSet<string>()

    for d in declarations do
        let baseId =
            if d.Symbols.Count > 0 && d.NameSource = "glob" then
                let primary =
                    d.Symbols
                    |> Seq.map (fun i -> symbols[i])
                    |> Seq.tryFind (fun s -> s.Name = d.Name)

                match primary with
                | Some s -> moduleName + "#" + s.Qualified
                | None -> moduleName + "#" + d.Name
            elif d.NameSource = "source" then
                moduleName + "#" + d.Name
            else
                moduleName + "#@" + string d.Sentence

        d.Id <-
            if usedIds.Add baseId then
                baseId
            else
                baseId + "@" + string d.Sentence

        usedIds.Add d.Id |> ignore

    // Comments inside a declaration's extent belong to it.
    if declarations.Count > 0 then
        for c in commentData do
            let mutable owner = -1

            let k = lastIndexAtOrBefore starts c.Span.Start

            if
                k >= 0
                && sentences[k].Decl >= 0
                && c.Span.Start < declarations[sentences[k].Decl].Extent.End
            then
                owner <- sentences[k].Decl

            if owner < 0 && k >= 0 then
                // Between sentences of an owning proof, or after the last declaration of a nested proof.
                let next = k + 1

                if
                    next < sentenceCount
                    && sentences[next].Decl >= 0
                    && sentences[k].Decl = sentences[next].Decl
                then
                    owner <- sentences[k].Decl

            c.Decl <- owner

    // Interned targets and sorted references.
    let targetIndex = Dictionary<struct (string * string * string * string), int>()
    let targets = ResizeArray<Target>()

    // A `name:uid` target with no binder line names a symbol of the sentence holding the reference.
    let enclosingSymbol (r: GlobRef) =
        let colon = r.Name.LastIndexOf ':'

        if
            r.Module <> moduleName
            || r.Path <> "<>"
            || colon <= 0
        then
            -1
        else
            let baseName = r.Name.Substring (0, colon)

            match symbolsBySentence.TryGetValue (sentenceOf r.Span.Start) with
            | true, own ->
                own
                |> Seq.tryFind (fun i -> symbols[i].Name = baseName)
                |> Option.defaultValue -1
            | _ -> -1

    let internTarget (r: GlobRef) =
        let key = struct (r.Module, r.Path, r.Name, r.Kind)

        match targetIndex.TryGetValue key with
        | true, i -> i
        | _ ->
            let binder =
                if r.Module = moduleName && r.Path = "<>" then
                    match binderByUid.TryGetValue r.Name with
                    | true, b -> b
                    | _ -> -1
                else
                    -1

            let i = targets.Count
            targetIndex[key] <- i

            targets.Add
                {
                    Module = r.Module
                    Path = r.Path
                    Name = r.Name
                    Kind = r.Kind
                    Binder = binder
                    Symbol = if binder >= 0 then -1 else enclosingSymbol r
                }

            i

    let refs =
        rawRefs
        |> Array.map (fun r ->
            {
                Line = r.Line
                Span = r.Span
                Target = internTarget r
            })
        |> Array.sortBy (fun r -> struct (r.Span.Start, r.Span.End, r.Line))

    // Aggregated uses. Binder references stay local to their file's refs layer.
    let useCounts = Dictionary<struct (int * int * int), int>()

    for r in refs do
        let t = targets[r.Target]

        if t.Binder < 0 && t.Kind <> "lib" then
            let k = sentenceOf r.Span.Start

            if k < 0 then
                diag "warning" "ref-outside-sentence" $"reference to {t.Name} lies outside every sentence" r.Span.Start
            else
                let owner = sentences[k].Decl

                let where =
                    if owner < 0 then
                        3
                    else
                        let d = declarations[owner]

                        if d.Sentence <> k then
                            2
                        else
                            match d.Body with
                            | ValueSome b when r.Span.Start >= b.Start -> 1
                            | _ -> 0

                let key = struct (owner, r.Target, where)

                match useCounts.TryGetValue key with
                | true, c -> useCounts[key] <- c + 1
                | _ -> useCounts[key] <- 1

    let uses =
        [|
            for KeyValue (struct (owner, target, where), count) in useCounts do
                {
                    Owner = owner
                    Target = target
                    Where = where
                    Count = count
                }
        |]
        |> Array.sortBy (fun u -> struct (u.Owner, u.Target, u.Where))

    // Notations: one item for each `not` or `abbrev` symbol, and one for each notation sentence without a symbol.
    let notations = ResizeArray<Notation>()

    let literalAt (offset: int) (k: int) =
        let sg = sigTokens[k]

        let first =
            sg
            |> Array.tryFindIndex (fun i ->
                tokens[i].Kind = TokenKind.String
                && tokens[i].Start >= offset)

        match first with
        | None -> null, ValueNone
        | Some p ->
            let start = tokens[sg[p]].Start
            let mutable q = p

            while q + 1 < sg.Length
                  && tokens[sg[q + 1]].Kind = TokenKind.String
                  && tokens[sg[q + 1]].Start = tokens[sg[q]].End do
                q <- q + 1

            let stop = tokens[sg[q]].End
            let raw = Encoding.UTF8.GetString (src, start + 1, stop - start - 2)
            raw.Replace ("\"\"", "\""), ValueSome (span start stop)

    let decodeNotation (name: string) =
        // `:entry:scope:key`, where only the key may contain further colons.
        if name.StartsWith ':' then
            let a = name.IndexOf (':', 1)
            let b = if a >= 0 then name.IndexOf (':', a + 1) else -1

            if b >= 0 then
                let empty (s: string) =
                    if s = "" then null else s

                empty (name.Substring (1, a - 1)), empty (name.Substring (a + 1, b - a - 1)), name.Substring (b + 1)
            else
                null, null, name
        else
            null, null, name

    let withSymbol = HashSet<int>()

    for i in 0 .. symbols.Count - 1 do
        let s = symbols[i]

        if
            s.Sentence >= 0
            && (s.Kind = "not" || s.Kind = "abbrev")
        then
            withSymbol.Add s.Sentence |> ignore

            let entry, scope, key =
                if s.Kind = "not" then
                    decodeNotation s.Name
                else
                    null, null, s.Name

            let literal, literalSpan =
                if s.Kind = "not" then
                    literalAt s.Span.Start s.Sentence
                else
                    null, ValueNone

            notations.Add
                {
                    Sentence = s.Sentence
                    Decl = s.Decl
                    Keyword = sentences[s.Sentence].Keyword
                    Literal = literal
                    LiteralSpan = literalSpan
                    Symbol = i
                    Entry = entry
                    Scope = scope
                    Key = key
                }

    for k in notationSentences do
        if not (withSymbol.Contains k) then
            let literal, literalSpan = literalAt sentences[k].Span.Start k

            notations.Add
                {
                    Sentence = k
                    Decl = sentences[k].Decl
                    Keyword = sentences[k].Keyword
                    Literal = literal
                    LiteralSpan = literalSpan
                    Symbol = -1
                    Entry = null
                    Scope = null
                    Key = null
                }

    let orderedNotations =
        notations.ToArray ()
        |> Array.sortBy (fun n -> struct (n.Sentence, n.Symbol))

    // Keyword tokens, from the classified sentences.
    for k in 0 .. sentenceCount - 1 do
        let s = sentences[k]

        for i in s.FirstToken .. s.EndToken - 1 do
            let t = tokens[i]

            if t.Kind = TokenKind.Ident then
                let isHeader =
                    match s.KeywordSpan with
                    | ValueSome ks ->
                        t.Start >= s.Span.Start
                        && t.End <= ks.End
                        && s.Category <> "tactic"
                    | ValueNone -> false

                if
                    isHeader
                    || reservedWords.Contains (tokenText src t)
                then
                    tokens[i] <- { t with Kind = TokenKind.Keyword }

    {
        Bytes = src
        Tokens = tokens
        Sentences = sentences
        Comments = commentData
        Headings = headings
        Declarations = declarations.ToArray ()
        Scopes = scopes.ToArray ()
        Imports = imports.ToArray ()
        Notations = orderedNotations
        Symbols = symbols.ToArray ()
        Binders = binders.ToArray ()
        Targets = targets.ToArray ()
        Refs = refs
        Uses = uses
        GlobLineCount =
            glob
            |> Option.map _.LineCount
            |> Option.defaultValue 0
        Diagnostics = diagnostics.ToArray ()
    }
