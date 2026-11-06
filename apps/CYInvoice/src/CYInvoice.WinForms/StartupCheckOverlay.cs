using System.Drawing.Drawing2D;

namespace CYInvoice.WinForms;

internal sealed class StartupCheckOverlay : UserControl
{
    private readonly System.Windows.Forms.Timer animation=new() { Interval=40 };
    private readonly StartupSpinner spinner=new() { Size=new Size(40,40),Margin=new Padding(0,0,16,0) };
    public StartupCheckOverlay()
    {
        AccessibleName = "啟動檢查中";
        Font = new Font("Microsoft JhengHei UI", 14F);
        Size = new Size(270, 76);
        TabStop = false;
        Anchor = AnchorStyles.None;
        BackColor = Color.White;
        var row = new FlowLayoutPanel {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty,
        };
        row.Controls.Add(spinner);
        animation.Tick+=(_,_)=>spinner.Advance();
        row.Controls.Add(new Label {
            Text = "啟動檢查中", AutoSize = true, Margin = new Padding(0, 7, 0, 0),
        });
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1 };
        row.Anchor = AnchorStyles.None;
        layout.Controls.Add(row);
        Controls.Add(layout);
    }

    internal void VerifyAnimation()
    {
        using var before=new Bitmap(spinner.Width,spinner.Height);
        using var after=new Bitmap(spinner.Width,spinner.Height);
        Console.WriteLine("Startup smoke animation: first shared paint frame");
        using(var graphics=Graphics.FromImage(before))spinner.DrawFrame(graphics);
        // Test-only native modal pump. Capture the same paint owner directly:
        // WM_PRINT/DoEvents on a headless desktop must not own test completion.
        // The product still has only the MainForm-owned overlay, no dialog.
        using(var measurement=new Form { ShowInTaskbar=false,ShowIcon=false,
            FormBorderStyle=FormBorderStyle.None,Size=new Size(1,1),
            StartPosition=FormStartPosition.Manual,Location=new Point(-32000,-32000) })
        using(var sample=new System.Threading.Timer(_=>measurement.BeginInvoke((Action)(()=> {
            Console.WriteLine("Startup smoke animation: sample deadline");
            measurement.DialogResult=DialogResult.OK;
        })),null,Timeout.Infinite,Timeout.Infinite)) {
            measurement.Shown+=(_,_)=> { Console.WriteLine("Startup smoke animation: native pump");sample.Change(250,Timeout.Infinite); };
            measurement.ShowDialog(FindForm());
        }
        Console.WriteLine("Startup smoke animation: second shared paint frame");
        using(var graphics=Graphics.FromImage(after))spinner.DrawFrame(graphics);
        var changed=false;
        for(var y=0;y<before.Height&&!changed;y++)
            for(var x=0;x<before.Width&&!changed;x++) changed=before.GetPixel(x,y)!=after.GetPixel(x,y);
        if(!changed)throw new InvalidOperationException("啟動圈圈未實際更新畫面");
        Visible=false;
        if(animation.Enabled)throw new InvalidOperationException("隱藏的啟動圈圈仍在運作");
        Visible=true;
        if(!animation.Enabled)throw new InvalidOperationException("啟動圈圈未恢復轉動");
    }

    // Visibility belongs to this overlay. A nested paint control does not
    // reliably receive visibility changes of every ancestor on WinForms.
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if(!Disposing)animation.Enabled=IsHandleCreated&&Visible;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        animation.Enabled=Visible;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if(!Disposing)animation.Stop();
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)animation.Dispose();
        base.Dispose(disposing);
    }

    // WinForms has no native circular progress control. Use the same small arc
    // animation as the CY desktop updater; paint has no application side effects.
    private sealed class StartupSpinner : Control
    {
        private float angle;

        public StartupSpinner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            TabStop = false;
        }
        internal void Advance() { angle=(angle+12)%360;Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawFrame(e.Graphics);
        }

        internal void DrawFrame(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var size = Math.Min(Width, Height) - 6;
            if (size <= 0) return;
            var box = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
            using var track = new Pen(Color.FromArgb(220, 230, 244), 3.5F);
            using var arc = new Pen(SystemColors.Highlight, 3.5F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawEllipse(track, box);
            graphics.DrawArc(arc, box, angle, 100);
        }

    }
}
