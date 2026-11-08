module RocqData.Tests.GlobTests

open System.Text
open Expecto
open RocqData.Glob

let private parseText (s: string) =
    parse (Encoding.UTF8.GetBytes s)

[<Tests>]
let tests =
    testList
        "glob"
        [
            test "the header holds the digest and the module" {
                let g = parseText "DIGEST abc\nFM.N\n"
                Expect.equal g.Digest (Some "abc") "digest"
                Expect.equal g.Module "M.N" "module"
                Expect.equal g.LineCount 2 "two lines"
            }

            test "DIGEST NO reads as no digest" { Expect.equal (parseText "DIGEST NO\nFM\n").Digest None "no digest" }

            test "end offsets become exclusive" {
                let g = parseText "DIGEST a\nFM\nprf 10:12 <> foo\nR3:3 M <> x:1 var\n"
                Expect.equal (g.Defs[0].Span.Start, g.Defs[0].Span.End) (10, 13) "definition"
                Expect.equal (g.Refs[0].Span.Start, g.Refs[0].Span.End) (3, 4) "reference"
            }

            test "a doubled space between fields parses" {
                let g = parseText "DIGEST a\nFM\nvar  606:606 Outer T\n"
                Expect.equal g.Defs[0].Kind "var" "kind"
                Expect.equal g.Defs[0].Name "T" "name"
                Expect.isEmpty g.Unparsed "nothing dropped"
            }

            test "an unknown kind of a known shape is parsed and recorded" {
                let g = parseText "DIGEST a\nFM\nzzz 1:2 <> foo\nR1:2 M <> foo yyy\n"
                Expect.equal g.Defs.Length 1 "definition kept"
                Expect.equal g.Refs.Length 1 "reference kept"
                Expect.equal g.UnknownKinds [| (2, "zzz"); (3, "yyy") |] "both reported"
            }

            test "a line of unknown shape is recorded with its text" {
                let g = parseText "DIGEST a\nFM\nsomething new here at last\nR1 M\n"
                Expect.equal g.Unparsed [| (2, "something new here at last"); (3, "R1 M") |] "both reported"
            }

            test "var-prefixed kinds are known" {
                let g = parseText "DIGEST a\nFM\nR1:2 M <> x vardef\n"
                Expect.isEmpty g.UnknownKinds "vardef"
            }

            test "a non-ASCII name survives" { Expect.equal (parseText "DIGEST a\nFM\ndef 1:6 <> αβγ\n").Defs[0].Name "αβγ" "name" }
        ]
