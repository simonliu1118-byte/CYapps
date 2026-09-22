# CY WinForms Theme Button Reference

> Status: `REFERENCE`  
> Validation source: CYInvoice Visual Shell V7 / Windows WinForms  
> Scope: WinForms only; this is a proven implementation reference, not a cross-framework mandate.

## Decision

For ordinary buttons, prefer the Windows / WinForms native themed `Button`.

When a key **Primary** or **Danger** action benefits materially from Theme color, a limited owner-painted button is acceptable **if it keeps the native `Button` as the underlying control**.

The validated CY pattern is:

- subclass `Button`; do not replace it with a custom `UserControl`;
- owner-paint appearance only;
- keep Click / keyboard / focus / tab / accessibility control behavior on the native Button base;
- use `AntiAlias`;
- clear with the parent background before drawing;
- fill and border the same rounded path;
- keep Hover / Pressed changes color-only; do not change geometry;
- draw inside the control bounds so anti-aliased edges are not clipped;
- if the custom result cannot visually match the native control cleanly, fall back to the native Button.

## Validated geometry

The V7 native-vs-theme comparison found the closest visual match to current Windows themed buttons at the tested sizes by using:

- **fixed 2 px corner radius** for Standard / Large / Danger;
- do **not** increase radius merely because the button is taller;
- a small symmetric vertical paint inset (about **1.5 px equivalent** in the 100% reference rendering) so the visible painted body matches the native button height more closely.

The 2 px value is a **WinForms reference result**, not a universal CY radius token for Qt / Win32 / other frameworks. DPI and framework scaling still require verification.

## Mixing native and themed buttons

A native Secondary button may be placed beside a Theme-colored Primary or Danger button when the hierarchy is intentional and the geometry is visually matched.

Validated comparison cases included:

- native `取消` + themed `儲存`;
- native `預覽` + large themed `開立測試發票`;
- native `關閉` + themed Danger `刪除`;
- same-label Standard and Large native-vs-themed comparisons.

Do not theme every button just because owner-paint is available. Theme-colored owner-paint is mainly for **key actions**.

## Theme / Danger

- Primary uses the selected Theme Accent family.
- Danger remains an independent Danger color and never becomes Coral.
- Coral Primary may use a softer coral/pink surface so it remains visually distinct from Danger red.

## Prototype

Validated prototype branch:

`design/cyinvoice-visual-shell-prototype`

Relevant files:

- `design/cy-desktop-visual-guide/prototypes/CYInvoiceVisualShell/RoundedThemeButtonV7.cs`
- `design/cy-desktop-visual-guide/prototypes/CYInvoiceVisualShell/ButtonLabFormV7.cs`

V7 launch commit:

`85b233a8135e0619d35f292a7805a051b43df126`
