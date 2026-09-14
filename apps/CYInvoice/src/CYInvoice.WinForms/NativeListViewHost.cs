using System.Runtime.InteropServices;

namespace CYInvoice.WinForms;

internal sealed class NativeListViewHost : UserControl
{
    private const int LvmFirst = 0x1000;
    private const int LvmGetCountPerPage = LvmFirst + 40;
    private const int LvmGetHeader = LvmFirst + 31;
    private readonly VScrollBar emptyScrollBar = new()
    {
        Enabled = false,
        TabStop = false,
        Visible = true,
        Width = SystemInformation.VerticalScrollBarWidth,
    };
    private readonly ImageList rowHeightImages = new();
    private readonly int configuredRowHeight;
    private bool settingColumnWidths;
    private bool scrollNeeded;

    public NativeListViewHost(float fontSize = 10F, int rowHeight = 27)
    {
        configuredRowHeight = rowHeight;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = Color.White;

        List = new NativeListView
        {
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            HideSelection = true,
            LabelEdit = false,
            MultiSelect = false,
            Scrollable = true,
            UseCompatibleStateImageBehavior = false,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft JhengHei UI", fontSize),
        };
        rowHeightImages.ColorDepth = ColorDepth.Depth32Bit;
        rowHeightImages.ImageSize = new Size(1, rowHeight);
        rowHeightImages.Images.Add(new Bitmap(1, rowHeight));
        List.SmallImageList = rowHeightImages;
        List.ColumnWidthChanging += (_, eventArgs) =>
        {
            if (!settingColumnWidths)
            {
                eventArgs.Cancel = true;
                eventArgs.NewWidth = List.Columns[eventArgs.ColumnIndex].Width;
            }
        };

        Controls.Add(List);
        Controls.Add(emptyScrollBar);
        emptyScrollBar.BringToFront();
    }

    public ListView List { get; }

    public event EventHandler? ViewportChanged;

    public bool ScrollSlotReserved => !scrollNeeded;

    public int ColumnViewportWidth
    {
        get
        {
            var width = List.ClientSize.Width - 2;
            if (scrollNeeded) width -= SystemInformation.VerticalScrollBarWidth;
            return Math.Max(1, width);
        }
    }

    public int VisibleRowCapacity()
    {
        if (!List.IsHandleCreated) List.CreateControl();
        var count = SendMessage(List.Handle, LvmGetCountPerPage, IntPtr.Zero, IntPtr.Zero).ToInt32();
        return Math.Max(0, count);
    }

    public void SetScrollNeeded(bool needed)
    {
        scrollNeeded = needed;
        emptyScrollBar.Visible = !needed;
        emptyScrollBar.Enabled = false;
        PerformLayout();
        emptyScrollBar.BringToFront();
    }

    public int HeightForRows(int rowCount)
    {
        if (!List.IsHandleCreated) List.CreateControl();
        var headerHeight = HeaderHeight();
        var actualRowHeight = configuredRowHeight;
        if (List.Items.Count > 0)
        {
            try { actualRowHeight = Math.Max(actualRowHeight, List.GetItemRect(0).Height); }
            catch (ArgumentException) { }
        }
        return headerHeight + (Math.Max(1, rowCount) * actualRowHeight) + 2;
    }

    public static void DrawHeader(DrawListViewColumnHeaderEventArgs eventArgs, Font font)
    {
        using (var background = new SolidBrush(Color.FromArgb(246, 246, 246)))
            eventArgs.Graphics.FillRectangle(background, eventArgs.Bounds);

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        var header = eventArgs.Header;
        flags |= header?.TextAlign switch
        {
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            HorizontalAlignment.Right => TextFormatFlags.Right,
            _ => TextFormatFlags.Left,
        };
        var textBounds = Rectangle.Inflate(eventArgs.Bounds, -6, 0);
        TextRenderer.DrawText(eventArgs.Graphics, header?.Text ?? string.Empty, font, textBounds, SystemColors.ControlText, flags);
        using var pen = new Pen(Color.FromArgb(190, 190, 190));
        eventArgs.Graphics.DrawLine(pen, eventArgs.Bounds.Right - 1, eventArgs.Bounds.Top, eventArgs.Bounds.Right - 1, eventArgs.Bounds.Bottom);
        eventArgs.Graphics.DrawLine(pen, eventArgs.Bounds.Left, eventArgs.Bounds.Bottom - 1, eventArgs.Bounds.Right, eventArgs.Bounds.Bottom - 1);
    }

    private int HeaderHeight()
    {
        var header = SendMessage(List.Handle, LvmGetHeader, IntPtr.Zero, IntPtr.Zero);
        if (header != IntPtr.Zero && GetWindowRect(header, out var bounds))
        {
            var height = bounds.Bottom - bounds.Top;
            if (height > 0) return height;
        }
        return TextRenderer.MeasureText("Ag", List.Font).Height + 10;
    }

    public void SetColumnWidths(IReadOnlyList<int> widths)
    {
        if (widths.Count != List.Columns.Count) throw new ArgumentException("欄寬數量與清單欄位不一致", nameof(widths));
        settingColumnWidths = true;
        try
        {
            for (var index = 0; index < widths.Count; index++)
                List.Columns[index].Width = Math.Max(1, widths[index]);
        }
        finally
        {
            settingColumnWidths = false;
        }
    }

    protected override void OnSizeChanged(EventArgs eventArgs)
    {
        base.OnSizeChanged(eventArgs);
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke((Action)(() =>
        {
            if (!IsDisposed && IsHandleCreated) ViewportChanged?.Invoke(this, EventArgs.Empty);
        }));
    }

    protected override void OnLayout(LayoutEventArgs eventArgs)
    {
        base.OnLayout(eventArgs);
        var reservedWidth = scrollNeeded ? 0 : emptyScrollBar.Width;
        List.SetBounds(0, 0, Math.Max(1, ClientSize.Width - reservedWidth), Math.Max(1, ClientSize.Height));
        emptyScrollBar.SetBounds(
            Math.Max(0, ClientSize.Width - emptyScrollBar.Width),
            0,
            emptyScrollBar.Width,
            Math.Max(1, ClientSize.Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) rowHeightImages.Dispose();
        base.Dispose(disposing);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed class NativeListView : ListView
    {
        public NativeListView() => DoubleBuffered = true;
    }
}
