namespace CYERPAutoInput;

internal static class ErpLayoutResolver
{
    private static readonly Dictionary<string, (int Row, int Col)> Positions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["order_type"] = (0, 0), ["order_date"] = (0, 2), ["customer_code"] = (1, 1),
        ["dept_code"] = (0, 0), ["currency"] = (0, 1), ["trade_note"] = (0, 2),
        ["salesperson"] = (1, 0), ["exchange_rate"] = (1, 1), ["transmit_count"] = (1, 2), ["invoice_print"] = (1, 3),
        ["receipt_salesperson"] = (2, 0), ["employee_code"] = (2, 1), ["payment_terms"] = (2, 2),
        ["ship_name"] = (0, 0), ["ship_addr1"] = (1, 0), ["ship_addr2"] = (2, 0),
        ["contact"] = (3, 0), ["receiver"] = (3, 1),
        ["tel"] = (4, 0), ["fax"] = (4, 1), ["mobile"] = (4, 2),
        ["appoint_date"] = (5, 0), ["delivery_slot"] = (5, 1), ["freight_type"] = (5, 2),
        ["cod"] = (6, 0), ["freight_fee"] = (6, 1), ["freight_file"] = (6, 2),
        ["inv_date"] = (0, 0), ["inv_time"] = (0, 1), ["inv_copies"] = (0, 2),
        ["inv_no"] = (1, 0), ["tax_type"] = (1, 1), ["customs"] = (1, 2),
        ["tax_id"] = (2, 0), ["tax_rate"] = (2, 1), ["report_month"] = (2, 2), ["voided"] = (2, 3),
        ["inv_name"] = (3, 0), ["card4"] = (3, 1),
        ["inv_addr1"] = (4, 0), ["inv_addr2"] = (5, 0), ["email"] = (6, 0)
    };

    private static readonly Dictionary<string, int[]> ExpectedShapes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["交易資料"] = [3, 4, 3],
        ["送貨資料"] = [1, 1, 1, 2, 3, 3, 3],
        ["發票資料(一)"] = [3, 3, 4, 2, 1, 1, 1]
    };

    public static WindowControl ResolveHeader(nint root, string key)
    {
        if (!Positions.TryGetValue(key, out var pos)) throw new InvalidOperationException($"Unknown field position: {key}");
        if (!NativeMethods.GetWindowRect(root, out var rr)) throw new InvalidOperationException("ERP root rectangle unavailable.");
        var candidates = Win32Automation.EnumerateChildren(root)
            .Where(c => c.Visible && c.ClassName.Equals("TDBEdit", StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Rect.Top - rr.Top is >= 140 and <= 245)
            .ToList();
        var rows = BuildRows(candidates, 9);
        if (rows.Count != 2 || rows.Any(r => r.Count != 3))
            throw new InvalidOperationException($"ERP 表頭結構不符預期 [3,3]，實際 rows={rows.Count}。");
        if (pos.Row >= rows.Count || pos.Col >= rows[pos.Row].Count) throw new InvalidOperationException($"ERP 表頭欄位位置不存在 key={key}。");
        return rows[pos.Row][pos.Col];
    }

    public static WindowControl ResolveTabField(nint sheet, string group, string key)
    {
        if (!Positions.TryGetValue(key, out var pos)) throw new InvalidOperationException($"Unknown field position: {key}");
        var controls = Win32Automation.EnumerateChildren(sheet)
            .Where(c => c.Parent == sheet)
            .Where(c =>
            {
                var u = c.ClassName.ToUpperInvariant();
                return u.Contains("TDBEDIT") || u.Contains("TCXDBIMAGECOMBOBOX") || u.Contains("TFDBCHECKBOX");
            })
            .ToList();
        var rows = BuildRows(controls, 9);
        if (!ExpectedShapes.TryGetValue(group, out var expected)) throw new InvalidOperationException($"Unknown ERP tab group: {group}");
        if (rows.Count != expected.Length || rows.Where((row, i) => row.Count != expected[i]).Any())
            throw new InvalidOperationException($"ERP {group} 欄位結構不符預期；為避免填錯欄位已停止。");
        if (pos.Row >= rows.Count || pos.Col >= rows[pos.Row].Count) throw new InvalidOperationException($"ERP {group} 欄位位置不存在 key={key}。");
        return rows[pos.Row][pos.Col];
    }

    public static nint FindTabSheet(nint root, string group)
    {
        var target = Win32Automation.NormalizeLabel(group);
        return Win32Automation.EnumerateChildren(root)
            .Where(c => c.ClassName.Equals("TcxTabSheet", StringComparison.OrdinalIgnoreCase))
            .Where(c => Win32Automation.NormalizeLabel(c.Text) == target)
            .Select(c => c.Handle)
            .FirstOrDefault();
    }

    private static List<List<WindowControl>> BuildRows(List<WindowControl> controls, int tolerance)
    {
        controls.Sort((a, b) =>
        {
            var ay = (a.Rect.Top + a.Rect.Bottom) / 2;
            var by = (b.Rect.Top + b.Rect.Bottom) / 2;
            var cmp = ay.CompareTo(by);
            return cmp != 0 ? cmp : a.Rect.Left.CompareTo(b.Rect.Left);
        });
        var rows = new List<List<WindowControl>>();
        var centers = new List<int>();
        foreach (var c in controls)
        {
            var cy = (c.Rect.Top + c.Rect.Bottom) / 2;
            var index = -1;
            for (var i = 0; i < centers.Count; i++)
            {
                if (Math.Abs(cy - centers[i]) <= tolerance) { index = i; break; }
            }
            if (index < 0)
            {
                rows.Add([c]);
                centers.Add(cy);
            }
            else rows[index].Add(c);
        }
        foreach (var row in rows) row.Sort((a, b) => a.Rect.Left.CompareTo(b.Rect.Left));
        return rows;
    }
}
