/// <summary>Colours <c>rocq</c> and <c>coq</c> code blocks, using the tokens and keywords of the library's reading of a source file.</summary>
module Rocq.Navigator.Highlight

open System.Text
open FSharp.Data.Rocq.Lexer
open FSharp.Data.Rocq.Structure
open Nacara.Core

let private classOf (kind: TokenKind) =
    match kind with
    | TokenKind.Keyword -> Some "tok-keyword"
    | TokenKind.Comment -> Some "tok-comment"
    | TokenKind.String
    | TokenKind.Quotation -> Some "tok-string"
    | TokenKind.Number -> Some "tok-number"
    | TokenKind.Symbol -> Some "tok-operator"
    | TokenKind.Terminator
    | TokenKind.Bullet
    | TokenKind.Focus -> Some "tok-punctuation"
    | _ -> None

/// <summary>The lines of a snippet, each a list of coloured pieces. Joining the pieces gives back the snippet's lines.</summary>
/// <param name="code">Rocq source, whole or a fragment. An unterminated comment or string runs to the end.</param>
let lines (code: string) : Token list list =
    let bytes = Encoding.UTF8.GetBytes (code.Replace("\r\n", "\n").TrimEnd '\n')

    let finished = ResizeArray<Token list>()
    let current = ResizeArray<Token>()

    for token in (analyze "Snippet" bytes None).Tokens do
        let className = classOf token.Kind

        let parts =
            (Encoding.UTF8.GetString (bytes, token.Start, token.End - token.Start)).Split '\n'

        parts
        |> Array.iteri (fun index part ->
            if index > 0 then
                finished.Add (List.ofSeq current)
                current.Clear ()

            if part <> "" then
                current.Add { Text = part; ClassName = className })

    finished.Add (List.ofSeq current)
    List.ofSeq finished

/// <summary>A highlighter for fences naming <c>rocq</c> or <c>coq</c>.</summary>
type RocqHighlighter() =
    interface IHighlighter with
        member _.Name = "rocq"

        member _.Highlight(language, code) =
            match language with
            | Some "rocq"
            | Some "coq" -> Some (lines code)
            | _ -> None
