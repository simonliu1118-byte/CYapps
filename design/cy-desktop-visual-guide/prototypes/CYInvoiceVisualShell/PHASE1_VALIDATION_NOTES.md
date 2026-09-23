# Phase 1 Windows Validation Notes

> Prototype-only checkpoint for the CY Desktop Visual Guide. This file records real Windows validation decisions before they are folded back into the canonical Visual Guide. It is not a permanent governance document.

## Buttons

- Native-first remains the default.
- If a button does not need special color / emphasis / state treatment, prefer the native control.
- Key Primary / Danger actions may use the validated CY owner-painted button treatment when stronger theme identity is useful.
- Native and themed buttons may coexist in the same application when role hierarchy is clear and height / font / alignment remain coherent.
- WinForms validated themed-button corner radius: approximately 2 logical px at 96 DPI; do not increase radius merely because the button is taller.

## Inputs

- Native TextBox / ComboBox / DateTimePicker are preferred.
- Standard WinForms reference: Microsoft JhengHei UI 10 pt is visually stable at 96 DPI.
- Do not force a tall single-line TextBox solely to make its mathematical height match another control.
- Density is App Choice. High-density screens may naturally use smaller text and controls; no fixed density package is imposed across all apps.

## Tabs

### Preferred visual option

Header-only Custom is the preferred option when a stronger CY visual identity is desired.

- Customize only the header strip.
- Keep native TabControl / TabPage page ownership and normal child controls underneath.
- Do not repaint the page contents.
- Active state: stronger text weight + theme Accent underline.
- Hover / keyboard focus may use a soft theme surface.
- Standard and Large headers are both App Choice.

### Native fallback

A native TabControl is fully acceptable as a lower-cost / framework-native alternative.

- Native borders, bevels and shadows are not considered a visual failure by themselves.
- Do not force an app to replace a stable native tab merely for visual uniformity.
- The legacy dotted focus rectangle should not be shown; keyboard operation must remain functional and focus must still be understandable through another state cue where needed.

### Rejected experiment

`TabAppearance.FlatButtons` as the visible tab style was rejected because it reads visually as old button-strip UI and does not fit the CY visual direction.

## Table / List

- Native DataGridView / equivalent native table control is preferred when it satisfies the app.
- Header stays neutral when a cell / row is selected. Do not color the current column header merely because a cell in that column has focus.
- Grid Continuity remains a must: header and body separators must line up through resize, scrollbar and DPI changes.
- Numeric values should normally align right; textual values left; status may center when useful.

### Selection interaction is App Choice

Do not force one selection model across all CY apps.

Supported patterns:

1. **Record List / Full Row Selection** — use when one row represents one business object and row-level actions such as open / print / delete operate on that record.
2. **Cell Selection** — use when individual cells are editable, copyable or independently meaningful.
3. **Row Hover + Cell Selection** — optional modern interaction; may be used when hover helps scanning but the actual selection unit is the cell.

During implementation, AI should recommend the most semantically appropriate model and explain why. The user may choose another supported model.

## Dialogs

- If MessageBox can fully express a simple information message, warning, error or Yes/No confirmation, use the native MessageBox.
- Custom dialogs are reserved for actual custom-content needs such as input, settings, preview, multi-choice or complex details.

## Theme

Validated theme set:

- Blue
- Teal
- Coral
- Apricot

Danger remains semantically independent from Coral.

## DPI validation target

Final integration must be checked at:

- 100% / 96 DPI
- 125% / 120 DPI
- 150% / 144 DPI

Validation points:

- no clipping
- readable text
- native input layout remains natural
- themed buttons scale without becoming excessively rounded
- tab headers do not clip or jump
- table header / body grid continuity remains correct
- scrollbars and resize behavior remain usable
