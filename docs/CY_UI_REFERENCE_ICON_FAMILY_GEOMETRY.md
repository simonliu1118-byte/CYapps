# CYInvoice Legacy Icon Geometry — Historical Reference

> Status: `HISTORICAL / SOURCE MEASUREMENT ONLY`  
> Source: existing `apps/CYInvoice/assets/CYInvoice.ico`  
> **This document is not the future CY Icon Family geometry standard.**

## 1. Why this file still exists

The original CYInvoice `INV` icon was the visual ancestor used while the CY icon family was being explored. Its geometry was measured so later comparisons could distinguish real visual-size differences from canvas-size differences.

That measurement is still useful as historical evidence, but the design direction has changed:

> **Future CY family icons should use the newer, more square and visually fuller shared family master.**

The older INV silhouette is therefore no longer a geometry target for ACC / ENV / Auto / WTM / CVT / CAL or future family members.

If CYInvoice itself is later migrated to the new family, INV should also be rebuilt from the same new square-family master rather than preserving a visibly narrower / taller body beside newer icons.

---

## 2. Historical measurement

The existing production `CYInvoice.ico` contains native layers at:

`16 / 24 / 32 / 48 / 64 / 128 / 256 px`

Historical measured artwork bounds:

| Canvas | INV visual Live Area | Width occupancy | Height occupancy | Near-opaque core |
|---:|---:|---:|---:|---:|
| 16 × 16 | ~13 × 14 | ~81.2% | ~87.5% | ~12 × 12 |
| 24 × 24 | ~20 × 21 | ~83.3% | ~87.5% | ~18 × 20 |
| 32 × 32 | ~26 × 28 | ~81.2% | ~87.5% | ~24 × 27 |
| 48 × 48 | ~39 × 42 | ~81.2% | ~87.5% | ~38 × 41 |
| 64 × 64 | ~52 × 55 | ~81.2% | ~85.9% | ~50 × 55 |
| 128 × 128 | ~103 × 112 | ~80.5% | ~87.5% | ~101 × 110 |
| 256 × 256 | ~206 × 222 | ~80.5% | ~86.7% | ~206 × 222 |

Historical summary:

- width occupancy roughly 80–83%;
- height occupancy roughly 86–88%;
- 48 px visual area roughly 39 × 42 px.

These values describe **what the old INV icon is**, not **what future CY icons must become**.

---

## 3. Terminology retained from the measurement work

The following concepts remain useful even though the numeric target is historical:

- **Canvas** — complete bitmap / ICO layer.
- **Live Area** — visible artwork bounds inside the canvas.
- **Opaque Core** — near-fully-opaque interior reference.
- **Optical Margin** — apparent empty space around artwork.
- **Identifier Zone** — upper recognition area containing INV / ACC / ENV / Auto / WTM / CVT / CAL.
- **Motif Zone** — lower supporting area containing one simple app-specific motif.

These terms may still be used when building the new vector master.

---

## 4. What remains valid from the old analysis

The following principles remain useful:

- Canvas size alone does not guarantee equal apparent icon size.
- Family members should be compared side by side for visual mass.
- Optical centering matters more than mathematically identical margins.
- Small ICO layers may need optical adjustment rather than blind downscaling.
- Identifier hierarchy and motif hierarchy should remain consistent.
- Production should inspect at least `16 / 24 / 32 / 48 / 64 / 128 / 256` layers where those sizes are shipped.

What is **not** retained:

- old INV width / height occupancy as a future family target;
- old INV narrow/tall body as the master silhouette;
- the assumption that every new icon must visually fit the old INV frame proportion.

---

## 5. Current source of direction

The intended shared source for current icon-family direction is:

`simonliu1118-byte/AITeam/shared/cy-visual/icon-family/`

Current promotion work is tracked in AITeam PR #60. Until that PR is merged, do not describe AITeam `main` as already containing the canonical package.

Current direction includes:

- a newer square / fuller family body;
- short uppercase identifiers by default;
- `Auto` as the approved readability exception;
- one app-specific lower motif;
- INV's two horizontal lines treated as INV-specific, not family-wide;
- AI generation used for concept exploration only;
- a reusable editable vector master required for production consistency.

---

## 6. Handoff rule

When creating or revising a CY app icon:

1. Read the AITeam icon-family reference first.
2. Treat this file only as historical measurement context.
3. Do **not** copy the old INV live-area percentages as new geometry requirements.
4. Build from the new square-family master once the precision vector master is frozen.
