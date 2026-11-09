module Rocq.Navigator.Tests.HighlightTests

open Expecto
open Nacara.Core
open Rocq.Navigator

let private highlighter = Highlight.RocqHighlighter () :> IHighlighter

let private withClass (className: string) (code: string) =
    Highlight.lines code
    |> List.concat
    |> List.filter (fun piece -> piece.ClassName = Some className)
    |> List.map _.Text

let private joined (code: string) =
    Highlight.lines code
    |> List.map (fun line -> line |> List.map _.Text |> String.concat "")

let tests =
    testList
        "highlight"
        [
            testCase "answers for rocq and coq, and declines the rest"
            <| fun () ->
                let answers language =
                    (highlighter.Highlight (language, "Check 1.")).IsSome

                Expect.isTrue (answers (Some "rocq")) "rocq"
                Expect.isTrue (answers (Some "coq")) "coq"
                Expect.isFalse (answers (Some "fsharp")) "another language"
                Expect.isFalse (answers None) "no language"

            testCase "colours each kind of token"
            <| fun () ->
                let code = "Theorem t : forall n, n = 1. (* note *)\nDefinition s := \"text\"."

                Expect.equal (withClass "tok-keyword" code) [ "Theorem"; "forall"; "Definition" ] "keywords"
                Expect.equal (withClass "tok-comment" code) [ "(* note *)" ] "comments"
                Expect.equal (withClass "tok-string" code) [ "\"text\"" ] "strings"
                Expect.equal (withClass "tok-number" code) [ "1" ] "numbers"
                Expect.equal (withClass "tok-punctuation" code) [ "."; "." ] "sentence ends"
                Expect.contains (withClass "tok-operator" code) ":=" "symbols"

            testCase "joining the pieces gives back the lines"
            <| fun () ->
                let source =
                    "(** ∀ doc\n\n   more *)\nLemma é : ∀ x, x → x.\n\nProof.\n  - auto.\nQed."

                Expect.equal (joined source) (List.ofArray (source.Split '\n')) "text"

            testCase "colours every line of a comment that spans several"
            <| fun () -> Expect.equal (withClass "tok-comment" "(* one\n   two *)") [ "(* one"; "   two *)" ] "lines"

            testCase "reads carriage returns and a final newline as the fence does"
            <| fun () -> Expect.equal (joined "Check 1.\r\nCheck 2.\r\n") [ "Check 1."; "Check 2." ] "lines"

            testCase "accepts unterminated comments and strings"
            <| fun () ->
                Expect.equal (joined "(* open") [ "(* open" ] "comment"
                Expect.equal (joined "Check \"open") [ "Check \"open" ] "string"
        ]
