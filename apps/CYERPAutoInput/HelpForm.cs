using System.Reflection;

namespace CYERPAutoInput;

/// <summary>
/// A help window: headings, text and example images (whose numbers are pixelated) in one
/// scrolling column that follows the window width, never a horizontal scrollbar.
/// </summary>
internal abstract class HelpForm : Form
{
    private readonly Panel _scroll = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 12, 20, 12) };
    private readonly FlowLayoutPanel _flow = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new Point(20, 12) };
    private readonly List<PictureBox> _pictures = [];

    protected HelpForm(string title, Size size)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowIcon = false; // secondary dialogs do not repeat the app icon (CY Desktop Visual Guide §11.1)
        Size = size;
        MinimumSize = new Size(720, 480);
        Font = new Font("Microsoft JhengHei UI", 9.5F);
        _scroll.Controls.Add(_flow);
        Controls.Add(_scroll);
        _scroll.HorizontalScroll.Enabled = false;
        Shown += (_, _) => Fit();
        Resize += (_, _) => Fit();
    }

    /// <summary>Call at the end of the subclass constructor, after the content.</summary>
    protected void Finish() => CyVisualTheme.Apply(this);

    protected void Heading(string text) => _flow.Controls.Add(new Label
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Microsoft JhengHei UI", 11F, FontStyle.Bold),
        Margin = new Padding(0, 14, 0, 4)
    });

    protected void Body(string text) => _flow.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(1000, 0), Margin = new Padding(0, 2, 0, 2) });

    protected void Example(string resource)
    {
        var image = LoadImage(resource);
        if (image is null) return;
        var picture = new PictureBox
        {
            Image = image,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 4, 0, 6),
            Size = image.Size
        };
        _pictures.Add(picture);
        _flow.Controls.Add(picture);
    }

    private void Fit()
    {
        var available = _scroll.ClientSize.Width - _scroll.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4;
        if (available < 200) return;
        _flow.SuspendLayout();
        foreach (var picture in _pictures)
        {
            var width = Math.Min(picture.Image!.Width + 2, available);
            picture.Size = new Size(width, (int)Math.Round((picture.Image.Height + 2) * (width / (double)(picture.Image.Width + 2))));
        }
        foreach (var label in _flow.Controls.OfType<Label>()) label.MaximumSize = new Size(available, 0);
        _flow.ResumeLayout();
        _scroll.HorizontalScroll.Visible = false;
    }

    private static Image? LoadImage(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"CYERPAutoInput.help.{name}");
        if (stream is null) return null;
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
