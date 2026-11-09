module Rocq.Navigator.Tests.TokenTests

open System
open System.Globalization
open Expecto
open Rocq.Navigator
open Rocq.Navigator.Model

let private channel (hex: string) (start: int) =
    let value =
        float (Int32.Parse (hex.Substring (start, 2), NumberStyles.HexNumber))
        / 255.0

    if value <= 0.03928 then
        value / 12.92
    else
        Math.Pow ((value + 0.055) / 1.055, 2.4)

let private luminance (color: string) =
    0.2126 * channel color 1
    + 0.7152 * channel color 3
    + 0.0722 * channel color 5

let private contrast (first: string) (second: string) =
    let a, b = luminance first, luminance second
    (max a b + 0.05) / (min a b + 0.05)

let tests =
    testList
        "tokens"
        [
            testCase "all four combinations meet WCAG AA"
            <| fun () ->
                for look in [ Family; Journal ] do
                    for mode in [ Light; Dark ] do
                        let tokens = Tokens.``of`` look mode

                        for name, color in
                            [
                                "Ink", tokens.Ink
                                "Soft", tokens.Soft
                                "Dim", tokens.Dim
                                "Keyword", tokens.Keyword
                                "Binder", tokens.Binder
                                "Link", tokens.Link
                                "Pass", tokens.Pass
                                "Warn", tokens.Warn
                            ] do
                            for surface, background in [ "Bg", tokens.Bg; "Side", tokens.Side ] do
                                let ratio = contrast color background

                                Expect.isGreaterThanOrEqual ratio 4.5 $"{look} {mode}: {name} on {surface} is {ratio:F2}"

            testCase "the two looks' specified values match the design table"
            <| fun () ->
                let family = Tokens.``of`` Family Dark
                let journal = Tokens.``of`` Journal Light

                let row (tokens: Tokens) =
                    [
                        tokens.Bg
                        tokens.Side
                        tokens.Line
                        tokens.Ink
                        tokens.Soft
                        tokens.Selected
                        tokens.Keyword
                        tokens.Binder
                        tokens.Link
                        tokens.Pass
                        tokens.Radius
                        tokens.TitleSize
                        string tokens.TitleWeight
                    ]

                Expect.equal
                    (row family)
                    [
                        "#0f1419"
                        "#161c23"
                        "#2a3340"
                        "#e8ebf0"
                        "#c3c9d3"
                        "#21224a"
                        "#9b9ef0"
                        "#e5c07b"
                        "#9b9ef0"
                        "#7fd8a6"
                        "7px"
                        "19px"
                        "650"
                    ]
                    "Ranvier family, dark"

                Expect.equal family.Dim "#8d97a5" "family dim"
                Expect.stringContains family.Body "Inter" "family body"

                Expect.equal
                    (row journal)
                    [
                        "#fbf9f4"
                        "#f3efe6"
                        "#ddd6c8"
                        "#23201b"
                        "#3d3931"
                        "#e8e1d2"
                        "#7c2d3f"
                        "#4f6a3a"
                        "#27508f"
                        "#2f6b47"
                        "3px"
                        "22px"
                        "500"
                    ]
                    "Journal, light"

                Expect.stringContains journal.Body "serif" "journal body"

                let light = Tokens.``of`` Family Light
                Expect.equal (light.Bg, light.Side) ("#ffffff", "#f6f7f9") "family light base"

            testCase "the compiled stylesheet holds both looks and is one file"
            <| fun () ->
                let name, css = Styles.compiled ()
                Expect.equal name "navigator.css" "one file"
                Expect.stringContains css "data-look=\"family\"" "the family look"
                Expect.stringContains css "data-look=\"journal\"" "the journal look"
                Expect.stringContains css "prefers-color-scheme: dark" "the system mode"
                Expect.stringContains css "data-mode=\"dark\"" "a saved mode"
                Expect.stringContains css "@font-face" "the fonts"
                Expect.stringContains css "max-width: 900px" "the drawers"
                Expect.stringContains css "#fbf9f4" "a journal value"
                Expect.stringContains css "#0f1419" "a family value"
        ]
