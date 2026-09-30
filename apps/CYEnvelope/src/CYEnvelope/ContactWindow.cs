using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CYEnvelope;

// Manage the customer database directly: search, add, edit and delete customers, addresses and phones.
// Edits happen on a copy, so leaving without saving really discards them.
public sealed class ContactWindow : Window
{
    private readonly Repository _repository;
    private readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 8), ToolTip = "輸入客戶名稱的一部分" };
    private readonly TextBlock _count = new() { Foreground = DesktopUi.Muted, Margin = new Thickness(0, 0, 0, 8) };
    private readonly ListBox _contacts = new() { MinWidth = 210 };
    private readonly TextBox _name = new() { Margin = new Thickness(0, 4, 0, 12) };
    private readonly DataGrid _addresses = new() { AutoGenerateColumns = false, CanUserAddRows = false, MinHeight = 120 };
    private readonly DataGrid _phones = new() { AutoGenerateColumns = false, CanUserAddRows = false, MinHeight = 100 };
    private Contact? _editing;
    private Dictionary<string, (string Value, string Postal)> _original = [];
    private ObservableCollection<ContactAddress> _addressRows = [];
    private ObservableCollection<ContactPhone> _phoneRows = [];
    private List<Contact> _items = [];
    private bool _dirty, _loading, _switching;

    public ContactWindow(Repository repository)
    {
        _repository = repository;
        Title = "聯絡人資料";
        Width = 1080; Height = 750; MinWidth = 960; MinHeight = 680;
        DesktopUi.Dialog(this);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft JhengHei UI");
        FontSize = 14; Background = System.Windows.Media.Brushes.White;
        var root = new Grid { Margin = new Thickness(18) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        var shell = new DockPanel(); Content = shell;
        var footer = Row(("新增聯絡人", (_, _) => NewContact()), ("刪除聯絡人", (_, _) => DeleteContact()),
            ("關閉", (_, _) => Close()), ("儲存", (_, _) => Save()));
        footer.HorizontalAlignment = HorizontalAlignment.Right; footer.Margin = new Thickness(20, 12, 12, 12);
        ((Button)footer.Children[2]).IsCancel = true;
        ((Button)footer.Children[1]).SetResourceReference(StyleProperty, "DangerButton");
        ((Button)footer.Children[3]).SetResourceReference(StyleProperty, "PrimaryButton");
        DockPanel.SetDock(footer, Dock.Bottom); shell.Children.Add(footer); shell.Children.Add(root);

        var left = new DockPanel();
        Grid.SetColumn(left, 0); root.Children.Add(left);
        var heading = DesktopUi.Heading("聯絡人"); heading.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(heading, Dock.Top); left.Children.Add(heading);
        DockPanel.SetDock(_search, Dock.Top); left.Children.Add(_search);
        DockPanel.SetDock(_count, Dock.Top); left.Children.Add(_count);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(Contact.Name)));
        var summary = new FrameworkElementFactory(typeof(TextBlock));
        summary.SetBinding(TextBlock.TextProperty, new Binding(nameof(Contact.Summary)));
        summary.SetValue(TextBlock.FontSizeProperty, 12.0);
        summary.SetValue(TextBlock.ForegroundProperty, DesktopUi.Muted);
        var item = new FrameworkElementFactory(typeof(StackPanel));
        item.AppendChild(name); item.AppendChild(summary);
        _contacts.ItemTemplate = new DataTemplate { VisualTree = item };
        left.Children.Add(_contacts);
        _search.TextChanged += (_, _) => RefreshContacts(_editing?.Id);
        _contacts.SelectionChanged += (_, _) => ContactPicked();

        var right = new Grid();
        for (var i = 0; i < 8; i++) right.RowDefinitions.Add(new RowDefinition
            { Height = i is 3 or 6 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Grid.SetColumn(right, 2); root.Children.Add(right);
        right.Children.Add(new TextBlock { Text = "收件人姓名" });
        right.Children.Add(_name);
        right.Children.Add(new TextBlock { Text = "地址（可直接修改名稱及備註）", Margin = new Thickness(0, 6, 0, 6) });
        _addresses.Columns.Add(new DataGridTextColumn { Header = "名稱", Binding = new Binding("Label"), Width = 90 });
        _addresses.Columns.Add(new DataGridTextColumn { Header = "地址", Binding = new Binding("Value"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 170 });
        _addresses.Columns.Add(new DataGridTextColumn { Header = "郵遞區號", Binding = new Binding("PostalCode"), Width = 86 });
        _addresses.Columns.Add(new DataGridTextColumn { Header = "備註", Binding = new Binding("Note"), Width = 90 });
        right.Children.Add(_addresses);
        right.Children.Add(Row(("新增地址", (_, _) => _addressRows.Add(new ContactAddress { Label = $"地址{_addressRows.Count + 1}" })),
            ("刪除地址", (_, _) => { if (_addresses.SelectedItem is ContactAddress a && Confirm($"刪除地址「{a.Label}｜{a.Value}」？")) _addressRows.Remove(a); })));
        right.Children.Add(new TextBlock { Text = "電話（含分機與備註）", Margin = new Thickness(0, 10, 0, 6) });
        _phones.Columns.Add(new DataGridTextColumn { Header = "號碼", Binding = new Binding("Number"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 170 });
        _phones.Columns.Add(new DataGridTextColumn { Header = "分機", Binding = new Binding("Extension"), Width = 75 });
        _phones.Columns.Add(new DataGridTextColumn { Header = "備註", Binding = new Binding("Note"), Width = 150 });
        right.Children.Add(_phones);
        right.Children.Add(Row(("新增電話", (_, _) => _phoneRows.Add(new ContactPhone())),
            ("刪除電話", (_, _) => { if (_phones.SelectedItem is ContactPhone p && Confirm($"刪除電話「{p.Display}」？")) _phoneRows.Remove(p); })));
        for (var i = 0; i < right.Children.Count; i++) Grid.SetRow(right.Children[i], i);

        _name.TextChanged += (_, _) => MarkDirty();
        _addresses.CellEditEnding += (_, _) => MarkDirty();
        _phones.CellEditEnding += (_, _) => MarkDirty();
        Closing += (_, e) => { if (!ResolveUnsaved()) e.Cancel = true; };

        RefreshContacts(null);
        if (_items.Count > 0) Load(_items[0]); else NewContact();
    }

    private static StackPanel Row(params (string Name, RoutedEventHandler Action)[] actions)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
        foreach (var (name, action) in actions)
        {
            var button = new Button { Content = name, Margin = new Thickness(0, 0, 8, 0) };
            button.Click += action;
            row.Children.Add(button);
        }
        return row;
    }

    private bool Confirm(string message) => MessageBox.Show(this, message, "確認刪除",
        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void MarkDirty() { if (!_loading) _dirty = true; }

    // Refills the list for the current search text and keeps the edited customer selected when it is shown.
    private void RefreshContacts(string? keepId)
    {
        _items = _search.Text.Trim().Length == 0 ? _repository.Contacts() : _repository.SearchContacts(_search.Text, 200);
        _count.Text = _search.Text.Trim().Length == 0 ? $"共 {_items.Count} 位" : $"符合 {_items.Count} 位";
        _switching = true;
        _contacts.ItemsSource = _items;
        _contacts.SelectedItem = keepId is null ? null : _items.FirstOrDefault(c => c.Id == keepId);
        _switching = false;
    }

    private void ContactPicked()
    {
        if (_switching || _contacts.SelectedItem is not Contact picked || picked.Id == _editing?.Id) return;
        if (!ResolveUnsaved())
        {
            _switching = true;
            _contacts.SelectedItem = _items.FirstOrDefault(c => c.Id == _editing?.Id);
            _switching = false;
            return;
        }
        Load(picked);
    }

    // Unsaved edits: save, discard or stay. Returns true when it is fine to leave the current customer.
    private bool ResolveUnsaved()
    {
        if (!_dirty) return true;
        var answer = MessageBox.Show(this, "有尚未儲存的修改。\n\n是：儲存後繼續\n否：放棄修改\n取消：回到編輯",
            "未儲存的修改", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.Yes) return Save();
        _dirty = false;
        return true;
    }

    private static Contact Clone(Contact contact) =>
        JsonSerializer.Deserialize<Contact>(JsonSerializer.Serialize(contact))!;

    private void Load(Contact stored)
    {
        _loading = true;
        _editing = Clone(stored);
        _original = _editing.Addresses.ToDictionary(a => a.Id, a => (a.Value, a.PostalCode));
        _name.Text = _editing.Name;
        Bind(new ObservableCollection<ContactAddress>(_editing.Addresses), new ObservableCollection<ContactPhone>(_editing.Phones));
        _switching = true;
        _contacts.SelectedItem = _items.FirstOrDefault(c => c.Id == stored.Id);
        _switching = false;
        _dirty = false;
        _loading = false;
    }

    private void Bind(ObservableCollection<ContactAddress> addresses, ObservableCollection<ContactPhone> phones)
    {
        _addressRows = addresses; _phoneRows = phones;
        _addressRows.CollectionChanged += (_, _) => MarkDirty();
        _phoneRows.CollectionChanged += (_, _) => MarkDirty();
        _addresses.ItemsSource = _addressRows;
        _phones.ItemsSource = _phoneRows;
    }

    private void NewContact()
    {
        if (!ResolveUnsaved()) return;
        _loading = true;
        _editing = new Contact();
        _original = [];
        _name.Clear();
        Bind([], []);
        _switching = true;
        _contacts.SelectedItem = null;
        _switching = false;
        _dirty = false;
        _loading = false;
        _name.Focus();
    }

    private void DeleteContact()
    {
        if (_editing is null) return;
        var stored = _repository.GetContact(_editing.Id);
        if (stored is null) { NewContact(); return; } // never saved: nothing to delete
        if (!Confirm($"刪除「{stored.Name}」及其 {stored.Addresses.Count} 組地址、{stored.Phones.Count} 組電話？\n\n刪除後無法在程式內復原（Data\\Backups 的每日備份可還原整個資料庫）。")) return;
        _repository.DeleteContact(stored.Id);
        _dirty = false;
        RefreshContacts(null);
        if (_items.Count > 0) Load(_items[0]); else NewContact();
    }

    private bool Save()
    {
        _addresses.CommitEdit(DataGridEditingUnit.Row, true);
        _phones.CommitEdit(DataGridEditingUnit.Row, true);
        var name = Names.Normalize(_name.Text);
        if (name.Length == 0)
        {
            MessageBox.Show(this, "請輸入收件人姓名。", "資料不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        _editing ??= new Contact();
        if (_repository.FindByName(name).Any(x => x.Id != _editing.Id))
        {
            MessageBox.Show(this, $"已有名為「{name}」的客戶，請改用不同的名稱。", "客戶名稱重複",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _editing.Name = name;
        // Same rules as printing: blank rows dropped, postal code inferred from the address whenever the
        // address text changed (and the code was not edited by hand), phone numbers in the shared format.
        _editing.Addresses = _addressRows.Where(a => !string.IsNullOrWhiteSpace(a.Value)).ToList();
        foreach (var address in _editing.Addresses)
        {
            address.Value = address.Value.Trim();
            var changed = !_original.TryGetValue(address.Id, out var before) || !Postal.SameAddress(before.Value, address.Value);
            var codeEdited = before.Postal != address.PostalCode;
            if (string.IsNullOrWhiteSpace(address.PostalCode) || (changed && !codeEdited))
                address.PostalCode = Postal.Infer(address.Value) ?? "";
        }
        _editing.Phones = _phoneRows.Where(p => !string.IsNullOrWhiteSpace(p.Number)).ToList();
        foreach (var phone in _editing.Phones) phone.Number = PhoneFormatting.Format(phone.Number);
        _repository.SaveContact(_editing);
        _dirty = false;
        RefreshContacts(_editing.Id);
        var stored = _repository.GetContact(_editing.Id)!;
        Load(stored);
        return true;
    }
}
