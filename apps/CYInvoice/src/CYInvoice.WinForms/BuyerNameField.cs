using CYInvoice.Core.Invoicing;

namespace CYInvoice.WinForms;

internal sealed class BuyerNameField : UserControl
{
    private readonly TextBox editor;
    private readonly int nativeFieldHeight;
    private readonly Button retry = new NoFocusCueButton
    {
        Text = "↻",
        Dock = DockStyle.Right,
        Width = 30,
        TabStop = false,
        FlatStyle = FlatStyle.Flat,
        Visible = false,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        UseVisualStyleBackColor = false,
    };
    private readonly ToolTip toolTip = new();
    private string apiName = string.Empty;
    private bool locked;

    public BuyerNameField(TextBox editor)
    {
        this.editor = editor;
        nativeFieldHeight = editor.PreferredHeight;

        editor.Dock = DockStyle.Fill;
        editor.Margin = Padding.Empty;
        editor.BorderStyle = BorderStyle.None;
        editor.BackColor = Color.White;

        Dock = DockStyle.None;
        Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Margin = new Padding(3, 5, 3, 5);
        Padding = new Padding(3, 0, 1, 0);
        BorderStyle = BorderStyle.Fixed3D;
        BackColor = Color.White;
        AutoSize = false;
        Height = nativeFieldHeight;
        MinimumSize = new Size(0, nativeFieldHeight);
        MaximumSize = new Size(0, nativeFieldHeight);

        retry.FlatAppearance.BorderSize = 1;
        retry.FlatAppearance.BorderColor = Color.FromArgb(145, 145, 145);
        retry.ForeColor = Color.FromArgb(55, 55, 55);
        retry.BackColor = Color.FromArgb(248, 248, 248);
        retry.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 243, 252);
        retry.FlatAppearance.MouseDownBackColor = Color.FromArgb(214, 234, 249);
        toolTip.SetToolTip(retry, "重新查詢買受人名稱");

        Controls.Add(editor);
        Controls.Add(retry);
        retry.BringToFront();
        editor.TextChanged += (_, _) => UpdateState();
        retry.Click += (_, _) => RetryRequested?.Invoke(this, EventArgs.Empty);
    }

    public TextBox Editor => editor;
    public string ApiName => apiName;
    public bool HasApiMismatch => apiName.Length != 0 && !string.Equals(editor.Text.Trim(), apiName, StringComparison.Ordinal);
    public event EventHandler? RetryRequested;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width > 0 ? proposedSize.Width : 120;
        return new Size(width, nativeFieldHeight);
    }

    public void SetLookupState(NameLookup lookup)
    {
        apiName = lookup.ApiName.Trim();
        UpdateState();
    }

    public void ClearLookupState()
    {
        apiName = string.Empty;
        UpdateState();
    }

    public void SetLocked(bool value)
    {
        locked = value;
        editor.Enabled = true;
        editor.ReadOnly = value;
        editor.TabStop = !value;
        BackColor = value ? Color.FromArgb(242, 242, 242) : Color.White;
        editor.BackColor = BackColor;
        retry.BackColor = value ? BackColor : Color.FromArgb(248, 248, 248);
        UpdateState();
    }

    private void UpdateState()
    {
        editor.ForeColor = HasApiMismatch ? Color.FromArgb(196, 0, 0) : SystemColors.WindowText;
        retry.Visible = !locked && HasApiMismatch;
        retry.Enabled = !locked;
    }
}
