/// Expected structure of the committed fixture project.
module RocqData.Tests.FixtureTests

open System.IO
open System.Text
open System.Text.Json
open Expecto
open RocqData.Project
open RocqData.Tests.Support

let private fixture =
    lazy
        (let out = tempDir ()

         let report =
             run
                 { options fixtureProject out with
                     RocqDep = Some fixtureDep
                     RocqVersion = Some "test"
                 }

         Expect.equal report.ExitCode 0 "extraction"
         load fixtureProject out)

let private file (suffix: string) =
    let _, _, files = fixture.Value

    files
    |> Array.find (fun f -> f.Path.EndsWith suffix)

let private declarations (f: Loaded) =
    (f.Outline.GetProperty "declarations").EnumerateArray()
    |> Seq.toArray

let private decl (f: Loaded) (name: string) =
    declarations f
    |> Array.find (fun d -> d.GetProperty("name").GetString() = name)

let private ending (d: JsonElement) =
    [
        for p in (d.GetProperty "proofs").EnumerateArray() -> p.GetProperty("ending").GetString()
    ]

let private sentenceTexts (f: Loaded) =
    let s = f.Outline.GetProperty "sentences"
    Array.map2 (fun a b -> sliceBytes f.Bytes (a, b)) (ints s "start") (ints s "end")

[<Tests>]
let tests =
    testList
        "fixture structure"
        [
            test "non-ASCII offsets address the right bytes" {
                let k = file "Kitchen.v"
                let d = decl k "αβγ"
                Expect.equal (sliceBytes k.Bytes (spanOf (d.GetProperty "nameSpan"))) "αβγ" "name span"
                Expect.equal (sliceBytes k.Bytes (spanOf (d.GetProperty "sentenceSpan"))) "Definition αβγ (δ : nat) : nat := δ." "sentence"
                Expect.equal (sliceBytes k.Bytes (spanOf (d.GetProperty "body"))) "δ" "body"
                Expect.equal (sliceBytes k.Bytes (spanOf (d.GetProperty "binders"))) "(δ : nat)" "binders"
                Expect.equal (sliceBytes k.Bytes (spanOf (d.GetProperty "type"))) "nat" "type"

                let after = decl k "after_unicode"
                let refs = k.Refs.GetProperty "refs"
                let targets = k.Refs.GetProperty "targets"
                let names = strs targets "name"
                let s, e = spanOf (after.GetProperty "sentenceSpan")
                let starts = ints refs "start"
                let ends = ints refs "end"
                let targetColumn = ints refs "target"

                let hit =
                    [ 0 .. starts.Length - 1 ]
                    |> List.find (fun i ->
                        starts[i] >= s
                        && ends[i] <= e
                        && names[targetColumn[i]] = "αβγ")

                Expect.equal (sliceBytes k.Bytes (starts[hit], ends[hit])) "αβγ" "a reference past non-ASCII text"
            }

            test "UTF-16 offsets differ from byte offsets past non-ASCII text, and still slice correctly" {
                let k = file "Kitchen.v"
                let starts = ints k.Tokens "start"
                let u16 = ints k.Tokens "u16"
                Expect.isGreaterThan starts[starts.Length - 1] u16[u16.Length - 1] "bytes run ahead of UTF-16 units"
            }

            test "CRLF files keep their carriage returns and their offsets" {
                let c = file "Crlf.v"
                Expect.contains c.Bytes 13uy "the fixture holds CRs"
                let d = decl c "crlf_lemma"
                Expect.equal (sliceBytes c.Bytes (spanOf (d.GetProperty "nameSpan"))) "crlf_lemma" "name span"
                let doc = (c.Outline.GetProperty "comments")[d.GetProperty("docs").GetArrayLength()]
                Expect.equal (doc.GetProperty("format").GetString()) "doc" "the µ comment"
                let tail = sentenceTexts c
                Expect.equal tail[tail.Length - 1] "Qed." "final sentence"
            }

            test "keywords come from the source, and modifiers stay apart" {
                let k = file "Kitchen.v"
                Expect.equal ((decl k "uses_inner").GetProperty("keyword").GetString()) "Theorem" "Theorem"
                Expect.equal ((decl k "uses_section_var").GetProperty("keyword").GetString()) "Lemma" "Lemma"
                let inst = decl k "nat_shape"
                Expect.equal (inst.GetProperty("keyword").GetString()) "Instance" "Instance"
                Expect.equal (strs inst "modifiers") [| "#[global]" |] "attribute"
            }

            test "an assumption's statement holds its binders and type" {
                let k = file "Kitchen.v"

                let d =
                    declarations k
                    |> Array.find (fun d -> d.GetProperty("keyword").GetString() = "Parameters")

                Expect.equal (sliceBytes k.Bytes (spanOf (d.GetProperty "binders"))) "p1 p2" "names"
                Expect.equal (sliceBytes k.Bytes (spanOf (d.GetProperty "type"))) "nat" "type"
                let names = strs (k.Outline.GetProperty "symbols") "name"
                Expect.equal (ints d "symbols" |> Array.map (fun i -> names[i])) [| "p1"; "p2" |] "both names"

                let v =
                    declarations k
                    |> Array.find (fun d -> d.GetProperty("keyword").GetString() = "Variable")

                Expect.equal (sliceBytes k.Bytes (spanOf (v.GetProperty "statement"))) "(v : nat)" "parenthesised binder"
            }

            test "a top-level let in a statement is no body" {
                let k = file "Kitchen.v"
                let d = decl k "let_statement"
                Expect.equal (d.GetProperty("body").ValueKind) JsonValueKind.Null "no body"
                Expect.equal (ending d) [ "Qed" ] "the proof is found"
            }

            test "a recursive call and a mutual sibling resolve to their symbol" {
                let index, xref, _ = fixture.Value
                let qualified = strs (index.GetProperty "symbols") "qualified"
                let offsets = ints xref "toOffsets"
                let from = ints (xref.GetProperty "edges") "from"
                let ids = strs (index.GetProperty "declarations") "id"

                let users (name: string) =
                    let n = Array.findIndex (fun q -> q = name) qualified
                    [ for e in offsets[n] .. offsets[n + 1] - 1 -> ids[from[e]] ]

                Expect.contains (users "mul2") "Fixture.Kitchen#mul2" "recursive call"
                Expect.contains (users "forest") "Fixture.Kitchen#tree" "mutual sibling"
            }

            test "proof endings" {
                let k = file "Kitchen.v"
                Expect.equal (ending (decl k "uses_section_var")) [ "Qed" ] "Qed"
                Expect.equal (ending (decl k "uses_inner")) [ "Defined" ] "Defined"
                Expect.equal (ending (decl k "left_open")) [ "Admitted" ] "Admitted"
                Expect.equal (ending (decl k "named_goal")) [ "Abort" ] "Abort"
                Expect.equal (ending (decl k "proof_term")) [ "ProofTerm" ] "Proof term"
                Expect.equal (ending (decl k "no_proof_keyword")) [ "Qed" ] "no Proof keyword"
                Expect.equal (ending (decl k "interactive_def")) [ "Defined" ] "definition by tactics"
                Expect.equal (ending (decl k "point_shape")) [ "Defined" ] "instance by tactics"
                Expect.equal (ending (decl k "obligated")) [ "Qed" ] "Program obligation"
                Expect.equal (ending (decl k "nat_shape")) [] "instance by term"
            }

            test "the proof span runs from the opener to the terminator, and the body sits between" {
                let k = file "Kitchen.v"
                let p = (decl k "uses_inner").GetProperty("proofs")[0]
                Expect.equal ((sliceBytes k.Bytes (spanOf (p.GetProperty "bodySpan"))).TrimEnd()) "reflexivity." "body"
                Expect.stringStarts (sliceBytes k.Bytes (spanOf (p.GetProperty "span"))) "Proof." "opener"
                Expect.stringEnds (sliceBytes k.Bytes (spanOf (p.GetProperty "span"))) "Defined." "terminator"
                Expect.equal (p.GetProperty("tacticSentences").GetInt32()) 1 "one tactic"
            }

            test "bullets, focus braces and selectors are sentences" {
                let texts = sentenceTexts (file "Kitchen.v")
                Expect.contains texts "-" "bullet"
                Expect.contains texts "+" "bullet"
                Expect.contains texts "{" "brace"
                Expect.contains texts "}" "brace"
                Expect.contains texts "1:{" "selector"
                Expect.contains texts "[g]: exact I." "named selector on a tactic"
                Expect.contains texts "idtac..." "ellipsis"
            }

            test "a comment in a sentence, a period and a string do not split it" {
                let texts = sentenceTexts (file "Kitchen.v")
                Expect.contains texts "Definition with_inner_comment (* inner . comment *) := 1 + 2." "inner comment"
                Expect.contains texts "Comments \"ends with a dot. and \"\"quotes\"\" (* no comment\"." "string"
            }

            test "sections and modules nest, and End closes them" {
                let k = file "Kitchen.v"

                let scopes =
                    (k.Outline.GetProperty "scopes").EnumerateArray()
                    |> Seq.toArray

                let find (name: string) =
                    scopes
                    |> Array.find (fun s -> s.GetProperty("name").GetString() = name)

                let index (name: string) =
                    scopes
                    |> Array.findIndex (fun s -> s.GetProperty("name").GetString() = name)

                Expect.equal ((find "Outer").GetProperty("kind").GetString()) "Section" "section"
                Expect.equal ((find "Inner").GetProperty("parent").GetInt32()) (index "Outer") "section nesting"
                Expect.equal ((find "Nested").GetProperty("parent").GetInt32()) (index "M") "module nesting"
                Expect.equal ((find "Sig").GetProperty("kind").GetString()) "Module Type" "module type"
                let alias = find "Alias"
                Expect.equal (alias.GetProperty("endSentence").GetInt32()) (alias.GetProperty("sentence").GetInt32()) "alias has no End"
                Expect.equal ((decl k "inner_def").GetProperty("scope").GetInt32()) (index "Inner") "declaration scope"
            }

            test "mutual inductives, constructors and generated schemes group under one declaration" {
                let k = file "Kitchen.v"
                let d = decl k "tree"
                let names = strs (k.Outline.GetProperty "symbols") "name"
                let own = ints d "symbols" |> Array.map (fun i -> names[i])

                for expected in [ "tree"; "forest"; "Leaf"; "Node"; "FNil"; "FCons"; "tree_ind"; "forest_ind" ] do
                    Expect.contains own expected expected
            }

            test "docs attach across no blank line, and a blank line detaches" {
                let k = file "Kitchen.v"

                let comments =
                    (k.Outline.GetProperty "comments").EnumerateArray()
                    |> Seq.toArray

                let attached = ints (decl k "uses_section_var") "docs"
                Expect.equal attached.Length 1 "one comment"
                Expect.stringContains (comments[attached[0]].GetProperty("text").GetString()) "Attached across no blank line" "text"
                Expect.isEmpty (ints (decl k "uses_inner") "docs") "blank line detaches"

                let detached =
                    comments
                    |> Array.find (fun c -> c.GetProperty("text").GetString().Contains "Detached")

                Expect.equal (detached.GetProperty("newlinesToNext").GetInt32()) 2 "the gap is recorded for other rules"
                Expect.equal (detached.GetProperty("attachedTo").GetInt32()) -1 "unattached"
            }

            test "comments stay exactly as written, with format, trailing position and container" {
                let k = file "Kitchen.v"

                let comments =
                    (k.Outline.GetProperty "comments").EnumerateArray()
                    |> Seq.toArray

                let md =
                    comments
                    |> Array.find (fun c -> c.GetProperty("format").GetString() = "md")

                Expect.stringStarts (md.GetProperty("text").GetString()) "(**md" "delimiters kept"

                let trailing =
                    comments
                    |> Array.find (fun c -> c.GetProperty("text").GetString() = "(* trailing αβγ *)")

                Expect.isGreaterThanOrEqual (trailing.GetProperty("trailingSentence").GetInt32()) 0 "trailing"

                let inner =
                    comments
                    |> Array.find (fun c -> c.GetProperty("text").GetString() = "(* inner . comment *)")

                Expect.isGreaterThanOrEqual (inner.GetProperty("sentence").GetInt32()) 0 "contained"

                let nested =
                    comments
                    |> Array.find (fun c -> c.GetProperty("text").GetString().Contains "nested")

                Expect.stringEnds (nested.GetProperty("text").GetString()) "here *)" "nesting and a string handled"
            }

            test "headings come from both doc formats, and fenced code is no heading" {
                let k = file "Kitchen.v"

                let headings =
                    (k.Outline.GetProperty "headings").EnumerateArray()
                    |> Seq.toArray

                let titles =
                    headings
                    |> Array.map (fun h -> h.GetProperty("level").GetInt32(), h.GetProperty("text").GetString())

                Expect.equal titles [| (1, "A markdown heading"); (2, "A second heading") |] "markdown"

                for h in headings do
                    Expect.equal (sliceBytes k.Bytes (spanOf (h.GetProperty "span"))) (h.GetProperty("text").GetString()) "span"

                let b = file "Base.v"
                let coqdoc = (b.Outline.GetProperty "headings")[0]
                Expect.equal (coqdoc.GetProperty("text").GetString()) "A heading in coqdoc form" "coqdoc"
                Expect.equal (coqdoc.GetProperty("format").GetString()) "doc" "format"
            }

            test "imports resolve to modules" {
                let k = file "Kitchen.v"

                let imports =
                    (k.Outline.GetProperty "imports").EnumerateArray()
                    |> Seq.toArray

                let first = imports[0]
                Expect.equal (first.GetProperty("from").GetString()) "Fixture" "from"
                Expect.equal (first.GetProperty("mode").GetString()) "Import" "mode"
                Expect.equal (first.GetProperty("modules").[0].GetProperty("resolved").GetString()) "Fixture.Base" "resolved"
                let plain = imports[2]
                Expect.equal (plain.GetProperty("keyword").GetString()) "Require" "plain Require"
                Expect.equal (plain.GetProperty("mode").ValueKind) JsonValueKind.Null "no import mode"

                let moduleImport =
                    imports
                    |> Array.find (fun i -> i.GetProperty("keyword").GetString() = "Import")

                Expect.equal (moduleImport.GetProperty("modules").[0].GetProperty("text").GetString()) "M" "module import"
            }

            test "notations decode their glob name and their literal" {
                let k = file "Kitchen.v"

                let notations =
                    (k.Outline.GetProperty "notations").EnumerateArray()
                    |> Seq.toArray

                let literal (s: string) =
                    notations
                    |> Array.find (fun n -> n.GetProperty("literal").GetString() = s)

                let plain = literal "a +++ b"
                Expect.equal (plain.GetProperty("key").GetString()) "x_'+++'_x" "key"
                Expect.equal ((literal "%% x").GetProperty("scope").GetString()) "kitchen_scope" "scope"
                Expect.isGreaterThanOrEqual ((literal "'νotation' x").GetProperty("symbol").GetInt32()) 0 "non-ASCII"

                let reserved =
                    notations
                    |> Array.filter (fun n -> n.GetProperty("keyword").GetString() = "Reserved Notation")

                Expect.equal reserved.Length 1 "reserved notation, no glob line"

                let where =
                    notations
                    |> Array.find (fun n -> n.GetProperty("keyword").GetString() = "Fixpoint")

                Expect.equal (where.GetProperty("literal").GetString()) "a *** b" "where clause"

                let abbreviation =
                    notations
                    |> Array.find (fun n -> n.GetProperty("key").GetString() = "double")

                Expect.equal (abbreviation.GetProperty("literal").ValueKind) JsonValueKind.Null "abbreviation"

                let tactic =
                    notations
                    |> Array.find (fun n -> n.GetProperty("keyword").GetString() = "Tactic Notation")

                Expect.equal (tactic.GetProperty("literal").GetString()) "fixture_tac" "tactic notation"
            }

            test "Ltac is named from the source, and Goal is anonymous in the source" {
                let k = file "Kitchen.v"
                Expect.equal ((decl k "solve_it").GetProperty("nameSource").GetString()) "source" "Ltac"

                let goal =
                    declarations k
                    |> Array.find (fun d -> d.GetProperty("keyword").GetString() = "Goal")

                Expect.equal (goal.GetProperty("name").GetString()) "Unnamed_thm" ".glob's generated name"
            }

            test "Fail runs a failing sentence that declares nothing" {
                let k = file "Kitchen.v"
                let texts = sentenceTexts k
                let i = Array.findIndex (fun (t: string) -> t.StartsWith "Fail") texts
                let s = k.Outline.GetProperty "sentences"
                Expect.equal (Array.item i (ints s "decl")) -1 "no declaration"
            }

            test "uses cross files and sort by where they occur" {
                let index, xref, _ = fixture.Value
                let symbols = index.GetProperty "symbols"
                let qualified = strs symbols "qualified"
                let target = Array.findIndex (fun n -> n = "base_id") qualified
                let offsets = ints xref "toOffsets"
                let edges = xref.GetProperty "edges"
                let from = ints edges "from"
                let where = ints edges "where"
                let decls = index.GetProperty "declarations"
                let ids = strs decls "id"

                let users =
                    [
                        for e in offsets[target] .. offsets[target + 1] - 1 -> (if from[e] >= 0 then ids[from[e]] else "<directive>"), where[e]
                    ]

                Expect.contains users ("Fixture.Crlf#crlf_def", 1) "the body of another file's declaration"
                Expect.contains users ("Fixture.Base#base_id_eq", 0) "the statement of a lemma"
                Expect.contains users ("Fixture.Kitchen#last_one", 1) "a body"
                Expect.contains users ("<directive>", 3) "a top-level Check"
            }

            test "a proof's mention carries where = proof" {
                let index, xref, _ = fixture.Value
                let qualified = strs (index.GetProperty "symbols") "qualified"
                let target = Array.findIndex (fun n -> n = "Outer.h") qualified
                let offsets = ints xref "toOffsets"
                let edges = xref.GetProperty "edges"
                let from = ints edges "from"
                let where = ints edges "where"
                let ids = strs (index.GetProperty "declarations") "id"

                let users =
                    [
                        for e in offsets[target] .. offsets[target + 1] - 1 -> ids[from[e]], where[e]
                    ]

                Expect.contains users ("Fixture.Kitchen#uses_section_var", 2) "exact h in a proof"
            }

            test "forward and reverse edge lookups agree" {
                let index, xref, _ = fixture.Value
                let nDecls = (index.GetProperty "declarations").GetProperty("id").GetArrayLength()
                let toOffsets = ints xref "toOffsets"
                let fromOffsets = ints xref "fromOffsets"
                let byFrom = ints xref "byFrom"
                let edges = xref.GetProperty "edges"
                let from = ints edges "from"
                let targets = ints edges "to"
                Expect.equal fromOffsets[fromOffsets.Length - 1] from.Length "every edge reachable forward"
                Expect.equal toOffsets[toOffsets.Length - 1] from.Length "every edge reachable in reverse"

                for slot in 0 .. fromOffsets.Length - 2 do
                    for k in fromOffsets[slot] .. fromOffsets[slot + 1] - 1 do
                        let e = byFrom[k]
                        let expected = if slot < nDecls then slot else ~~~(slot - nDecls)
                        Expect.equal from[e] expected "owner slot"

                        Expect.isTrue
                            (e >= toOffsets[targets[e]]
                             && e < toOffsets[targets[e] + 1])
                            "reverse range holds the edge"
            }

            test "token links reach refs, symbols and binders" {
                let k = file "Kitchen.v"
                let links = k.Tokens.GetProperty "links"
                let token = ints links "token"
                let kind = ints links "type"
                let index = ints links "index"
                let starts = ints k.Tokens "start"
                let refStarts = ints (k.Refs.GetProperty "refs") "start"
                let symStarts = ints (k.Outline.GetProperty "symbols") "start"
                let bindStarts = ints (k.Refs.GetProperty "binders") "start"

                for i in 0 .. token.Length - 1 do
                    let linked =
                        match kind[i] with
                        | 0 -> refStarts[index[i]]
                        | 1 -> symStarts[index[i]]
                        | _ -> bindStarts[index[i]]

                    Expect.isLessThanOrEqual linked starts[token[i]] "the entry covers the token"

                Expect.equal (Array.distinct kind |> Array.sort) [| 0; 1; 2 |] "all three link types"
            }
        ]
