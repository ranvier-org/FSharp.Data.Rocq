namespace Nacara.Plugins

open System.IO
open System.Reflection
open System.Text.RegularExpressions
open Feliz.ViewEngine
open Nacara.Core
open Ranvier.View
open Rocq.Navigator

/// <summary>Options of the island spike.</summary>
type SpikeOptions =
    {
        /// The client bundle, or <c>None</c> for a site whose pages carry no script.
        Bundle: byte array option
        /// Whether the second page links to an anchor the first page lacks.
        BrokenLink: bool
    }

/// <summary>Two generated pages with two islands, proving the shape later tasks build on.</summary>
[<RequireQualifiedAccess>]
module Spike =

    /// <summary>The bytes of an asset embedded in this assembly.</summary>
    /// <param name="name">The file name under <c>assets/</c>.</param>
    /// <returns>The bytes, or an empty array for an asset missing from this build.</returns>
    let embedded (name: string) : byte array =
        use stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream($"Nacara.Plugin.Rocq.assets.%s{name}")

        match stream with
        | null -> [||]
        | stream ->
            use memory = new MemoryStream ()
            stream.CopyTo memory
            memory.ToArray ()

    let defaults =
        {
            Bundle =
                match embedded "navigator.js" with
                | [||] -> None
                | bytes -> Some bytes
            BrokenLink = false
        }

    /// <summary>Set <c>Bundle</c>.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let bundle value (options: SpikeOptions) =
        { options with Bundle = value }

    /// <summary>Set <c>BrokenLink</c>.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let brokenLink value (options: SpikeOptions) =
        { options with BrokenLink = value }

    let private scriptAsset = "assets/navigator.js"
    let private styleAsset = "assets/spike.css"

    let private serverOptions =
        { Server.Options.defaults with
            Policy = Omit
        }

    let private hostOf (name: string) =
        $"""<div %s{Islands.hostAttribute}="%s{name}"></div>"""

    /// `t.Unsafe.html` rejects root markers, so island markup enters the finished document.
    let private fillHost (name: string) (html: string) =
        let options =
            { serverOptions with
                IdPrefix = Islands.idPrefix name
            }

        let host = hostOf name
        let content = html.IndexOf host + host.Length - "</div>".Length
        html.Insert (content, Server.render options Islands.stub ())

    let private shell (title: string) =
        Component.define (fun _ (markup: UnsafeHtml) -> markup) (fun t ->
            t.fragment
                [|
                    t.Document.title title
                    t.element<obj> (Representation.HtmlTag "main") [| t.Unsafe.html<obj>(fun markup -> markup) |] [||]
                |])

    /// Plain head elements survive island hydration, which removes marked ones no entry adopted.
    let private unmarkHead (html: string) =
        let headEnd = html.IndexOf "</head>"

        Regex.Replace (html.Substring (0, headEnd), " data-ranvier-head=\"[^\"]*\"", "")
        + html.Substring headEnd

    let private layout (options: SpikeOptions) (context: PageContext<string>) =
        let article = $"""<article>%s{context.Content}</article>"""

        let document =
            { Server.DocumentShell.defaults with
                Lang = Some "en"
                Stylesheets = [ context.Site.UrlOfAsset styleAsset ]
                Scripts =
                    match options.Bundle with
                    | Some _ -> [ Server.ModuleScript.Src (context.Site.UrlOfAsset scriptAsset) ]
                    | None -> []
            }

        let markup = UnsafeHtml.Trust (hostOf "a" + hostOf "b" + article)

        let html =
            Server.renderDocument serverOptions document (shell context.FrontMatter) markup
            |> unmarkHead
            |> fillHost "a"
            |> fillHost "b"

        // Nacara writes the doctype around whatever the layout returns.
        Html.rawText (html.Substring "<!doctype html>".Length)

    let private page (title: string) (body: string) =
        GeneratedContent.create $"%s{title}.rocqpage" $"---\ntitle: %s{title}\n---\n%s{body}"

    let private collection (options: SpikeOptions) =
        Collection.create "proofs" (Decode.object (fun get -> get.Required.Field "title" Decode.string))
        |> Collection.title id
        |> Collection.route (fun info -> Route.file info.Locale $"proofs/%s{info.FrontMatter}.html")
        |> Collection.layout (layout options)
        |> Collection.producer "rocq-spike" (fun context ->
            let target = Url.ofPath context.Site.Url "proofs/Mod.A.html"
            let anchor = if options.BrokenLink then "gone" else "a1"

            [
                page "Mod.A" """<section id="a1">*a* # b</section>"""
                page "Mod.B" $"""<section id="b1"><a href="%s{target}#%s{anchor}">A</a></section>"""
            ])

    type private SpikePlugin(options: SpikeOptions) =
        interface IPlugin with
            member _.Name = "rocq"

            member _.Configure registry =
                let registry =
                    registry
                    |> Registry.frontMatter
                        {
                            Name = "rocqpage"
                            Extensions = [ ".rocqpage" ]
                            Opening = "---"
                            Closing = "---"
                            Wrapper = None
                        }
                    |> Registry.transform
                        {
                            Name = "rocqpage"
                            Extensions = [ ".rocqpage" ]
                            Transform = fun _ page -> { page with Html = page.Body }
                        }
                    |> Registry.asset (WriteText ("main { display: block; }\n", RelativePath.create styleAsset))

                match options.Bundle with
                | Some bytes ->
                    registry
                    |> Registry.asset (WriteBytes (bytes, RelativePath.create scriptAsset))
                | None ->
                    registry
                    |> Registry.onPagesRouted (fun context ->
                        context.Diagnostics.Add (
                            Diagnostic.warning "client-missing" "The site was built with no client bundle"
                            |> Diagnostic.withHint "Run navigator/build-client.sh and rebuild; the pages read without it"
                        ))

    /// <summary>Adds the plugin and its collection to a site.</summary>
    /// <param name="configure">Changes to the defaults.</param>
    /// <param name="site">The site being described.</param>
    let registerWith (configure: SpikeOptions -> SpikeOptions) (site: Site) =
        let options = configure defaults

        site
        |> Site.plugin (SpikePlugin options)
        |> Site.collection (collection options)

    /// <summary>Adds the plugin and its collection to a site, with the defaults.</summary>
    /// <param name="site">The site being described.</param>
    let register (site: Site) =
        registerWith id site
