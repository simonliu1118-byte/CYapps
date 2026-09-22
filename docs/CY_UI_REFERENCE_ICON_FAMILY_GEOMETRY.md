# CY App Icon Family Geometry Reference

> Status: `REFERENCE / PROVISIONAL FAMILY RULE`  
> Source of truth for the baseline geometry: existing preserved `apps/CYInvoice/assets/CYInvoice.ico`  
> Scope: future CY desktop app icons. This document does **not** authorize changing the existing CYInvoice icon.

## 1. Purpose

CY App icons are not considered the same family merely because they use the same canvas size, color style, or three-letter abbreviation.

The **visible graphic mass** must also occupy a comparable area of the canvas and use a comparable internal structure. Otherwise one app will look visually smaller, larger, heavier, or more crowded than another in Explorer, Desktop, Taskbar, Start, and window title bars.

The existing CYInvoice `INV` icon is the preserved family master. New icons should grow from its measured geometry rather than redefining the master.

---

## 2. Terminology

### Canvas

The complete bitmap / ICO layer, for example `48 × 48`.

### Live Area

The bounding box of the visually meaningful icon artwork inside the canvas, including normal anti-aliased edge pixels that materially contribute to the visible shape.

### Opaque Core

The near-fully-opaque interior of the artwork. It is useful as a measurement reference, but it is **not** the only area that counts visually.

### Optical Margin

The apparent empty space between the Live Area and the canvas edge. It may differ by roughly 1–2 px between sides or between icon shapes when needed for visual centering.

### Abbreviation Zone

The main recognition area containing `INV`, `ACC`, `ENV`, `CAL`, `CVT`, `WM`, or another approved short identifier.

### Symbol Zone

The lower supporting area containing one simple functional symbol or motif. It must remain subordinate to the abbreviation and must not become a second detailed illustration.

---

## 3. Preserved CYInvoice master measurement

The production file contains native layers at:

`16 / 24 / 32 / 48 / 64 / 128 / 256 px`

Measured from the preserved `apps/CYInvoice/assets/CYInvoice.ico`:

| Canvas | Measured visual Live Area | Width occupancy | Height occupancy | Near-opaque core |
|---:|---:|---:|---:|---:|
| 16 × 16 | ~13 × 14 | ~81.2% | ~87.5% | ~12 × 12 |
| 24 × 24 | ~20 × 21 | ~83.3% | ~87.5% | ~18 × 20 |
| 32 × 32 | ~26 × 28 | ~81.2% | ~87.5% | ~24 × 27 |
| 48 × 48 | ~39 × 42 | ~81.2% | ~87.5% | ~38 × 41 |
| 64 × 64 | ~52 × 55 | ~81.2% | ~85.9% | ~50 × 55 |
| 128 × 128 | ~103 × 112 | ~80.5% | ~87.5% | ~101 × 110 |
| 256 × 256 | ~206 × 222 | ~80.5% | ~86.7% | ~206 × 222 |

The family-level conclusion is therefore:

> **The INV master normally occupies about 80–83% of canvas width and about 86–88% of canvas height.**

This is a **visual mass target**, not a requirement to mathematically scale every future icon to the exact same pixel rectangle.

### 48 px reference

The most useful desktop reference is the 48 × 48 layer:

- visual Live Area: about **39 × 42 px**;
- near-opaque core: about **38 × 41 px**;
- optical side margins: roughly **4–5 px**;
- optical top / bottom margins: roughly **3 px**.

This closely matches the icon's observed Windows Desktop rendering and is the preferred starting point when comparing new family members at normal desktop size.

---

## 4. Family geometry rules

### 4.1 Visual mass — CORE

New icons should target approximately the same **apparent visual mass** as the INV master.

Recommended family reference:

- width occupancy: approximately **80–83%**;
- height occupancy: approximately **86–88%**;
- optical centering around the canvas center;
- no app should casually use a tiny ~65–70% live area or an almost edge-to-edge ~95% live area while claiming the same family geometry.

A small shape-specific correction is allowed when it improves apparent equality.

### 4.2 Canvas is not Live Area

`48 × 48` only describes the canvas. It does **not** mean the drawing should fill 48 × 48.

All new icon reviews must compare both:

1. the exported canvas size; and
2. the actual artwork Live Area / optical mass.

### 4.3 Abbreviation Zone — CORE

The large abbreviation remains the primary recognition element.

- Prefer short uppercase identifiers already associated with the app, such as `INV`, `ACC`, `ENV`, `CAL`, `CVT`, `WM`.
- The abbreviation should occupy a comparable visual width and height across the family.
- Do **not** force every abbreviation to the same font size if different letter shapes make one look materially smaller or larger.
- Small optical adjustments to font size, horizontal scale, tracking, or x-position are allowed to equalize the visible abbreviation block.
- The target is comparable **visual bounding box and weight**, not identical typographic parameters.

### 4.4 Symbol Zone — CORE

The lower motif is secondary.

- Keep it simple enough to remain legible at 16 / 24 / 32 px.
- Prefer one symbol or one very simple motif.
- Keep its vertical zone, spacing from the abbreviation, and overall weight comparable to the INV lower-line motif.
- A new symbol must not extend the overall Live Area far beyond the family target merely because its natural shape is tall or wide.
- If a symbol requires excessive detail to be recognizable, simplify the symbol rather than enlarge the entire icon.

### 4.5 Optical centering — CORE

Do not require mathematically identical left / right / top / bottom margins.

Allowed optical correction examples:

- `INV` may need different horizontal compensation from `ACC` because the letters have different visual widths;
- a circular or diagonal symbol may need ~1 px shift compared with a rectangular symbol;
- a bottom-heavy symbol may need slightly more lower margin to appear centered.

The result should look centered at normal Windows display sizes.

---

## 5. Small-size handling

### 5.1 Do not blindly downscale

The production INV ICO already shows small optical changes between layers. Therefore new icons should not be generated only by taking a 256 px master and mechanically shrinking it to every size.

At minimum, inspect:

- 16 px;
- 24 px;
- 32 px;
- 48 px;
- 64 px;
- 128 px;
- 256 px.

### 5.2 Allowed small-size corrections

At small layers, it is acceptable to adjust:

- stroke thickness;
- gap between abbreviation and symbol;
- abbreviation size by a small amount;
- symbol simplification;
- 1 px alignment / centering;
- anti-aliasing / pixel snapping.

The objective is to preserve the **same perceived icon**, not the same raw vector coordinates.

### 5.3 What must not change between layers

- app identity;
- main abbreviation;
- overall family silhouette;
- primary / secondary hierarchy;
- major color identity;
- general live-area proportion.

---

## 6. Family acceptance check

Before accepting a new CY app icon, compare it directly beside the preserved INV icon at **48 px** and at least one smaller size.

Check all of the following:

- similar apparent total size;
- similar visual weight;
- abbreviation block is neither obviously smaller nor larger;
- symbol remains subordinate;
- icon is optically centered;
- margins are not conspicuously tighter / looser than INV;
- no symbol detail collapses at 16 / 24 / 32 px;
- the icon still reads as the same family when app colors differ.

A new icon is **not accepted** merely because its drawing fits inside the ICO canvas.

---

## 7. Current family DNA inherited from INV

The geometry rules above combine with the existing visual DNA:

- flat;
- high contrast;
- large abbreviation;
- one simple supporting symbol / motif;
- no gradient;
- no shadow;
- no 3D rendering;
- small-size recognizability first.

The exact functional symbol and app color can vary by app; geometry and visual mass should remain recognizably related.

---

## 8. Source / preservation

Preserved production icon:

`apps/CYInvoice/assets/CYInvoice.ico`

The existing CYInvoice icon remains `PRESERVE` and must not be modified to fit future family work. Future family members adapt to this master, not the reverse.
