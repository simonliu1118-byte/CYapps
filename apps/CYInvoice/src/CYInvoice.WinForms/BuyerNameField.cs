using CYInvoice.Core.Invoicing;

namespace CYInvoice.WinForms;

internal sealed class BuyerNameField : UserControl
{
    private readonly TextBox editor;
    private readonly Button retry = new NoFocusCueButton
    {
        Text = "↻",
        Dock = DockStyle.Right,
        Width = 28,
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
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        Padding = new Padding(4, 3, 0, 2);
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = Color.White;

        editor.Dock = DockStyle.Fill;
        editor.Margin = Padding.Empty;
        editor.BorderStyle = BorderStyle.None;
        editor.BackColor = Color.White;

        retry.FlatAppearance.BorderSize = 0;
        retry.BackColor = Color.White;
        retry.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 246, 252);
        retry.FlatAppearance.MouseDownBackColor = Color.FromArgb(222, 239, 250);
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
        retry.BackColor = BackColor;
        UpdateState();
    }

    private void UpdateState()
    {
        editor.ForeColor = HasApiMismatch ? Color.FromArgb(196, 0, 0) : SystemColors.WindowText;
        retry.Visible = !locked && HasApiMismatch;
        retry.Enabled = !locked;
    }
}
