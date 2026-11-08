/// Digest checks, unknown lines and incremental extraction, on copies of the fixture.
module RocqData.Tests.RunTests

open System.IO
open System.Text
open System.Text.Json
open Expecto
open RocqData.Project
open RocqData.Source
open RocqData.Tests.Support

let private projectDir (project: string) =
    Path.GetDirectoryName project

let private filePair (project: string) (name: string) =
    let dir = Path.Combine (projectDir project, "theories")
    Path.Combine (dir, name + ".v"), Path.Combine (dir, name + ".glob")

/// Appends a comment to the source and rewrites the `.glob`'s digest, leaving its offsets valid.
let private touchConsistently (project: string) (name: string) =
    let v, g = filePair project name
    File.AppendAllText (v, "\n(* appended *)\n")
    let lines = File.ReadAllLines g
    lines[0] <- "DIGEST " + md5Hex (File.ReadAllBytes v)
    File.WriteAllText (g, String.concat "\n" lines + "\n")

let private snapshot (dir: string) =
    Directory.GetFiles (dir, "*", SearchOption.AllDirectories)
    |> Array.map (fun p -> Path.GetRelativePath(dir, p).Replace('\\', '/'), File.ReadAllBytes p)
    |> Array.sortBy fst

[<Tests>]
let tests =
    testList
        "run"
        [
            test "a stale .glob refuses the run and writes nothing" {
                let project = copyFixture ()
                let v, _ = filePair project "Base"
                File.AppendAllText (v, " ")
                let out = tempDir ()
                let report = run (options project out)
                Expect.equal report.ExitCode 2 "refused"
                Expect.equal report.Refused.Length 1 "one file"
                Expect.stringContains report.Refused[0] "stale" "says why"
                Expect.stringContains report.Refused[0] "theories/Base.v" "says which"
                Expect.isEmpty (Directory.GetFileSystemEntries out) "nothing written"
            }

            test "--allow-stale keeps lexical data and drops .glob facts" {
                let project = copyFixture ()
                let v, _ = filePair project "Base"
                File.AppendAllText (v, "\n(* new *)\n")
                let out = tempDir ()

                let report =
                    run
                        { options project out with
                            AllowStale = true
                        }

                Expect.equal report.ExitCode 0 "ran"
                let _, _, files = load project out

                let b =
                    files
                    |> Array.find (fun f -> f.Path.EndsWith "Base.v")

                Expect.equal (b.Outline.GetProperty("globStatus").GetString()) "stale" "flagged"
                Expect.equal ((b.Refs.GetProperty "refs").GetProperty("start").GetArrayLength()) 0 "no references"
                Expect.isGreaterThan ((b.Outline.GetProperty "sentences").GetProperty("start").GetArrayLength()) 0 "sentences stay"
            }

            test "DIGEST NO refuses the run" {
                let project = copyFixture ()
                let _, g = filePair project "Base"
                let lines = File.ReadAllLines g
                lines[0] <- "DIGEST NO"
                File.WriteAllLines (g, lines)
                let report = run (options project (tempDir ()))
                Expect.equal report.ExitCode 2 "refused"
            }

            test "a missing .glob refuses the run" {
                let project = copyFixture ()
                let _, g = filePair project "Base"
                File.Delete g
                let report = run (options project (tempDir ()))
                Expect.equal report.ExitCode 2 "refused"
                Expect.stringContains report.Refused[0] "compile" "says what to do"
            }

            test "an unknown kind is recorded, and fails only under --strict" {
                let project = copyFixture ()
                let _, g = filePair project "Base"
                File.AppendAllText (g, "zzz 0:3 <> mystery\n")
                let out = tempDir ()
                let report = run (options project out)
                Expect.equal report.ExitCode 0 "recorded"
                Expect.equal report.UnknownKinds (Map [ "zzz", 1 ]) "reported"
                let index, _, files = load project out
                Expect.equal (index.GetProperty("unknownKinds").GetProperty("zzz").GetInt32()) 1 "in the index"

                let b =
                    files
                    |> Array.find (fun f -> f.Path.EndsWith "Base.v")

                let kinds = strs (b.Outline.GetProperty "symbols") "kind"
                Expect.contains kinds "zzz" "the line's data stays"

                Expect.equal
                    (run
                        { options project (tempDir ()) with
                            Strict = true
                        })
                        .ExitCode
                    3
                    "strict fails"
            }

            test "a line of unknown shape is recorded and fails the run" {
                let project = copyFixture ()
                let _, g = filePair project "Base"
                File.AppendAllText (g, "an entirely new line form\n")
                let out = tempDir ()
                let report = run (options project out)
                Expect.equal report.ExitCode 3 "fails loudly"
                Expect.equal report.UnparsedLines 1 "counted"
                let _, _, files = load project out

                let b =
                    files
                    |> Array.find (fun f -> f.Path.EndsWith "Base.v")

                let unparsed = (b.Outline.GetProperty "globLines").GetProperty("unparsed")[0]
                Expect.equal (unparsed.GetProperty("text").GetString()) "an entirely new line form" "text kept"
            }

            test "an out-of-range .glob span is reported, and the data stays usable" {
                let project = copyFixture ()
                let _, g = filePair project "Base"
                File.AppendAllText (g, "def 999990:999995 <> offscreen\n")
                let out = tempDir ()
                Expect.equal (run (options project out)).ExitCode 0 "ran"
                let _, _, files = load project out

                let b =
                    files
                    |> Array.find (fun f -> f.Path.EndsWith "Base.v")

                let codes =
                    [
                        for d in (b.Outline.GetProperty "diagnostics").EnumerateArray() -> d.GetProperty("code").GetString()
                    ]

                Expect.contains codes "glob-span-invalid" "reported"
            }

            test "a second run reuses every file and changes no byte" {
                let project = copyFixture ()
                let out = tempDir ()
                let first = run (options project out)
                Expect.equal first.Reused.Length 0 "first run extracts all"
                let before = snapshot out
                let second = run (options project out)
                Expect.equal second.Extracted.Length 0 "nothing extracted"
                Expect.equal second.Reused.Length first.Extracted.Length "all reused"
                Expect.equal (snapshot out) before "same bytes"
            }

            test "changing one file re-extracts it alone, and matches a full run byte for byte" {
                let project = copyFixture ()
                let out = tempDir ()
                run (options project out) |> ignore
                touchConsistently project "Crlf"
                let incremental = run (options project out)
                Expect.equal incremental.Extracted [| "theories/Crlf.v" |] "one file"
                let fresh = tempDir ()
                run (options project fresh) |> ignore
                Expect.equal (snapshot out) (snapshot fresh) "same bytes as a full run"
            }

            test "a changed .glob with an unchanged source re-extracts" {
                let project = copyFixture ()
                let out = tempDir ()
                run (options project out) |> ignore
                let _, g = filePair project "Base"
                File.AppendAllText (g, "R0:3 Elsewhere <> changed def\n")
                let report = run (options project out)
                Expect.equal report.Extracted [| "theories/Base.v" |] "the .glob is part of the key"
            }

            test "a removed file's shards go away" {
                let project = copyFixture ()
                let out = tempDir ()
                run (options project out) |> ignore
                let text = File.ReadAllText project
                File.WriteAllText (project, text.Replace ("theories/Crlf.v", ""))
                run (options project out) |> ignore

                let names =
                    Directory.GetFiles (Path.Combine (out, "files"))
                    |> Array.map Path.GetFileName

                Expect.isFalse (names |> Array.exists (fun n -> n.Contains "Crlf")) "shards removed"
            }

            test "extraction needs no Rocq, and the output needs no sources" {
                let project = copyFixture ()
                let out = tempDir ()
                run (options project out) |> ignore
                Directory.Delete (projectDir project, true)

                let doc =
                    JsonDocument.Parse (File.ReadAllBytes (Path.Combine (out, "files", "Fixture.Base.tokens.json")))

                Expect.stringContains (doc.RootElement.GetProperty("source").GetString()) "base_id" "the tokens layer holds the text"
            }
        ]
