namespace CYERPAutoInput;

internal sealed class DetailDataGridView : DataGridView
{
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.Enter or Keys.Tab)
        {
            var reverse = (keyData & Keys.Shift) == Keys.Shift;
            MoveCell(reverse ? -1 : 1);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void MoveCell(int delta)
    {
        if (CurrentCell is null)
        {
            if (RowCount == 0) Rows.Add();
            CurrentCell = this[0, 0];
            BeginEdit(true);
            return;
        }

        EndEdit();
        var row = CurrentCell.RowIndex;
        var col = CurrentCell.ColumnIndex + delta;
        if (col >= ColumnCount) { col = 0; row++; }
        if (col < 0) { col = ColumnCount - 1; row--; }
        if (row < 0) row = 0;

        // DataGridView keeps a special new-row placeholder when AllowUserToAddRows is true.
        // Enter/Tab into that row materializes it automatically when editing starts.
        row = Math.Min(row, Math.Max(0, RowCount - 1));
        CurrentCell = this[col, row];
        BeginEdit(true);
    }
}
