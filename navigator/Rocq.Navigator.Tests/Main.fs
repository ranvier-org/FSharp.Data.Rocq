module Rocq.Navigator.Tests.Main

open Expecto

[<EntryPoint>]
let main args =
    runTestsWithCLIArgs [] args (testList "navigator" [ IslandTests.tests; DocsTests.tests ])
