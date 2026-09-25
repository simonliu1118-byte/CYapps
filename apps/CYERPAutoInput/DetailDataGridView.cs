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
        if (ColumnCount == 0) return;

        if (CurrentCell is null)
        {
            if (RowCount == 0) Rows.Add();
            var firstEditable = Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => !c.ReadOnly);
            if (firstEditable is null) return;
            CurrentCell = this[firstEditable.Index, 0];
            BeginEdit(false);
            return;
        }

        EndEdit();
        var row = CurrentCell.RowIndex;
        var col = CurrentCell.ColumnIndex;

        // The read-only sequence column must be skipped. EditOnEnter + BeginEdit(false)
        // keeps the next cell's editing control active before the first character arrives;
        // this avoids the first keystroke being consumed once by DataGridView and once by
        // its TextBox editing control.
        for (var step = 0; step < ColumnCount + 1; step++)
        {
            col += delta;
            if (col >= ColumnCount) { col = 0; row++; }
            if (col < 0) { col = ColumnCount - 1; row--; }
            if (row < 0) row = 0;

            row = Math.Min(row, Math.Max(0, RowCount - 1));
            if (Columns[col].ReadOnly) continue;

            CurrentCell = this[col, row];
            BeginEdit(false);
            return;
        }
    }
}
