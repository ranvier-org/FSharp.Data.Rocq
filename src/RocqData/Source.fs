/// Byte-level helpers shared by the lexer and the glob reader. Every offset is a UTF-8 byte offset.
module RocqData.Source

open System
open System.Security.Cryptography
open System.Text

/// A half-open byte range: `Start` is included and `End` is excluded.
[<Struct>]
type Span = { Start: int; End: int }

let span s e =
    { Start = s; End = e }

/// Decodes a byte range. The range must fall on UTF-8 character boundaries.
let text (bytes: byte[]) (s: Span) =
    Encoding.UTF8.GetString (bytes, s.Start, s.End - s.Start)

/// Lower-case hexadecimal MD5, the digest form Rocq writes to the first `.glob` line.
let md5Hex (bytes: byte[]) =
    Convert.ToHexStringLower (MD5.HashData bytes)

let isValidUtf8 (bytes: byte[]) =
    try
        UTF8Encoding(false, true).GetCharCount bytes
        |> ignore

        true
    with :? DecoderFallbackException ->
        false

let isCharBoundary (bytes: byte[]) (offset: int) =
    offset = bytes.Length
    || offset >= 0
       && offset < bytes.Length
       && (bytes[offset] &&& 0xC0uy) <> 0x80uy

/// The byte offset of the first byte of each line. Handles `\n` and `\r\n`; a line ends after its `\n`.
let lineStarts (bytes: byte[]) =
    let starts = ResizeArray [ 0 ]

    for i in 0 .. bytes.Length - 1 do
        if bytes[i] = 10uy && i + 1 < bytes.Length then
            starts.Add (i + 1)

    starts.ToArray ()
