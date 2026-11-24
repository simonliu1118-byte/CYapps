namespace CYInvoice.WinForms;

// The native EDIT overlay used by both invoice entry and allowance products.
// Owners choose editable columns and navigation; this component owns edit state.
internal sealed class NativeListCellEditor
{
    private readonly ListView list;
    private bool committing;
    public TextBox? Editor { get; private set; }
    public int RowIndex { get; private set; } = -1;
    public int ColumnIndex { get; private set; } = -1;
    public event Action<ListViewItem, int>? Committed;
    public event Action<int, int, Keys>? NavigationRequested;

    public NativeListCellEditor(ListView list)
    {
        this.list = list;
        list.MouseWheel += (_, _) => Commit(false);
        list.SizeChanged += (_, _) => Commit(false);
        list.Disposed += (_, _) => Commit(true);
    }

    public void Begin(int rowIndex, int columnIndex, string value, int maxLength)
    {
        Commit(false);
        if (rowIndex < 0 || rowIndex >= list.Items.Count) return;
        var bounds = list.Items[rowIndex].SubItems[columnIndex].Bounds;
        if (columnIndex == 0) bounds.Width = list.Columns[0].Width;
        if (bounds.Width <= 8 || bounds.Height <= 4) return;
        var editor = new TextBox {
            Text = value, AutoSize = false, BorderStyle = BorderStyle.FixedSingle,
            Font = list.Font, MaxLength = maxLength,
            TextAlign = list.Columns[columnIndex].TextAlign,
            Bounds = Rectangle.Inflate(bounds, -1, -1),
        };
        RowIndex = rowIndex;
        ColumnIndex = columnIndex;
        Editor = editor;
        editor.KeyDown += (_, e) => HandleKey(e);
        editor.Leave += (_, _) => Commit(false);
        list.Controls.Add(editor);
        editor.BringToFront();
        editor.Focus();
        editor.SelectAll();
    }

    internal void HandleKey(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            Commit(true);
            list.Focus();
            return;
        }
        if (e.KeyCode is not (Keys.Enter or Keys.Tab)) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        var row = RowIndex;
        var column = ColumnIndex;
        Commit(false);
        NavigationRequested?.Invoke(row, column, e.KeyCode);
    }

    public void Commit(bool cancel)
    {
        if (Editor is null || committing) return;
        committing = true;
        var editor = Editor;
        var rowIndex = RowIndex;
        var columnIndex = ColumnIndex;
        Editor = null;
        RowIndex = ColumnIndex = -1;
        try
        {
            if (!cancel && rowIndex >= 0 && rowIndex < list.Items.Count)
            {
                var row = list.Items[rowIndex];
                row.SubItems[columnIndex].Text = editor.Text.Trim();
                Committed?.Invoke(row, columnIndex);
            }
        }
        finally { editor.Dispose(); committing = false; }
    }
}
