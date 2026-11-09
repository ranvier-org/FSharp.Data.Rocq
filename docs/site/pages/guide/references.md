---
title: References and dependencies
---

`.glob` files record what every name resolves to. The library joins them across the project, so you can ask both directions without reading Rocq's output yourself.

## Who uses a name

`whoUses` takes a name and returns each place that mentions it.

<!-- source: whoUses -->

For `total` in the example project:

<!-- output: whoUses -->

`usesOf` does the same for a `Node`, when a name alone is ambiguous. `symbolsNamed` lists the candidates.

## What a declaration uses

`usesBy` runs the other way, from an owner to its targets. A target outside the project is an `ExternalSymbolNode`, which records its module and name.

<!-- source: usesBy -->

For `total_append`:

<!-- output: usesBy -->

## Between files

`RocqProject.FileGraph` holds the `Require` edges and the reference counts between files.

<!-- source: fileGraph -->

<!-- output: fileGraph -->
