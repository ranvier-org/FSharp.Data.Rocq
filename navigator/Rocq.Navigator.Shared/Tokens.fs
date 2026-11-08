namespace Rocq.Navigator

open Rocq.Navigator.Model

/// <summary>The design values of a look in one mode, written to the page as CSS custom properties.</summary>
type Tokens =
    {
        Bg: string
        /// The side panes and code blocks.
        Side: string
        Line: string
        Ink: string
        Soft: string
        Dim: string
        Selected: string
        Keyword: string
        Binder: string
        Link: string
        /// A passing badge.
        Pass: string
        /// An axiom badge and the mark of an admitted proof.
        Warn: string
        Radius: string
        Body: string
        Title: string
        TitleSize: string
        TitleWeight: int
    }

/// <summary>The values of each look and mode. All text colours meet WCAG AA on <c>Bg</c> and <c>Side</c>.</summary>
[<RequireQualifiedAccess>]
module Tokens =

    let private inter = "Inter, \"JetBrains Mono\", system-ui, sans-serif"

    let private serif =
        "\"Source Serif 4\", Georgia, \"Iowan Old Style\", \"JetBrains Mono\", serif"

    let private familyDark =
        {
            Bg = "#0f1419"
            Side = "#161c23"
            Line = "#2a3340"
            Ink = "#e8ebf0"
            Soft = "#c3c9d3"
            Dim = "#8d97a5"
            Selected = "#21224a"
            Keyword = "#9b9ef0"
            Binder = "#e5c07b"
            Link = "#9b9ef0"
            Pass = "#7fd8a6"
            Warn = "#e5a84b"
            Radius = "7px"
            Body = inter
            Title = inter
            TitleSize = "19px"
            TitleWeight = 650
        }

    let private familyLight =
        { familyDark with
            Bg = "#ffffff"
            Side = "#f6f7f9"
            Line = "#dfe3e8"
            Ink = "#1c2129"
            Soft = "#3b4350"
            Dim = "#5f6875"
            Selected = "#e9e9fb"
            Keyword = "#5e61d3"
            Binder = "#8a5a00"
            Link = "#5e61d3"
            Pass = "#1f7a4d"
            Warn = "#9a5b00"
        }

    let private journalLight =
        {
            Bg = "#fbf9f4"
            Side = "#f3efe6"
            Line = "#ddd6c8"
            Ink = "#23201b"
            Soft = "#3d3931"
            Dim = "#726a5c"
            Selected = "#e8e1d2"
            Keyword = "#7c2d3f"
            Binder = "#4f6a3a"
            Link = "#27508f"
            Pass = "#2f6b47"
            Warn = "#8a5413"
            Radius = "3px"
            Body = serif
            Title = serif
            TitleSize = "22px"
            TitleWeight = 500
        }

    let private journalDark =
        { journalLight with
            Bg = "#1c1915"
            Side = "#25211b"
            Line = "#3b352b"
            Ink = "#ece5d8"
            Soft = "#d6cebf"
            Dim = "#a79d8c"
            Selected = "#3a3226"
            Keyword = "#e3a6b1"
            Binder = "#b4c996"
            Link = "#9fbce8"
            Pass = "#8fcda6"
            Warn = "#e3b062"
        }

    /// <summary>The tokens of a look in a mode.</summary>
    /// <param name="look">The look.</param>
    /// <param name="resolved">The mode in effect. <c>System</c> reads as <c>Light</c>.</param>
    let ``of`` (look: Look) (resolved: Mode) : Tokens =
        match look, resolved with
        | Family, Dark -> familyDark
        | Family, _ -> familyLight
        | Journal, Dark -> journalDark
        | Journal, _ -> journalLight
