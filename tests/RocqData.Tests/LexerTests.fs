module RocqData.Tests.LexerTests

open System.Text
open Expecto
open RocqData.Lexer

let private lexText (s: string) =
    let bytes = Encoding.UTF8.GetBytes s
    let tokens, issues = lex bytes
    bytes, tokens, issues

let private kinds (s: string) =
    let _, tokens, _ = lexText s

    tokens
    |> Array.filter (fun t -> t.Kind <> TokenKind.Whitespace)
    |> Array.map _.Kind

let private texts (s: string) =
    let bytes, tokens, _ = lexText s

    tokens
    |> Array.filter (fun t -> t.Kind <> TokenKind.Whitespace)
    |> Array.map (fun t -> Encoding.UTF8.GetString (bytes, t.Start, t.End - t.Start))

let private terminators (s: string) =
    kinds s
    |> Array.filter (fun k -> k = TokenKind.Terminator)
    |> Array.length

[<Tests>]
let tests =
    testList
        "lexer"
        [
            test "a dot followed by a blank or the end of input ends a sentence" {
                Expect.equal (terminators "x. y.") 2 "dot-space and dot-eof"
                Expect.equal (terminators "x.\ny.\r\nz.\tw.") 4 "newline, CRLF and tab"
            }

            test "a dot inside a qualified name or a projection ends nothing" {
                Expect.equal (texts "Foo.bar.baz.") [| "Foo.bar.baz"; "." |] "qualified name, then terminator"
                Expect.equal (terminators "x.1 .") 1 "projection"
                Expect.equal (terminators "f.(g) .") 1 "field application"
            }

            test "two dots are a symbol and three dots end a sentence" {
                Expect.equal (terminators "[x; .. ; y] .") 1 "only the last dot"
                Expect.equal (terminators "idtac... next.") 2 "ellipsis then dot"
            }

            test "comments nest and hold strings that a closer cannot end" {
                let input = "(* a (* b *) \"*)\" c *)x."
                Expect.equal (kinds input) [| TokenKind.Comment; TokenKind.Ident; TokenKind.Terminator |] "one comment"
            }

            test "a doubled quote stays inside a string" {
                Expect.equal (kinds "\"a\"\"b. (* c\".") [| TokenKind.String; TokenKind.Terminator |] "one string"
            }

            test "bullets and braces are tokens of their own only between commands" {
                Expect.equal
                    (kinds "- + ** { } x - y.")
                    [|
                        TokenKind.Bullet
                        TokenKind.Bullet
                        TokenKind.Bullet
                        TokenKind.Focus
                        TokenKind.Focus
                        TokenKind.Ident
                        TokenKind.Symbol
                        TokenKind.Ident
                        TokenKind.Terminator
                    |]
                    "mid-sentence minus is a symbol"
            }

            test "a comment leaves the between-commands state alone" {
                Expect.equal (Array.item 3 (kinds "x. (* c *) - y.")) TokenKind.Bullet "bullet after comment"
            }

            test "selectors end a sentence at the brace" {
                Expect.equal (Array.item 2 (texts "1:{")) "{" "numeric"
                Expect.equal (Array.item 2 (kinds "1:{")) TokenKind.Focus "numeric focus"
                Expect.equal (Array.item 4 (kinds "[g]:{")) TokenKind.Focus "named focus"
                Expect.equal (Array.item 3 (kinds "a b:{ x |}")) TokenKind.Symbol "not a selector"
            }

            test "an ident-prefixed double brace is one quotation holding dots" {
                Expect.equal (kinds "foo:{{ a. b. }}.") [| TokenKind.Quotation; TokenKind.Terminator |] "gobbles dots"
            }

            test "non-ASCII letters join identifiers and non-ASCII symbols do not" {
                Expect.equal (texts "αβ₁ ∀ x∧y.") [| "αβ₁"; "∀"; "x"; "∧"; "y"; "." |] "letters against symbols"
            }

            test "offsets count bytes" {
                let bytes, tokens, _ = lexText "α."
                Expect.equal bytes.Length 3 "two bytes and one"
                Expect.equal (tokens[1].Start, tokens[1].End) (2, 3) "terminator offset"
            }

            test "an unterminated comment reports an issue" {
                let _, tokens, issues = lexText "x. (* y"
                Expect.equal issues.Length 1 "one issue"
                Expect.equal tokens[tokens.Length - 1].End 7 "still covers the file"
            }

            test "refine splits at every boundary and leaves comments whole" {
                let bytes, tokens, _ = lexText "abc (* d *) efg"
                let refined = refine tokens [| 1; 6; 13 |]

                let parts =
                    refined
                    |> Array.map (fun t -> Encoding.UTF8.GetString (bytes, t.Start, t.End - t.Start))

                Expect.equal parts [| "a"; "bc"; " "; "(* d *)"; " "; "e"; "fg" |] "split ident, whole comment"
            }

            test "tokens partition arbitrary input" {
                let pool = "abc 1.(*)\"{}[]-+:;\n\r\t'αβ∀₁.."
                let random = System.Random 20261109

                for _ in 1..2000 do
                    let input =
                        System.String (Array.init (random.Next 40) (fun _ -> pool[random.Next pool.Length]))

                    let bytes, tokens, _ = lexText input
                    let mutable at = 0

                    for t in tokens do
                        Expect.equal t.Start at $"contiguous on {input}"
                        Expect.isGreaterThan t.End t.Start $"non-empty on {input}"
                        at <- t.End

                    Expect.equal at bytes.Length $"covers {input}"
            }
        ]
