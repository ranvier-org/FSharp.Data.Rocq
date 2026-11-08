/// A byte scanner for Rocq vernacular, following `parsing/cLexer.ml` from Rocq 9.1.0. It has no keyword table, so a
/// notation symbol ending in `.` and followed by a blank reads as a sentence end.
module RocqData.Lexer

open System.Text
open RocqData.Source

type TokenKind =
    | Whitespace = 0
    | Comment = 1
    | Ident = 2
    | Number = 3
    | String = 4
    | Symbol = 5
    | Terminator = 6
    | Bullet = 7
    | Focus = 8
    | Quotation = 9
    | Keyword = 10

[<Struct>]
type Token =
    {
        Kind: TokenKind
        Start: int
        End: int
    }

type LexIssue = { Offset: int; Message: string }

let private isBlank (b: byte) =
    b = 32uy || b = 9uy || b = 10uy || b = 13uy

let private isAsciiIdentStart (b: byte) =
    b >= 97uy && b <= 122uy
    || b >= 65uy && b <= 90uy
    || b = 95uy

let private isDigit (b: byte) =
    b >= 48uy && b <= 57uy

let private isAsciiIdentPart (b: byte) =
    isAsciiIdentStart b || isDigit b || b = 39uy

let private isSingleCharSymbol (b: byte) =
    match char b with
    | '('
    | ')'
    | '['
    | ']'
    | '{'
    | '}'
    | ','
    | ';'
    | '\'' -> true
    | _ -> false

/// The class of the non-ASCII character at `i` and its byte length: 1 starts an identifier, 2 continues one,
/// 0 is a symbol.
let private unicodeClass (src: byte[]) (i: int) =
    let mutable rune = Unchecked.defaultof<Rune>
    let mutable consumed = 0

    match Rune.DecodeFromUtf8 (System.ReadOnlySpan (src, i, src.Length - i), &rune, &consumed) with
    | System.Buffers.OperationStatus.Done ->
        match Rune.GetUnicodeCategory rune with
        | System.Globalization.UnicodeCategory.UppercaseLetter
        | System.Globalization.UnicodeCategory.LowercaseLetter
        | System.Globalization.UnicodeCategory.TitlecaseLetter
        | System.Globalization.UnicodeCategory.ModifierLetter
        | System.Globalization.UnicodeCategory.OtherLetter
        | System.Globalization.UnicodeCategory.LetterNumber -> struct (1, consumed)
        | System.Globalization.UnicodeCategory.DecimalDigitNumber
        | System.Globalization.UnicodeCategory.OtherNumber
        | System.Globalization.UnicodeCategory.NonSpacingMark
        | System.Globalization.UnicodeCategory.SpacingCombiningMark -> struct (2, consumed)
        | _ -> struct (0, consumed)
    | _ -> struct (0, 1)

let private blankOrEof (src: byte[]) (i: int) =
    i >= src.Length || isBlank src[i]

let private dotRun (src: byte[]) (i: int) =
    let mutable n = 0

    while i + n < src.Length && src[i + n] = 46uy do
        n <- n + 1

    n

/// Scans a string whose opening quote is at `i`. Returns the offset after the closing quote, or -1.
let private scanString (src: byte[]) (i: int) =
    let mutable j = i + 1
    let mutable result = -2

    while result = -2 do
        if j >= src.Length then
            result <- -1
        elif src[j] = 34uy then
            if j + 1 < src.Length && src[j + 1] = 34uy then
                j <- j + 2
            else
                result <- j + 1
        else
            j <- j + 1

    result

/// Scans a comment whose opening `(*` is at `i`. Comments nest, and a `"` inside one opens a string that a `*)`
/// cannot end. Returns the offset after the closing `*)`, or -1.
let private scanComment (src: byte[]) (i: int) =
    let mutable j = i + 2
    let mutable depth = 1

    while depth > 0 && j >= 0 do
        if j >= src.Length then
            j <- -1
        else
            match src[j] with
            | 40uy when j + 1 < src.Length && src[j + 1] = 42uy ->
                depth <- depth + 1
                j <- j + 2
            | 42uy when j + 1 < src.Length && src[j + 1] = 41uy ->
                depth <- depth - 1
                j <- j + 2
            | 34uy -> j <- scanString src j
            | _ -> j <- j + 1

    j

let private isIdentStartAt (src: byte[]) (i: int) =
    i < src.Length
    && (isAsciiIdentStart src[i]
        || src[i] >= 0x80uy
           && (let struct (c, _) = unicodeClass src i in c = 1))

let private scanIdent (src: byte[]) (start: int) =
    let mutable j = start
    let mutable go = true

    while go && j < src.Length do
        let b = src[j]

        if isAsciiIdentPart b then
            j <- j + 1
        elif b >= 0x80uy then
            let struct (c, n) = unicodeClass src j

            if c <> 0 then j <- j + n else go <- false
        elif b = 46uy && isIdentStartAt src (j + 1) then
            j <- j + 1
        else
            go <- false

    j

let private scanNumber (src: byte[]) (start: int) =
    let mutable j = start

    while j < src.Length
          && (isDigit src[j]
              || isAsciiIdentStart src[j]
              || src[j] = 46uy
                 && j + 1 < src.Length
                 && isDigit src[j + 1]) do
        j <- j + 1

    j

let private scanSymbolRun (src: byte[]) (start: int) =
    let mutable j = start
    let mutable go = true

    while go && j < src.Length do
        let b = src[j]

        if
            isBlank b
            || b = 34uy
            || isSingleCharSymbol b
            || isAsciiIdentStart b
            || isDigit b
        then
            go <- false
        elif b = 46uy then
            let n = dotRun src j

            if blankOrEof src (j + n) && (n = 1 || n = 3) then
                go <- false
            else
                j <- j + n
        elif b >= 0x80uy then
            let struct (c, n) = unicodeClass src j

            if c = 1 then go <- false else j <- j + n
        else
            j <- j + 1

    j

/// Scans an `ident:{{ ... }}` body whose first `{` is at `i`. Rocq treats it as one token that may contain dots.
let private scanQuotation (src: byte[]) (i: int) =
    let mutable width = 0

    while i + width < src.Length && src[i + width] = 123uy do
        width <- width + 1

    let matches (at: int) (b: byte) =
        at + width <= src.Length
        && Seq.forall (fun k -> src[at + k] = b) (seq { 0 .. width - 1 })

    let mutable j = i + width
    let mutable depth = 1

    while depth > 0 && j >= 0 do
        if j >= src.Length then
            j <- -1
        elif matches j 123uy then
            depth <- depth + 1
            j <- j + width
        elif matches j 125uy then
            depth <- depth - 1
            j <- j + width
        else
            j <- j + 1

    j

/// Splits a source file into tokens that partition it.
let lex (src: byte[]) : Token[] * LexIssue list =
    let tokens = ResizeArray<Token>(src.Length / 4 + 1)
    let issues = ResizeArray<LexIssue>()
    let mutable i = 0
    // Rocq's `between_commands`: `-`, `+`, `*`, `{` and `}` are sentences of their own here.
    let mutable between = true
    // Tracks `n : {` and `[name] : {`, the selector forms ending a sentence at the brace.
    // 0 start, 1 after a name or number, 2 after `[`, 3 after `[name`, 4 after `]`, 5 after `:`, -1 no selector.
    let mutable selector = 0

    let emit kind stop =
        tokens.Add { Kind = kind; Start = i; End = stop }

        i <- stop

    let significant kind stop =
        match kind with
        | TokenKind.Terminator
        | TokenKind.Bullet
        | TokenKind.Focus ->
            between <- true
            selector <- 0
        | _ ->
            between <- false
            let isName = kind = TokenKind.Ident || kind = TokenKind.Number

            let isSymbol (s: string) =
                stop - i = s.Length
                && Encoding.ASCII.GetString (src, i, stop - i) = s

            selector <-
                match selector with
                | 0 when isName -> 1
                | 0 when isSymbol "[" -> 2
                | 2 when isName -> 3
                | 3 when isSymbol "]" -> 4
                | 1
                | 4 when isSymbol ":" -> 5
                | _ -> -1

        emit kind stop

    while i < src.Length do
        let b = src[i]

        if isBlank b then
            let mutable j = i + 1

            while j < src.Length && isBlank src[j] do
                j <- j + 1

            emit TokenKind.Whitespace j
        elif
            b = 40uy
            && i + 1 < src.Length
            && src[i + 1] = 42uy
        then
            match scanComment src i with
            | -1 ->
                issues.Add
                    {
                        Offset = i
                        Message = "unterminated comment"
                    }

                emit TokenKind.Comment src.Length
            | stop -> emit TokenKind.Comment stop
        elif b = 34uy then
            match scanString src i with
            | -1 ->
                issues.Add
                    {
                        Offset = i
                        Message = "unterminated string"
                    }

                significant TokenKind.String src.Length
            | stop -> significant TokenKind.String stop
        elif between && (b = 45uy || b = 43uy || b = 42uy) then
            let mutable j = i + 1

            while j < src.Length && src[j] = b do
                j <- j + 1

            significant TokenKind.Bullet j
        elif
            (b = 123uy || b = 125uy)
            && (between || b = 123uy && selector = 5)
        then
            significant TokenKind.Focus (i + 1)
        elif
            b = 46uy
            && blankOrEof src (i + dotRun src i)
            && (dotRun src i = 1 || dotRun src i = 3)
        then
            significant TokenKind.Terminator (i + dotRun src i)
        elif isSingleCharSymbol b then
            significant TokenKind.Symbol (i + 1)
        elif
            isAsciiIdentStart b
            || (b >= 0x80uy && isIdentStartAt src i)
        then
            let stop = scanIdent src i

            if
                stop + 2 < src.Length
                && src[stop] = 58uy
                && src[stop + 1] = 123uy
                && src[stop + 2] = 123uy
            then
                match scanQuotation src (stop + 1) with
                | -1 ->
                    issues.Add
                        {
                            Offset = i
                            Message = "unterminated quotation"
                        }

                    significant TokenKind.Quotation src.Length
                | close -> significant TokenKind.Quotation close
            else
                significant TokenKind.Ident stop
        elif isDigit b then
            significant TokenKind.Number (scanNumber src i)
        else
            let stop = scanSymbolRun src i
            significant TokenKind.Symbol (if stop > i then stop else i + 1)

    tokens.ToArray (), List.ofSeq issues

/// Splits tokens so that every offset in the sorted `boundaries` falls between two tokens. Comments stay whole.
let refine (tokens: Token[]) (boundaries: int[]) =
    let result = ResizeArray<Token>(tokens.Length + boundaries.Length / 4)
    let mutable b = 0

    for t in tokens do
        while b < boundaries.Length && boundaries[b] <= t.Start do
            b <- b + 1

        let mutable start = t.Start

        if t.Kind <> TokenKind.Comment then
            while b < boundaries.Length && boundaries[b] < t.End do
                result.Add
                    { t with
                        Start = start
                        End = boundaries[b]
                    }

                start <- boundaries[b]
                b <- b + 1

        result.Add { t with Start = start }

    result.ToArray ()
