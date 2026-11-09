---
title: Reader settings
---

Readers change a page from **Settings**, at the bottom of the left pane, or at the end of the Modules drawer on narrow screens. The surface follows the viewport: a popover with a fine pointer, a side panel on large touch devices, and a dialog on phones.

Choices apply live, and persist per key in the browser's `localStorage`.

| Setting | Values | Default |
|---|---|---|
| Look | Family, Journal | Family |
| Mode | Auto, Light, Dark | Auto |
| Definitions | Open, Closed, Close long ones with a line count | The site's `collapseCodeOver` output |
| Proofs | Closed, Open | Closed |
| Imports | Closed, Open | Closed |
| References (above 900 px) | Pinned, Badges, Under definitions | Pinned |
| References open (under definitions) | Closed, Open | Closed |
| Arguments | Names only, With types, In the panel | Names only |

Reset clears every saved key. Below 900 px the references stay inline folds.

Try them on [the example site]({{base}}example/Tally.Laws.html).
