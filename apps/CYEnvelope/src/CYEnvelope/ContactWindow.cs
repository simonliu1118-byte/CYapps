using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace CYEnvelope;

public sealed class ContactWindow : Window
{
    private readonly Repository _repository;
    private readonly ListBox _contacts = new() { DisplayMemberPath = "Name", MinWidth = 210 };
    private readonly TextBox _name = new() { Margin = new Thickness(0, 4, 0, 12) };
    private readonly DataGrid _addresses = new() { AutoGenerateColumns = false, CanUserAddRows = false, MinHeight = 120 };
    private readonly DataGrid _phones = new() { AutoGenerateColumns = false, CanUserAddRows = false, MinHeight = 100 };
    private Contact? _editing;
    private ObservableCollection<ContactAddress> _addressRows = [];
    private ObservableCollection<ContactPhone> _phoneRows = [];

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
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        var shell = new DockPanel(); Content = shell;
        var footer = Row(("新增聯絡人", (_, _) => NewContact()), ("刪除聯絡人", (_, _) => DeleteContact()), ("儲存", (_, _) => Save()));
        footer.HorizontalAlignment = HorizontalAlignment.Right; footer.Margin = new Thickness(20, 12, 12, 12);
        ((Button)footer.Children[2]).SetResourceReference(StyleProperty, "PrimaryButton");
        DockPanel.SetDock(footer, Dock.Bottom); shell.Children.Add(footer); shell.Children.Add(root);
        var left = new DockPanel();
        Grid.SetColumn(left, 0); root.Children.Add(left);
        var heading = DesktopUi.Heading("聯絡人"); DockPanel.SetDock(heading, Dock.Top); left.Children.Add(heading);
        left.Children.Add(_contacts);
        _contacts.SelectionChanged += (_, _) => LoadSelection();
        var right = new Grid();
        for (var i = 0; i < 8; i++) right.RowDefinitions.Add(new RowDefinition
            { Height = i is 3 or 6 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Grid.SetColumn(right, 2); root.Children.Add(right);
        right.Children.Add(new TextBlock { Text = "收件人姓名" });
        right.Children.Add(_name);
        right.Children.Add(new TextBlock { Text = "地址（可直接修改名稱及備註）", Margin = new Thickness(0, 6, 0, 6) });
        _addresses.Columns.Add(new DataGridTextColumn { Header = "名稱", Binding = new System.Windows.Data.Binding("Label"), Width = 90 });
        _addresses.Columns.Add(new DataGridTextColumn { Header = "地址", Binding = new System.Windows.Data.Binding("Value"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 170 });
        _addresses.Columns.Add(new DataGridTextColumn { Header = "郵遞區號", Binding = new System.Windows.Data.Binding("PostalCode"), Width = 86 });
        _addresses.Columns.Add(new DataGridTextColumn { Header = "備註", Binding = new System.Windows.Data.Binding("Note"), Width = 90 });
        right.Children.Add(_addresses);
        right.Children.Add(Row(("新增地址", (_, _) => _addressRows.Add(new ContactAddress { Label = $"地址{_addressRows.Count + 1}" })),
            ("刪除地址", (_, _) => { if (_addresses.SelectedItem is ContactAddress a && Confirm()) _addressRows.Remove(a); })));
        right.Children.Add(new TextBlock { Text = "電話（含分機與備註）", Margin = new Thickness(0, 10, 0, 6) });
        _phones.Columns.Add(new DataGridTextColumn { Header = "號碼", Binding = new System.Windows.Data.Binding("Number"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 170 });
        _phones.Columns.Add(new DataGridTextColumn { Header = "分機", Binding = new System.Windows.Data.Binding("Extension"), Width = 75 });
        _phones.Columns.Add(new DataGridTextColumn { Header = "備註", Binding = new System.Windows.Data.Binding("Note"), Width = 150 });
        right.Children.Add(_phones);
        right.Children.Add(Row(("新增電話", (_, _) => _phoneRows.Add(new ContactPhone())),
            ("刪除電話", (_, _) => { if (_phones.SelectedItem is ContactPhone p && Confirm()) _phoneRows.Remove(p); })));
        for (var i = 0; i < right.Children.Count; i++) Grid.SetRow(right.Children[i], i);
        RefreshContacts();
        if (_contacts.Items.Count > 0) _contacts.SelectedIndex = 0;
        else NewContact();
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
    private bool Confirm() => MessageBox.Show(this, "確定刪除所選資料？", "確認刪除",
        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    private void RefreshContacts() => _contacts.ItemsSource = _repository.Contacts();
    private void LoadSelection()
    {
        if (_contacts.SelectedItem is not Contact selected) return;
        _editing = selected;
        _name.Text = selected.Name;
        _addressRows = new ObservableCollection<ContactAddress>(selected.Addresses);
        _phoneRows = new ObservableCollection<ContactPhone>(selected.Phones);
        _addresses.ItemsSource = _addressRows;
        _phones.ItemsSource = _phoneRows;
    }
    private void NewContact()
    {
        _contacts.SelectedIndex = -1;
        _editing = new Contact();
        _name.Clear();
        _addressRows = [];
        _phoneRows = [];
        _addresses.ItemsSource = _addressRows;
        _phones.ItemsSource = _phoneRows;
        _name.Focus();
    }
    private void DeleteContact()
    {
        if (_editing is null || !Confirm()) return;
        _repository.DeleteContact(_editing.Id);
        NewContact();
        RefreshContacts();
    }
    private void Save()
    {
        _addresses.CommitEdit(DataGridEditingUnit.Row, true);
        _phones.CommitEdit(DataGridEditingUnit.Row, true);
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            MessageBox.Show(this, "請輸入收件人姓名。", "資料不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _editing ??= new Contact();
        _editing.Name = _name.Text.Trim();
        // Same rules as the main window: blank rows are dropped, missing postal codes are
        // inferred from the address, and numbers use the shared phone format.
        _editing.Addresses = _addressRows.Where(a => !string.IsNullOrWhiteSpace(a.Value)).ToList();
        foreach (var address in _editing.Addresses)
        {
            address.Value = address.Value.Trim();
            if (string.IsNullOrWhiteSpace(address.PostalCode)) address.PostalCode = Postal.Infer(address.Value) ?? "";
        }
        _editing.Phones = _phoneRows.Where(p => !string.IsNullOrWhiteSpace(p.Number)).ToList();
        foreach (var phone in _editing.Phones) phone.Number = PhoneFormatting.Format(phone.Number);
        _repository.SaveContact(_editing);
        var id = _editing.Id;
        RefreshContacts();
        _contacts.SelectedItem = ((IEnumerable<Contact>)_contacts.ItemsSource).First(c => c.Id == id);
    }
}
