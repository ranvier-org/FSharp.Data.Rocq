module Rocq.Navigator.Tests.RankingTests

open Expecto
open Rocq.Navigator.Model
open Rocq.Navigator.Ranking

let private entry (name: string) =
    {
        Name = name
        Qualified = "M." + name
        Module = "M"
        Kind = "lemma"
        Href = $"M.html#{name}"
        Summary = ""
    }

let private names (entries: SearchEntry[]) =
    entries |> Array.map _.Name |> List.ofArray

let tests =
    testList
        "ranking"
        [
            testCase "exact name beats prefix, prefix beats substring, substring beats subsequence"
            <| fun () ->
                let entries =
                    [|
                        entry "xfoo_bar"
                        entry "fxoxo"
                        entry "foo_bar"
                        entry "xfoo"
                        entry "foo"
                    |]

                Expect.equal (names (rank entries "foo")) [ "foo"; "foo_bar"; "xfoo"; "xfoo_bar"; "fxoxo" ] "tiers in order, shorter first"

            testCase "the query is case-insensitive, and an empty query returns nothing"
            <| fun () ->
                let entries = [| entry "Foo"; entry "bar" |]
                Expect.equal (names (rank entries "fOO")) [ "Foo" ] "case"
                Expect.isEmpty (rank entries "") "empty"
                Expect.isEmpty (rank entries "   ") "blank"

            testCase "a qualified name matches after every name tier"
            <| fun () ->
                let entries =
                    [|
                        { entry "zzz" with
                            Qualified = "Guide.helper"
                        }
                        entry "helper"
                    |]

                Expect.equal (names (rank entries "guide.he")) [ "zzz" ] "qualified"
                Expect.equal (names (rank entries "helper")) [ "helper"; "zzz" ] "name first"

            testCase "an entry repeated by Href appears once, the first kept"
            <| fun () ->
                let a = entry "foo"
                let entries = [| a; { a with Summary = "again" }; entry "foo2" |]
                let ranked = rank entries "foo"
                Expect.equal ranked.Length 2 "two"
                Expect.equal ranked[0].Summary "" "first kept"

            testCase "entries sharing an Href keep the best-ranked one"
            <| fun () ->
                let parent = entry "rtree"

                let constructorEntry =
                    { entry "r" with
                        Href = parent.Href
                    }

                Expect.equal (names (rank [| parent; constructorEntry |] "r")) [ "r" ] "the exact name wins"

            testCase "a name with markup characters ranks as plain text"
            <| fun () -> Expect.equal (names (rank [| entry "a<b>" |] "<b")) [ "a<b>" ] "literal"
        ]
