namespace CYERPAutoInput;

/// <summary>
/// Decides which text the read-only ERP structure probe may write as-is. UI chrome
/// (buttons, tabs, menus, column headers, window captions) is kept; anything that can
/// carry document data (edits, cells, rows, list items, static text) is reduced to its
/// length unless diagnostic logging is enabled.
/// </summary>
internal static class ProbeRedaction
{
    // MSAA ROLE_SYSTEM_* values for structural UI elements.
    private static readonly HashSet<int> StructuralRoles =
    [
        0x01, // TITLEBAR
        0x02, // MENUBAR
        0x09, // WINDOW
        0x0A, // CLIENT
        0x0B, // MENUPOPUP
        0x0C, // MENUITEM
        0x10, // PANE
        0x12, // DIALOG
        0x14, // GROUPING
        0x16, // TOOLBAR
        0x19, // COLUMNHEADER
        0x25, // PAGETAB
        0x26, // PROPERTYPAGE
        0x2B, // PUSHBUTTON
        0x2C, // CHECKBUTTON
        0x2D, // RADIOBUTTON
        0x38, // BUTTONDROPDOWN
        0x39, // BUTTONMENU
        0x3C, // PAGETABLIST
        0x3E  // SPLITBUTTON
    ];

    private static readonly string[] StructuralClassFragments =
    [
        "BUTTON", "TABSHEET", "PAGECONTROL", "BAR", "RIBBON", "GROUPBOX", "FORM", "PANEL"
    ];

    public static bool KeepMsaaName(int role, bool diagnostic) => diagnostic || StructuralRoles.Contains(role);

    public static bool KeepWindowText(string className, bool diagnostic)
    {
        if (diagnostic) return true;
        var upper = className.ToUpperInvariant();
        if (upper.Contains("EDIT", StringComparison.Ordinal) || upper.Contains("COMBO", StringComparison.Ordinal))
            return false;
        return StructuralClassFragments.Any(f => upper.Contains(f, StringComparison.Ordinal));
    }

    public static string Show(string? text, bool keep)
    {
        var value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (!keep) return $"<{value.Length} chars>";
        return value.Length <= 120 ? $"\"{value}\"" : $"\"{value[..120]}…\"";
    }
}
