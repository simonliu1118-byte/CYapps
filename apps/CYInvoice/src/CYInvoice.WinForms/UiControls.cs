using System.Runtime.InteropServices;

namespace CYInvoice.WinForms;

internal class NoFocusCueButton : Button
{
    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        UiControls.HideFocusCue(this);
    }

    protected override void OnGotFocus(EventArgs eventArgs)
    {
        base.OnGotFocus(eventArgs);
        UiControls.HideFocusCue(this);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        UiControls.HideFocusCue(this);
    }
}

internal sealed class BufferedTableLayoutPanel : TableLayoutPanel
{
    public BufferedTableLayoutPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
}

internal sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
{
    public BufferedFlowLayoutPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
}

internal sealed class NoFocusCueTabControl : TabControl
{
    public NoFocusCueTabControl() => TabStop = false;

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        UiControls.HideFocusCue(this);
    }

    protected override void OnGotFocus(EventArgs eventArgs)
    {
        base.OnGotFocus(eventArgs);
        UiControls.HideFocusCue(this);
    }
}

internal sealed class EnterNavigationMessageFilter : IMessageFilter
{
    private const int WmKeyDown = 0x0100;
    private const int VkReturn = 0x0D;

    public bool PreFilterMessage(ref Message message)
    {
        if (message.Msg != WmKeyDown || message.WParam.ToInt32() != VkReturn) return false;
        if ((Control.ModifierKeys & (Keys.Control | Keys.Alt)) != Keys.None) return false;

        var form = Form.ActiveForm;
        if (form is null || (!form.Modal && form.FormBorderStyle != FormBorderStyle.FixedDialog)) return false;

        var focused = LogicalInput(FindFocusedControl(form));
        if (focused is null || PreserveNativeEnter(focused)) return false;

        var reverse = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
        var inputs = EnumerateInputs(form).ToList();
        var index = inputs.FindIndex(control => ReferenceEquals(control, focused));
        if (index < 0) return false;

        var nextIndex = reverse ? index - 1 : index + 1;
        if (nextIndex >= 0 && nextIndex < inputs.Count)
        {
            var next = inputs[nextIndex];
            next.Focus();
            if (next is TextBoxBase textBox) textBox.SelectAll();
            return true;
        }

        if (!reverse && form.AcceptButton is Button button && button.Visible && button.Enabled)
        {
            button.PerformClick();
            return true;
        }

        return false;
    }

    private static Control? FindFocusedControl(Control root)
    {
        Control current = root;
        while (current is ContainerControl container && container.ActiveControl is { } active)
            current = active;
        return current == root ? null : current;
    }

    private static Control? LogicalInput(Control? focused)
    {
        if (focused is null) return null;
        Control? logical = null;
        for (var current = focused; current is not null && current is not Form; current = current.Parent)
        {
            if (current is DataGridView or InvoiceEntryControl or BuyerNameField) return null;
            if (IsInput(current)) logical = current;
        }
        return logical;
    }

    private static IEnumerable<Control> EnumerateInputs(Control parent)
    {
        var children = parent.Controls.Cast<Control>()
            .Where(control => control.Visible && control.Enabled)
            .OrderBy(control => control.TabIndex)
            .ThenBy(control => parent.Controls.GetChildIndex(control));

        foreach (var child in children)
        {
            if (child is DataGridView or InvoiceEntryControl or BuyerNameField) continue;
            if (IsInput(child) && child.TabStop && !PreserveNativeEnter(child))
            {
                yield return child;
                continue;
            }

            if (!child.HasChildren) continue;
            foreach (var nested in EnumerateInputs(child)) yield return nested;
        }
    }

    private static bool IsInput(Control control) => control is TextBoxBase or ComboBox or DateTimePicker or UpDownBase;

    private static bool PreserveNativeEnter(Control control)
    {
        if (control is TextBoxBase { Multiline: true }) return true;
        return control is ComboBox { DroppedDown: true };
    }
}

internal static class UiControls
{
    private const int WmUpdateUiState = 0x0128;
    private static readonly IntPtr HideFocusState = new(0x00010001);
    private static bool enterNavigationInstalled;

    internal static void InstallGlobalEnterNavigation()
    {
        if (enterNavigationInstalled) return;
        Application.AddMessageFilter(new EnterNavigationMessageFilter());
        UiConsistencyManager.Install();
        enterNavigationInstalled = true;
    }

    internal static void HideFocusCue(Control control)
    {
        if (control.IsHandleCreated)
            SendMessage(control.Handle, WmUpdateUiState, HideFocusState, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    public static Label Label(string text, ContentAlignment alignment = ContentAlignment.MiddleLeft) => new()
    {
        Text = text, Dock = DockStyle.Fill, TextAlign = alignment, AutoEllipsis = true, Margin = new Padding(3),
    };

    public static TextBox TextBox(int maximumLength = 32767) => new()
    {
        Dock = DockStyle.Fill, MaxLength = maximumLength, Margin = new Padding(3, 5, 3, 5),
    };

    public const int StandardButtonWidth = 132;
    public const int StandardButtonHeight = 34;

    public static Button StandardButton(string text)
    {
        if (text == "帳戶管理") text = "帳號管理";
        if (IsDangerText(text)) return new ThemedDangerButton(text);
        return new NoFocusCueButton
        {
            Text = text,
            Width = StandardButtonWidth,
            Height = StandardButtonHeight,
            Margin = new Padding(6, 2, 6, 2),
            AutoSize = false,
            UseVisualStyleBackColor = true,
        };
    }

    public static Button DangerButton(string text) => new ThemedDangerButton(text);

    private static bool IsDangerText(string text) => text is "作廢" or "確認作廢" or "確認送出作廢";

    public static Button ImportButton(string text, ImportBrand brand) => new ImportBrandButton(text, brand);

    public static Button PrimaryIssueButton() => new PrimaryActionButton();

    public static void ApplyIssueButtonTheme(Button button, bool production)
    {
        if (button is PrimaryActionButton primary)
        {
            primary.SetEnvironment(production);
            return;
        }

        button.BackColor = production ? Color.FromArgb(3, 155, 229) : Color.FromArgb(25, 135, 84);
    }

    public static bool HasLogicalSize(Control control, int width, int height, int tolerance = 3)
    {
        var dpi = control.DeviceDpi > 0 ? control.DeviceDpi : 96;
        var logicalWidth = control.Width * 96D / dpi;
        var logicalHeight = control.Height * 96D / dpi;
        return Math.Abs(logicalWidth - width) <= tolerance && Math.Abs(logicalHeight - height) <= tolerance;
    }

    public static void SetTextBoxLocked(TextBox field, bool locked)
    {
        field.Enabled = true;
        field.ReadOnly = locked;
        field.TabStop = !locked;
        field.BackColor = locked ? Color.FromArgb(242, 242, 242) : Color.White;
        field.BorderStyle = BorderStyle.FixedSingle;
    }

    public static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToOrderColumns = false, AllowUserToResizeColumns = false, AllowUserToResizeRows = false,
            AutoGenerateColumns = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single, ColumnHeadersHeight = 28,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles = false, GridColor = Color.FromArgb(226, 226, 226), RowHeadersVisible = false,
            ScrollBars = ScrollBars.Vertical, ShowCellToolTips = true,
        };
        grid.RowTemplate.Height = 27;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(246, 246, 246);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(246, 246, 246);
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
        grid.RowsDefaultCellStyle.BackColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 247, 247);
        return grid;
    }

    public static void ReserveVerticalScrollBar(DataGridView grid, int flexibleColumnIndex)
    {
        void LayoutColumns()
        {
            if (flexibleColumnIndex < 0 || flexibleColumnIndex >= grid.Columns.Count) return;
            var fixedWidth = grid.Columns.Cast<DataGridViewColumn>().Where((_, index) => index != flexibleColumnIndex).Sum(column => column.Width);
            var available = grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - fixedWidth - 3;
            grid.Columns[flexibleColumnIndex].Width = Math.Max(grid.Columns[flexibleColumnIndex].MinimumWidth, available);
        }
        grid.Layout += (_, _) => LayoutColumns();
        grid.SizeChanged += (_, _) => LayoutColumns();
    }
}
