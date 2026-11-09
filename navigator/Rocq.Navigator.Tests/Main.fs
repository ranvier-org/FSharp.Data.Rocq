module Rocq.Navigator.Tests.Main

open Expecto

[<EntryPoint>]
let main args =
    runTestsWithCLIArgs
        []
        args
        (testList
            "navigator"
            [
                FactsTests.tests
                SourceTests.tests
                HighlightTests.tests
                DocsTests.tests
                ArticleTests.tests
                GuaranteesTests.tests
                PluginTests.tests
                TokenTests.tests
                RankingTests.tests
                CorpusTests.tests
            ])
