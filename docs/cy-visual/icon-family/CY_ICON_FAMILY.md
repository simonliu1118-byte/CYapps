# CY App Icon Family Reference

> Status: `REFERENCE / SHARED DESIGN SOURCE`
>
> Canonical upstream: `simonliu1118-byte/AITeam`
>
> Downstream consumers: `simonliu1118-byte/CYapps`, `simonliu1118-byte/CYapps_pvt`
>
> This is a design/reference document, not a fourth governance layer.

## 1. Purpose

The CY App Icon Family should make different CY desktop applications look visibly related while keeping each app easy to distinguish.

The family is unified by a shared **square-card geometry, visual mass, typography hierarchy, frame language, and complexity level**. It is **not** unified by forcing every app to reuse the same lower symbol.

The production workflow must favor repeatable master geometry over repeatedly generating each icon from scratch.

---

## 2. Current family direction

The approved direction is a **new square-family master**.

The original CYInvoice `INV` icon is the historical visual ancestor and remains an important reference, but future family members should use the new, more square and visually fuller master geometry.

If CYInvoice is later migrated to the new family, it should also use the same new square master rather than retaining a visibly narrower/taller silhouette beside newer icons.

### Core appearance

- square canvas;
- visually square rounded-card body;
- white interior;
- one dominant accent color per app;
- colored rounded-square outer frame;
- large identifier in the upper zone;
- one simple app-specific motif in the lower zone;
- flat, high-contrast, business-desktop appearance;
- no decorative 3D, glossy chrome, heavy shadow, or unnecessary illustration detail.

---

## 3. Family skeleton — CORE

The family shares the **skeleton**, not the exact artwork.

### 3.1 Outer frame

All family members should use closely matched:

- body proportion;
- frame thickness;
- corner radius language;
- white inner field;
- outer/inner clear-space balance;
- apparent overall size inside the square canvas.

One icon must not look significantly smaller, narrower, taller, heavier, or more edge-to-edge than the others when displayed side by side.

### 3.2 Upper identifier zone

The upper zone is the primary recognition area.

Default rule:

> **CY Icon Family uses a short uppercase abbreviation whenever that remains reasonably recognizable.**

Examples:

- `INV`
- `ACC`
- `ENV`
- `WTM`
- `CVT`
- `CAL`

The visible letter block should have comparable apparent width, height, weight, and vertical position across the family. Different letter shapes may receive small optical adjustments; identical font-size numbers are less important than comparable apparent size.

### 3.3 Readability exception

If a short abbreviation is materially worse for recognition, a more intuitive identifier may be used as an explicit exception.

Approved example:

- `Auto` for `CYERPAutoInput`

The exception changes **only the identifier text strategy**. It does not authorize a different frame, body proportion, live area, motif scale, or overall visual weight.

### 3.4 Lower motif zone

The lower zone contains **one simple app-specific motif**.

Important:

> The two horizontal lines are an `INV`-specific concise element, not a universal CY family element.

Examples of appropriate motif roles:

- `INV`: two invoice/detail lines;
- `ACC`: accounting/chart/money motif;
- `ENV`: envelope motif;
- `Auto`: automated input / document + direction motif;
- `WTM`: watermark / marked-document motif;
- `CVT`: conversion / two-document transform motif;
- `CAL`: calculator motif.

The motif must remain subordinate to the identifier. If a functional symbol needs too much detail to be recognizable, simplify the symbol rather than enlarge or overcrowd the icon.

---

## 4. Visual mass and geometry

### 4.1 Family principle

Canvas size does not define family consistency by itself.

Two icons can both use a `48×48` canvas and still look unrelated if one drawing occupies a much smaller or larger area.

Review must compare:

- actual visible body size;
- frame/body proportion;
- apparent letter-block size;
- motif-block size;
- top/bottom balance;
- optical centering.

### 4.2 New square master

The new family intentionally moves away from the slightly taller/narrower apparent body of the historical INV toward a **more square, fuller body**.

Exact master geometry should ultimately be frozen in an editable vector template. Until that vector master is completed, new concept work should match the accepted square-family concept board and be compared side by side with other accepted members.

### 4.3 Optical correction

Small optical corrections are allowed when necessary, for example:

- minor abbreviation scale adjustment;
- small tracking change;
- roughly 1–2 px equivalent positional correction at working-size reference;
- motif width adjustment;
- small-size stroke/pixel-snapping correction.

These corrections exist to make the family look more equal, not to let each app invent a new layout.

---

## 5. Color strategy

Each app may have its own accent color.

Use the same accent color consistently for:

- outer frame;
- identifier;
- lower motif.

The white inner field remains common.

The family should remain cohesive even when app colors differ. Color alone must not be responsible for the family resemblance.

Avoid gradients as a production default. Concept-generation tools may occasionally produce subtle tonal variation, but the final vector master should prefer a clean solid-color system unless a later approved revision explicitly changes that direction.

---

## 6. Current known family members

The current CY repositories contain the following apps relevant to this family:

| Repository | Project | Family identifier direction | Motif direction |
|---|---|---|---|
| CYapps | `CYInvoice` | `INV` | invoice/detail lines |
| CYapps | `CYAccounting` | `ACC` | accounting/chart/money |
| CYapps | `CYEnvelope` | `ENV` | envelope |
| CYapps | `CYERPAutoInput` | `Auto` — readability exception | automated input / document + direction |
| CYapps | `SMARTCOPIConverter` | `CVT` | conversion / transform |
| CYapps | `TriINVCalc` | `CAL` | calculator |
| CYapps_pvt | `CYWatermark` | `WTM` | watermark / marked document |

`DriveDownloader` is **not** part of the CY App Icon Family and must not be included merely because it is another tool discussed or developed in parallel.

`CYAccountingWeb` is a web project and does not automatically inherit the Windows desktop application-icon production requirements; use the family only when an app/site icon decision explicitly calls for it.

---

## 7. Small-size production

Formal Windows ICO production should inspect native layers at least at:

`16 / 24 / 32 / 48 / 64 / 128 / 256 px`

Do not rely on a single 256 px image mechanically downscaled without review.

Allowed small-size corrections include:

- stroke thickening;
- gap simplification;
- motif simplification;
- abbreviation size adjustment;
- pixel snapping;
- optical centering.

The perceived identity and family silhouette must remain stable across sizes.

---

## 8. Production model — MUST FOR FINAL ADOPTION

Generative image models are useful for concept exploration but are not reliable enough to guarantee identical geometry across independently generated icons.

Therefore the intended workflow is:

1. use generation/reference images to explore and approve the family direction;
2. freeze a **single editable square-family vector master**;
3. define shared frame, live area, identifier zone, motif zone, and key dimensions in that master;
4. derive future app icons from that master by changing identifier, accent color, and lower motif;
5. review each new icon beside existing accepted family members;
6. create native PNG/ICO sizes from the accepted master and apply small-size optical corrections where needed.

Do **not** treat “use the same prompt again” as the family consistency mechanism.

---

## 9. Acceptance checklist

A new icon should not be accepted until the reviewer can answer yes to the relevant points:

- Does the frame/body look the same family as the accepted square master?
- Is the apparent icon size comparable to the others?
- Is the identifier block neither conspicuously larger nor smaller?
- Is the lower motif simple and subordinate?
- Is the motif app-specific rather than copied from another app without reason?
- Is the icon visually centered?
- Does the accent system remain one-color + white?
- Does it remain readable at 48 px and at least one smaller size?
- If the identifier is not a short uppercase abbreviation, is the readability exception justified?
- Would the icon still look like the same family in monochrome silhouette/structure, without relying only on color?

---

## 10. Current accepted concept direction

The accepted concept board uses a more square, fuller icon body and demonstrates these members together:

- INV
- ACC
- ENV
- Auto
- WTM
- CVT
- CAL

The concept board is a **visual direction reference**, not a precision geometry source. Exact production geometry must eventually come from the editable vector family master.
