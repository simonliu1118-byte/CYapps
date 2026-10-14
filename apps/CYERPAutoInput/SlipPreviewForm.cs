using System.Drawing.Imaging;

namespace CYERPAutoInput;

/// <summary>測試全家裁切列印's preview: the cut slip as it will print, its sizes, 列印／取消.</summary>
internal sealed class SlipPreviewForm : Form
{
    private readonly Bitmap _image;

    public SlipPreviewForm(GrayImage slip, string info, string printer)
    {
        Text = "測試全家裁切列印：預覽";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(720, 900);
        MinimumSize = new Size(480, 480);
        MinimizeBox = false;
        ShowIcon = false;
        TopMost = true;
        Font = new Font("Microsoft JhengHei UI", 10F);

        _image = new Bitmap(slip.Width, slip.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < slip.Height; y++)
            for (var x = 0; x < slip.Width; x++)
            {
                var v = slip[x, y];
                _image.SetPixel(x, y, Color.FromArgb(v, v, v));
            }

        var picture = new PictureBox { Dock = DockStyle.Fill, Image = _image, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
        var text = new Label { Dock = DockStyle.Top, Height = 64, Text = info + Environment.NewLine + "印表機：" + printer, Padding = new Padding(12, 8, 12, 0) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 50, Padding = new Padding(8) };
        var print = new CyPrimaryButton { Text = "列印", Size = new Size(92, 34), DialogResult = DialogResult.OK };
        var cancel = new CyButton { Text = "取消", Size = new Size(92, 34), DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(print);
        Controls.Add(picture);
        Controls.Add(text);
        Controls.Add(buttons);
        AcceptButton = print;
        CancelButton = cancel;
        CyVisualTheme.Apply(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image.Dispose();
        base.Dispose(disposing);
    }
}
