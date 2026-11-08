namespace Rocq.Navigator

open System
open System.IO
open System.Text.Json

/// <summary>Project settings read from <c>navigator.json</c>. All fields are optional in the file.</summary>
type Config =
    {
        /// The name suffix marking a theorem that disproves a claim.
        FalsifiedSuffix: string option
        /// The text marking a comment as a record of property checking.
        CheckMarker: string option
        /// The modules imported before capture, which select the notations Rocq prints.
        CaptureImports: string[]
    }

/// <summary>Reads <c>navigator.json</c>.</summary>
module Config =
    /// <summary>The configuration with every setting absent.</summary>
    let none =
        {
            FalsifiedSuffix = None
            CheckMarker = None
            CaptureImports = [||]
        }

    /// <summary>Reads the file at <c>path</c>. Returns <c>Error</c> naming the path when it is missing or malformed.</summary>
    let read (path: string) : Result<Config, string> =
        try
            use document = JsonDocument.Parse (File.ReadAllBytes path)
            let root = document.RootElement

            let text (name: string) =
                match root.TryGetProperty name with
                | true, value when value.ValueKind <> JsonValueKind.Null -> Option.ofObj (value.GetString ())
                | _ -> None

            Ok
                {
                    FalsifiedSuffix = text "falsifiedSuffix"
                    CheckMarker = text "checkMarker"
                    CaptureImports =
                        match root.TryGetProperty "captureImports" with
                        | true, value ->
                            [|
                                for item in value.EnumerateArray () do
                                    item.GetString ()
                            |]
                        | _ -> [||]
                }
        with
        | :? IOException
        | :? UnauthorizedAccessException
        | :? JsonException
        | :? InvalidOperationException as failure -> Result.Error $"{path}: {failure.Message}"
