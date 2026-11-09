/// <summary>The order of search results.</summary>
module Rocq.Navigator.Ranking

open Rocq.Navigator.Model

/// <summary>The most results the dialog shows.</summary>
[<Literal>]
let maxRows = 50

let private isSubsequence (needle: string) (text: string) =
    let mutable next = 0

    for character in text do
        if next < needle.Length && character = needle[next] then
            next <- next + 1

    next = needle.Length

/// The match tier of an entry, lower is better, or -1 for no match. The query is lower case.
let private tier (query: string) (entry: SearchEntry) =
    let name = entry.Name.ToLowerInvariant ()

    if name = query then 0
    elif name.StartsWith (query, System.StringComparison.Ordinal) then 1
    elif name.Contains query then 2
    elif isSubsequence query name then 3
    elif entry.Qualified.ToLowerInvariant().Contains query then 4
    else -1

/// <summary>Search entries matching the query, best first, with one entry per <c>Href</c>.</summary>
/// <remarks>The order is exact name, name prefix, name substring, name subsequence, qualified-name substring. Ties go to the shorter name, then to the entry's place in the input. An empty query matches nothing.</remarks>
let rank (entries: SearchEntry[]) (query: string) : SearchEntry[] =
    let query = query.Trim().ToLowerInvariant ()

    if query = "" then
        [||]
    else
        let seen = System.Collections.Generic.HashSet<string> ()

        entries
        |> Array.mapi (fun index entry -> tier query entry, entry.Name.Length, index, entry)
        |> Array.filter (fun (tier, _, _, entry) -> tier >= 0 && seen.Add entry.Href)
        |> Array.sortBy (fun (tier, length, index, _) -> tier, length, index)
        |> Array.map (fun (_, _, _, entry) -> entry)
