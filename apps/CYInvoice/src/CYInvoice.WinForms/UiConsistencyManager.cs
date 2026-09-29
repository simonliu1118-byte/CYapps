using System.Runtime.InteropServices;

namespace CYInvoice.WinForms;

/// <summary>
/// Shared runtime consistency layer for native WinForms controls that otherwise tend to
/// grow one-off layout/tooltip fixes in each form. The manager is deliberately narrow:
/// it only normalizes Details ListViews, clipped text hints and the RecordDetail action row.
/// </summary>
internal static class UiConsistencyManager
{
    private static readonly HashSet<Control> Registered = [];
    private static readonly HashSet<ListView> LayoutInProgress = [];
    private static readonly HashSet<ListView> LayoutQueued = [];
    private static readonly ToolTip ClippedTextToolTip = new()
    {
        AutoPopDelay = 8000,
        InitialDelay = 350,
        ReshowDelay = 100,
        ShowAlways = true,
    };
    private static bool installed;
    private static Control? toolTipOwner;
    private static string toolTipText = string.Empty;

    internal static void Install()
    {
        if (installed) return;
        installed = true;
        Application.Idle += (_, _) => RegisterOpenForms();
    }

    private static void RegisterOpenForms()
    {
        foreach (Form form in Application.OpenForms)
            RegisterTree(form);
    }

    private static void RegisterTree(Control control)
    {
        if (!Registered.Add(control)) return;
        control.Disposed += (_, _) => Registered.Remove(control);
        control.ControlAdded += (_, eventArgs) => RegisterTree(eventArgs.Control);

        switch (control)
        {
            case ListView list when list.View == View.Details:
                RegisterListView(list);
                break;
            case Label label when label.AutoEllipsis:
                RegisterClippedLabel(label);
                break;
            case DataGridView grid:
                grid.ShowCellToolTips = true;
                break;
        }

        foreach (Control child in control.Controls)
            RegisterTree(child);

        if (control is RecordDetailForm detail)
            NormalizeRecordDetailActions(detail);
    }

    private static void RegisterListView(ListView list)
    {
        if (!list.OwnerDraw && FillColumnIndex(list) >= 0)
        {
            list.OwnerDraw = true;
            list.DrawColumnHeader += (_, eventArgs) => NativeListViewHost.DrawHeader(eventArgs, list.Font);
            list.DrawItem += (_, eventArgs) =>
            {
                if (list.View != View.Details) eventArgs.DrawDefault = true;
            };
            list.DrawSubItem += (_, eventArgs) => eventArgs.DrawDefault = true;
        }

        list.SizeChanged += (_, _) => QueueListLayout(list);
        list.HandleCreated += (_, _) => QueueListLayout(list);
        list.ColumnWidthChanged += (_, _) => QueueListLayout(list);
        list.MouseMove += (_, eventArgs) => ShowClippedListText(list, eventArgs.Location);
        list.MouseLeave += (_, _) => HideToolTip(list);
        ApplyListLayout(list);
    }

    private static void QueueListLayout(ListView list)
    {
        if (list.IsDisposed || LayoutInProgress.Contains(list) || !list.IsHandleCreated) return;
        if (!LayoutQueued.Add(list)) return;
        list.BeginInvoke((Action)(() =>
        {
            LayoutQueued.Remove(list);
            if (!list.IsDisposed) ApplyListLayout(list);
        }));
    }

    private static void ApplyListLayout(ListView list)
    {
        if (list.IsDisposed || list.Columns.Count == 0 || list.ClientSize.Width <= 0) return;
        if (!LayoutInProgress.Add(list)) return;
        try
        {
            var fillIndex = FillColumnIndex(list);
            if (fillIndex < 0) return;

            // Operation history used to make the 類型 header too narrow and then let 摘要
            // stop short of the actual viewport. Keep the semantic columns readable first.
            if (IsOperationHistory(list))
            {
                list.Columns[0].Width = Math.Max(list.Columns[0].Width,
                    Math.Max(60, TextRenderer.MeasureText("類型", list.Font).Width + 18));
                list.Columns[1].Width = Math.Max(list.Columns[1].Width,
                    TextRenderer.MeasureText("2026/09/29", list.Font).Width + 16);
            }

            var viewportWidth = ColumnViewportWidth(list);
            var fixedWidth = list.Columns.Cast<ColumnHeader>()
                .Where((_, index) => index != fillIndex)
                .Sum(column => column.Width);
            var minimum = FillColumnMinimum(list, fillIndex);
            list.Columns[fillIndex].Width = Math.Max(minimum, viewportWidth - fixedWidth);
            list.Invalidate(true);
        }
        finally
        {
            LayoutInProgress.Remove(list);
        }
    }

    private static int FillColumnIndex(ListView list)
    {
        var headers = list.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray();
        var content = Array.FindIndex(headers, text => string.Equals(text, "內容", StringComparison.Ordinal));
        if (content >= 0) return content;
        var summary = Array.FindIndex(headers, text => string.Equals(text, "摘要", StringComparison.Ordinal));
        if (summary >= 0) return summary;
        if (headers.Contains("發票號碼", StringComparer.Ordinal))
        {
            var buyer = Array.FindIndex(headers, text => string.Equals(text, "買受人", StringComparison.Ordinal));
            if (buyer >= 0) return buyer;
        }
        return -1;
    }

    private static int FillColumnMinimum(ListView list, int fillIndex)
    {
        var header = list.Columns[fillIndex].Text;
        return header switch
        {
            "內容" => 120,
            "摘要" => 80,
            "買受人" => 90,
            _ => 60,
        };
    }

    private static bool IsOperationHistory(ListView list) =>
        list.Columns.Count == 3 &&
        string.Equals(list.Columns[0].Text, "類型", StringComparison.Ordinal) &&
        string.Equals(list.Columns[1].Text, "日期", StringComparison.Ordinal) &&
        string.Equals(list.Columns[2].Text, "摘要", StringComparison.Ordinal);

    private static int ColumnViewportWidth(ListView list)
    {
        var width = list.ClientSize.Width;
        if (list.IsHandleCreated && GetClientRect(list.Handle, out var bounds) && bounds.Right > bounds.Left)
            width = bounds.Right - bounds.Left;
        var dpi = list.DeviceDpi > 0 ? list.DeviceDpi : 96;
        var logicalWidth = (int)Math.Floor(width * 96D / dpi);
        return Math.Max(1, logicalWidth - 3);
    }

    private static void RegisterClippedLabel(Label label)
    {
        label.MouseMove += (_, eventArgs) =>
        {
            if (!IsLabelClipped(label))
            {
                HideToolTip(label);
                return;
            }
            ShowToolTip(label, label.Text, eventArgs.Location);
        };
        label.MouseLeave += (_, _) => HideToolTip(label);
    }

    private static bool IsLabelClipped(Label label)
    {
        if (label.AutoSize || string.IsNullOrWhiteSpace(label.Text)) return false;
        var available = Math.Max(1, label.ClientSize.Width - label.Padding.Horizontal - 4);
        var measured = TextRenderer.MeasureText(label.Text, label.Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        return measured > available;
    }

    private static void ShowClippedListText(ListView list, Point location)
    {
        var hit = list.HitTest(location);
        if (hit.Item is null || hit.SubItem is null || string.IsNullOrWhiteSpace(hit.SubItem.Text))
        {
            HideToolTip(list);
            return;
        }

        var bounds = hit.SubItem.Bounds;
        var available = Math.Max(1, bounds.Width - 12);
        var measured = TextRenderer.MeasureText(hit.SubItem.Text, list.Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        if (measured <= available)
        {
            HideToolTip(list);
            return;
        }

        ShowToolTip(list, hit.SubItem.Text, location);
    }

    private static void ShowToolTip(Control owner, string text, Point location)
    {
        if (ReferenceEquals(toolTipOwner, owner) && string.Equals(toolTipText, text, StringComparison.Ordinal)) return;
        if (toolTipOwner is not null) ClippedTextToolTip.Hide(toolTipOwner);
        toolTipOwner = owner;
        toolTipText = text;
        ClippedTextToolTip.Show(text, owner,
            new Point(Math.Max(0, Math.Min(owner.ClientSize.Width - 4, location.X + 14)),
                Math.Max(0, Math.Min(owner.ClientSize.Height - 4, location.Y + 18))),
            8000);
    }

    private static void HideToolTip(Control owner)
    {
        if (!ReferenceEquals(toolTipOwner, owner)) return;
        ClippedTextToolTip.Hide(owner);
        toolTipOwner = null;
        toolTipText = string.Empty;
    }

    private static void NormalizeRecordDetailActions(RecordDetailForm form)
    {
        foreach (var button in Descendants(form).OfType<Button>())
        {
            if (!string.Equals(button.Text, "關閉", StringComparison.Ordinal)) continue;
            button.Width = 112;
            button.Height = UiControls.StandardButtonHeight;
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
