using System.Runtime.InteropServices;

namespace CYInvoice.WinForms;

internal sealed class NativeListViewHost : UserControl
{
    private const int LvmFirst = 0x1000;
    private const int LvmGetCountPerPage = LvmFirst + 40;
    private readonly VScrollBar emptyScrollBar = new()
    {
        Dock = DockStyle.Right,
        Enabled = false,
        TabStop = false,
        Visible = true,
        Width = SystemInformation.VerticalScrollBarWidth,
    };
    private readonly ImageList rowHeightImages = new();
    private bool settingColumnWidths;
    private bool scrollNeeded;

    public NativeListViewHost(float fontSize = 10F, int rowHeight = 27)
    {
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = Color.White;

        List = new NativeListView
        {
            Dock = DockStyle.Fill,
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
        emptyScrollBar.BringToFront();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) rowHeightImages.Dispose();
        base.Dispose(disposing);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private sealed class NativeListView : ListView
    {
        public NativeListView() => DoubleBuffered = true;
    }
}
