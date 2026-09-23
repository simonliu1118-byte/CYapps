# CY Icon Creation Brief

> Purpose: handoff brief for future ChatGPT / AI / designer conversations.
>
> Read this document before creating or revising any CY app icon concept.
>
> Canonical family reference: `CY_ICON_FAMILY.md`

## 1. Objective

Create an app icon that looks like it belongs to the current **CY square-family icon system**.

The target is not merely “same color style” or “same square canvas.” The icon should match the family in:

- body/frame proportion;
- apparent visual mass;
- identifier hierarchy;
- lower-motif scale;
- line weight;
- white-space balance;
- simplicity;
- small-size readability.

Do not invent a new icon language for each project.

---

## 2. Required family appearance

Use these visual characteristics:

- square canvas;
- visually square rounded-card body;
- white interior;
- one dominant accent color;
- thick rounded-square accent frame;
- large identifier in the upper zone;
- one simple app-specific motif in the lower zone;
- flat / clean / high-contrast appearance;
- modern business-desktop tone;
- no unnecessary shadow, 3D, gloss, cartoon illustration, or decorative clutter.

The family should feel like related professional software tools, not unrelated logo illustrations.

---

## 3. Identifier selection

### Default

Use a short uppercase abbreviation whenever it remains recognizable.

Known examples:

- `INV` — CYInvoice
- `ACC` — CYAccounting
- `ENV` — CYEnvelope
- `WTM` — CYWatermark
- `CVT` — SMARTCOPIConverter
- `CAL` — TriINVCalc

### Exception

If the abbreviation would materially hurt recognition, a more intuitive label may be used as an explicit exception.

Current approved exception:

- `Auto` — CYERPAutoInput

Do not create multiple alternatives such as `AUT` / `Auto` unless the user explicitly asks for comparison. Current direction uses **Auto**.

Even when an exception is used, keep the same family geometry and visual weight.

---

## 4. Lower motif selection

Use **one simple motif specific to the app**.

Do not assume all CY icons use two horizontal lines.

The two horizontal lines are the concise lower motif of `INV` only.

Current motif directions:

| Identifier | Project | Lower motif direction |
|---|---|---|
| INV | CYInvoice | invoice/detail lines |
| ACC | CYAccounting | chart / accounting / money |
| ENV | CYEnvelope | envelope |
| Auto | CYERPAutoInput | automated input / document + direction |
| WTM | CYWatermark | watermark / marked document |
| CVT | SMARTCOPIConverter | conversion / document transform |
| CAL | TriINVCalc | calculator |

The motif should remain visually secondary to the identifier.

Prefer a simple silhouette over a detailed mini illustration.

---

## 5. Composition guidance

When generating or sketching:

- keep the frame/body size nearly the same as accepted family members;
- keep the upper identifier zone in the same vertical region;
- keep the lower motif zone in the same general region;
- keep the identifier block visually dominant;
- do not let a wide word push the frame wider;
- do not enlarge one motif so much that the family balance changes;
- do not shrink the entire icon merely because the motif is complex — simplify the motif instead.

The accepted current direction is **more square and fuller** than the historical narrow/tall INV body.

---

## 6. Color guidance

Use one main app accent color and white.

Apply the accent consistently to:

- outer frame;
- identifier;
- motif.

Do not depend on color alone for family recognition.

Do not introduce multiple unrelated accent colors inside a single icon unless the user explicitly changes the family system.

For concept generation, choose a color that is distinguishable from nearby family members. Final exact color may be adjusted later during production.

---

## 7. Generative-image instruction template

The following text can be reused as a starting instruction for image generation. Replace the app-specific fields only.

```text
Create a clean app-icon concept for the CY Apps square icon family.

Family requirements:
- square canvas
- visually square rounded-card body
- white interior
- one solid dominant accent color
- thick rounded-square accent frame
- large identifier in the upper area
- one simple app-specific motif in the lower area
- flat, minimal, high-contrast, professional Windows business-software appearance
- same apparent body size, frame weight, white-space balance, upper/lower hierarchy, and visual mass as the existing CY family
- no 3D, no glossy effect, no heavy shadow, no decorative illustration clutter
- small-size readability is important

Identifier: <IDENTIFIER>
App: <APP NAME>
Accent direction: <COLOR DIRECTION>
Lower motif: <ONE SIMPLE MOTIF>

Important:
- the CY family shares its square geometry and hierarchy, not a universal lower symbol
- do not automatically add the two invoice lines; those belong to INV only
- keep the lower motif secondary to the identifier
- do not change the family frame/body geometry to fit the motif
```

---

## 8. What AI generation cannot guarantee

Never assume that repeating the same prompt guarantees:

- identical corner radius;
- identical frame thickness;
- identical live area;
- identical identifier scale;
- identical motif position;
- identical small-size rendering.

AI-generated artwork is **concept material**, not the final consistency mechanism.

For a formally adopted icon:

1. compare it side by side with accepted family members;
2. select and approve the visual concept;
3. rebuild or clean the accepted concept using the shared editable vector master;
4. generate production-size assets from that master;
5. inspect Windows small-size rendering.

---

## 9. Side-by-side review checklist

Before presenting an icon as ready, compare it with at least two accepted family members and check:

- same apparent outer-body size;
- similar frame thickness and corner language;
- similar identifier-block weight;
- similar top/bottom white-space rhythm;
- simple motif;
- motif remains secondary;
- optical centering looks natural;
- no accidental extra decorative element;
- icon is readable at desktop size;
- icon should remain understandable when reduced.

If the new icon looks like a different logo placed beside CY icons, revise it.

---

## 10. Repository scope reminder

Current CY family projects intentionally considered by this reference:

### CYapps

- CYInvoice
- CYAccounting
- CYEnvelope
- CYERPAutoInput
- SMARTCOPIConverter
- TriINVCalc

### CYapps_pvt

- CYWatermark

`DriveDownloader` is not a CY App family member and must not be added to family boards or family asset work unless the user explicitly changes that product classification.

`CYAccountingWeb` should be treated separately from Windows desktop icon production unless the user explicitly asks to adopt the family for its web/app icon.

---

## 11. Handoff shortcut

For a future conversation, the shortest safe instruction is:

> Read `CY_ICON_FAMILY.md` and `CY_ICON_CREATION_BRIEF.md` first. Create the requested icon from the CY square-family skeleton. Default to a short uppercase identifier; `Auto` is the currently approved readability exception. Use one simple app-specific lower motif; the two lines belong to INV only. Keep the same frame/body proportion, visual mass, identifier/motif hierarchy, and small-size readability. Treat generated art as a concept and do not claim production-level geometry consistency until it has been rebuilt/checked against the shared vector master.
