/// <summary>The data shared by the build and the client, with its JSON codecs.</summary>
module Rocq.Navigator.Model

open Ranvier.Wire
open Ranvier.Wire.Thoth.Json.Core

/// <summary>The two visual designs of a page.</summary>
type Look =
    | Family
    | Journal

/// <summary>A reader's colour mode. <c>System</c> follows the browser's preference.</summary>
type Mode =
    | System
    | Light
    | Dark

/// <summary>What <c>Print Assumptions</c> reported about a theorem whose proof is complete.</summary>
type Badge =
    | NoAxioms
    | Rests of string[]

/// <summary>What a hover card shows for a reference key.</summary>
type CardData =
    {
        /// The <c>data-ref</c> value on the page.
        Key: string
        Name: string
        Qualified: string
        Module: string
        /// <summary><c>None</c> for a name with no page or a local binder.</summary>
        Href: string option
        Signature: string
        /// Whether <c>Signature</c> is a type Rocq printed, rather than source text.
        SignatureIsType: bool
        DocHtml: string option
        Mentions: int
        Badge: Badge option
        External: bool
    }

/// <summary>A declaration in the search index.</summary>
type SearchEntry =
    {
        Name: string
        Qualified: string
        Module: string
        Kind: string
        Href: string
        Summary: string
    }

/// An empty array stands for NoAxioms.
let private badgeCodec: Codec<Badge> =
    Codec.map
        (fun names ->
            match names with
            | [||] -> NoAxioms
            | names -> Rests names)
        (fun badge ->
            match badge with
            | NoAxioms -> [||]
            | Rests names -> names)
        (Codec.array Codec.string)

let private cardCodec: Codec<CardData> =
    objectCodec {
        let! key = Codec.field "key" _.Key Codec.string
        and! name = Codec.field "name" _.Name Codec.string
        and! qualified = Codec.field "qualified" _.Qualified Codec.string
        and! moduleName = Codec.field "module" _.Module Codec.string
        and! href = Codec.optional "href" _.Href Codec.string
        and! signature = Codec.field "signature" _.Signature Codec.string
        and! signatureIsType = Codec.field "signatureIsType" _.SignatureIsType Codec.bool
        and! docHtml = Codec.optional "docHtml" _.DocHtml Codec.string
        and! mentions = Codec.field "mentions" _.Mentions Codec.int
        and! badge = Codec.optional "badge" _.Badge badgeCodec
        and! isExternal = Codec.field "external" _.External Codec.bool

        return
            {
                Key = key
                Name = name
                Qualified = qualified
                Module = moduleName
                Href = href
                Signature = signature
                SignatureIsType = signatureIsType
                DocHtml = docHtml
                Mentions = mentions
                Badge = badge
                External = isExternal
            }
    }

let private searchCodec: Codec<SearchEntry> =
    objectCodec {
        let! name = Codec.field "name" _.Name Codec.string
        and! qualified = Codec.field "qualified" _.Qualified Codec.string
        and! moduleName = Codec.field "module" _.Module Codec.string
        and! kind = Codec.field "kind" _.Kind Codec.string
        and! href = Codec.field "href" _.Href Codec.string
        and! summary = Codec.field "summary" _.Summary Codec.string

        return
            {
                Name = name
                Qualified = qualified
                Module = moduleName
                Kind = kind
                Href = href
                Summary = summary
            }
    }

/// <summary>The codec of a <c>symbols.json</c> file.</summary>
let cardsWire: WireCodec<CardData[]> = WireCodec.ofCodec (Codec.array cardCodec)

/// <summary>The codec of <c>search.json</c>.</summary>
let searchWire: WireCodec<SearchEntry[]> =
    WireCodec.ofCodec (Codec.array searchCodec)
