---
title: Guarantees page
---

The index page of a Rocq site lists the project's titled theorems, sorted by what their proofs establish. [See it for the example]({{base}}example/index.html).

<!-- frame: index.html -->

## How a theorem gets there

A `Theorem` or `Corollary` appears when its attached `(**md` comment opens with a line wholly in bold. That line is its title. A `Lemma` never appears.

<!-- rocq: theories/Laws.v#total_append -->

## The groups

The first group that matches wins:

| Group | The theorem | In the example |
|---|---|---|
| Open | Ends `Admitted` | `size_append` |
| Falsified | Has a name ending in `falsifiedSuffix` | `totals_differ_falsified` |
| Checks | Has an attached comment holding `checkMarker` | `generated_sum` |
| Guarantees | Matches none of these | `total_append` |

Without a suffix or marker in `navigator.json`, that group stays empty.

## Assumption badges

With `facts.json`, each entry shows what its proof assumes. `size_stable` rests on the axiom `counts_are_small`, and its badge says so. The others show that they use no axioms. An Open or Falsified entry shows no verdict, since its proof establishes nothing about the claim.

<!-- rocq: theories/Laws.v#size_stable -->
