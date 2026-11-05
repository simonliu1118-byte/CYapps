using System.Drawing.Drawing2D;

namespace CYInvoice.WinForms;

internal sealed class StartupCheckForm : Form
{
    public StartupCheckForm()
    {
        Text = "啟動檢查中";
        Font = new Font("Microsoft JhengHei UI", 10F);
        ClientSize = new Size(280, 90);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        var row = new FlowLayoutPanel {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty,
        };
        row.Controls.Add(new StartupSpinner { Size = new Size(28, 28), Margin = new Padding(0, 0, 12, 0) });
        row.Controls.Add(new Label {
            Text = "啟動檢查中", AutoSize = true, Margin = new Padding(0, 4, 0, 0),
        });
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1 };
        row.Anchor = AnchorStyles.None;
        layout.Controls.Add(row);
        Controls.Add(layout);
    }

    protected override void OnLoad(EventArgs e)
    {
        if (Owner is { } owner)
            Location = new Point(owner.Left + (owner.Width - Width) / 2, owner.Top + (owner.Height - Height) / 2);
        base.OnLoad(e);
    }

    // WinForms has no native circular progress control. Use the same small arc
    // animation as the CY desktop updater; paint has no application side effects.
    private sealed class StartupSpinner : Control
    {
        private readonly System.Windows.Forms.Timer timer = new() { Interval = 40 };
        private float angle;

        public StartupSpinner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            TabStop = false;
            timer.Tick += (_, _) => { angle = (angle + 12) % 360; Invalidate(); };
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            timer.Enabled = Visible;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var size = Math.Min(Width, Height) - 6;
            if (size <= 0) return;
            var box = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
            using var track = new Pen(Color.FromArgb(220, 230, 244), 2.5F);
            using var arc = new Pen(SystemColors.Highlight, 2.5F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawEllipse(track, box);
            e.Graphics.DrawArc(arc, box, angle, 100);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
