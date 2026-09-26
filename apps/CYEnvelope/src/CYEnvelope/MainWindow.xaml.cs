using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Printing;

namespace CYEnvelope;

public partial class MainWindow : Window
{
    private readonly Repository _repository;
    private AppSettings _settings;
    private EnvelopeFormat _format;
    private Contact? _selectedContact;
    private ContactAddress? _selectedAddress;
    private ContactPhone? _selectedPhone;
    private readonly Dictionary<string, CheckBox> _delivery = [];
    private bool _loading;

    public MainWindow()
    {
        InitializeComponent();
        var data = Path.Combine(AppContext.BaseDirectory, "Data", "CYEnvelope.db");
        _repository = new Repository(data);
        _settings = _repository.Settings();
        var formats = _repository.Formats();
        _format = formats.FirstOrDefault(f => f.Id == _settings.SelectedFormatId)
                  ?? formats.FirstOrDefault(f => f.IsDefault) ?? formats[0];
        var icon = new BitmapImage(new Uri("pack://application:,,,/ENV.ico"));
        Icon = icon;
        var version = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "VERSION")).Trim();
        var build = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BUILD")).Trim();
        Title = $"CYEnvelope V{version}" + (build != "0" ? $" Build {build}" : "");
        ShowFrameBox.IsChecked = true;
        RebuildOptions();
        Refresh();
    }

    private void RebuildOptions()
    {
        _loading = true;
        DeliveryPanel.Children.Clear();
        _delivery.Clear();
        foreach (var option in _format.Delivery)
        {
            var item = new CheckBox { Content = option.Label, Margin = new Thickness(0, 0, 12, 9),
                                      MinWidth = 94, Tag = option.Id };
            item.Checked += InputChanged;
            item.Unchecked += InputChanged;
            DeliveryPanel.Children.Add(item);
            _delivery[option.Id] = item;
        }
        FrameChoice.ItemsSource = _settings.FrameTexts.ToArray();
        FrameChoice.SelectedIndex = _settings.FrameTexts.Count > 0 ? 0 : -1;
        _loading = false;
    }

    private PrintData CurrentData() => new()
    {
        Recipient = RecipientBox.Text.Trim(),
        Address = AddressBox.Text.Trim(),
        PostalCode = PostalBox.Text.Trim(),
        Phone = PhoneBox.Text.Trim(),
        DeliveryIds = _delivery.Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToList(),
        ShowFrame = ShowFrameBox.IsChecked == true,
        FrameText = FrameChoice.SelectedItem as string ?? ""
    };

    private void Refresh()
    {
        if (_loading) return;
        FormatName.Text = $"{_format.Name}　{_format.WidthMm:0.#} × {_format.HeightMm:0.#} mm";
        PreviewHost.Show(EnvelopeRenderer.Draw(_format, CurrentData(), true),
            _format.WidthMm * EnvelopeRenderer.DipPerMm, _format.HeightMm * EnvelopeRenderer.DipPerMm);
    }

    private void InputChanged(object sender, RoutedEventArgs e) => Refresh();

    private void RecipientChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        // Independent ListBox: filtering never replaces TextBox.Text or touches SelectionStart.
        _selectedContact = null;
        _selectedAddress = null;
        _selectedPhone = null;
        _loading = true;
        AddressChoice.ItemsSource = null;
        PhoneChoice.ItemsSource = null;
        AddressBox.Clear();
        PhoneBox.Clear();
        PostalBox.Clear();
        foreach (var box in _delivery.Values) box.IsChecked = false;
        _loading = false;
        var query = RecipientBox.Text.Trim();
        var matches = query.Length == 0 ? [] : _repository.Contacts()
            .Where(c => c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Take(12).ToList();
        Suggestions.ItemsSource = matches;
        Suggestions.Visibility = matches.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Refresh();
    }

    private void RecipientKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && Suggestions.Visibility == Visibility.Visible)
        {
            Suggestions.SelectedIndex = 0;
            Suggestions.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (Suggestions.Visibility == Visibility.Visible && Suggestions.Items.Count == 1)
                ChooseContact((Contact)Suggestions.Items[0]);
            AddressBox.Focus();
            e.Handled = true;
        }
    }

    private void SuggestionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Suggestions.SelectedItem is Contact contact)
        {
            ChooseContact(contact);
            AddressBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { Suggestions.Visibility = Visibility.Collapsed; RecipientBox.Focus(); }
    }
    private void SuggestionChosen(object sender, MouseButtonEventArgs e)
    {
        if (Suggestions.SelectedItem is Contact contact) ChooseContact(contact);
    }

    private void ChooseContact(Contact contact)
    {
        _loading = true;
        _selectedContact = contact;
        RecipientBox.Text = contact.Name;
        RecipientBox.CaretIndex = RecipientBox.Text.Length;
        Suggestions.Visibility = Visibility.Collapsed;
        AddressChoice.ItemsSource = contact.Addresses;
        var address = contact.Addresses.FirstOrDefault(x => x.Id == contact.LastAddressId) ?? contact.Addresses.FirstOrDefault();
        AddressChoice.SelectedItem = address;
        ApplyAddress(address);
        foreach (var (id, checkbox) in _delivery)
            checkbox.IsChecked = contact.LastDeliveryIds.Contains(id);
        _loading = false;
        Refresh();
    }

    private void ApplyAddress(ContactAddress? address)
    {
        _selectedAddress = address;
        AddressBox.Text = address?.Value ?? "";
        PostalBox.Text = address?.PostalCode ?? "";
        PhoneChoice.ItemsSource = _selectedContact?.Phones;
        var phone = _selectedContact?.Phones.FirstOrDefault(x => x.Id == address?.LastPhoneId)
                    ?? _selectedContact?.Phones.FirstOrDefault();
        PhoneChoice.SelectedItem = phone;
        ApplyPhone(phone);
    }
    private void ApplyPhone(ContactPhone? phone)
    {
        _selectedPhone = phone;
        PhoneBox.Text = phone?.Display ?? "";
    }
    private void AddressChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _loading = true;
        ApplyAddress(AddressChoice.SelectedItem as ContactAddress);
        _loading = false;
        Refresh();
    }
    private void PhoneChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _loading = true;
        ApplyPhone(PhoneChoice.SelectedItem as ContactPhone);
        _loading = false;
        Refresh();
    }
    private void InputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        PhoneBox.Focus();
        e.Handled = true;
    }
    private void AddressLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var code = Postal.Infer(AddressBox.Text);
        if (code is not null) PostalBox.Text = code; // unknown: preserve the user's previous code
    }
    private void PhoneLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (PhoneBox.Text.Length == 0) return;
        var formatted = PhoneFormatting.Format(PhoneBox.Text);
        if (formatted != PhoneBox.Text) PhoneBox.Text = formatted;
    }

    private void SaveBeforePrinting(PrintData data)
    {
        var contact = _selectedContact;
        if (contact is null)
        {
            var matches = _repository.Contacts().Where(x => x.Name == data.Recipient).ToList();
            contact = matches.Count == 1 ? matches[0] : new Contact { Name = data.Recipient };
        }
        contact.Name = data.Recipient;
        var address = _selectedAddress;
        if (address is not null && address.Value != data.Address)
        {
            var choice = MessageBox.Show("地址已變更。選「是」覆蓋原地址，選「否」另存為新地址。",
                "保存地址", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) throw new OperationCanceledException();
            if (choice == MessageBoxResult.No) address = null;
        }
        address ??= contact.Addresses.FirstOrDefault(x => x.Value == data.Address);
        if (address is null)
        {
            address = new ContactAddress { Label = $"地址{contact.Addresses.Count + 1}" };
            contact.Addresses.Add(address);
        }
        address.Value = data.Address;
        address.PostalCode = data.PostalCode;
        contact.LastAddressId = address.Id;
        if (data.Phone.Length > 0)
        {
            var number = PhoneFormatting.Format(data.Phone);
            var phone = _selectedPhone;
            if (phone is not null && phone.Display != number)
            {
                var choice = MessageBox.Show("電話已變更。選「是」覆蓋原電話，選「否」另存新電話。",
                    "保存電話", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel) throw new OperationCanceledException();
                if (choice == MessageBoxResult.No) phone = null;
            }
            phone ??= contact.Phones.FirstOrDefault(x => x.Display == number);
            if (phone is null) { phone = new ContactPhone(); contact.Phones.Add(phone); }
            var parts = number.Split(" #", 2);
            phone.Number = parts[0];
            phone.Extension = parts.Length == 2 ? parts[1] : "";
            address.LastPhoneId = phone.Id;
        }
        contact.LastDeliveryIds = data.DeliveryIds;
        _repository.SaveContact(contact);
        _selectedContact = contact;
        _selectedAddress = address;
    }

    private void PrintClick(object sender, RoutedEventArgs e)
    {
        var data = CurrentData();
        if (data.Recipient.Length == 0 || data.Address.Length == 0)
        {
            MessageBox.Show("請輸入收件人及地址。", "資料不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { SaveBeforePrinting(data); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "保存失敗"); return; }
        try
        {
            var dialog = CreatePrintDialog();
            if (dialog.ShowDialog() != true) return;
            _settings.PrinterName = dialog.PrintQueue.FullName;
            _repository.SaveSettings(_settings);
            dialog.PrintTicket.PageOrientation = _format.Landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
            dialog.PrintTicket.PageMediaSize = new PageMediaSize(_format.WidthMm * EnvelopeRenderer.DipPerMm,
                _format.HeightMm * EnvelopeRenderer.DipPerMm);
            dialog.PrintVisual(EnvelopeRenderer.Draw(_format, data, false), $"CYEnvelope - {data.Recipient}");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "列印失敗；聯絡人資料已保存"); }
    }
    private void PrinterClick(object sender, RoutedEventArgs e)
    {
        var dialog = CreatePrintDialog();
        if (dialog.ShowDialog() == true)
        {
            _settings.PrinterName = dialog.PrintQueue.FullName;
            _repository.SaveSettings(_settings);
        }
    }
    private PrintDialog CreatePrintDialog()
    {
        var dialog = new PrintDialog();
        if (_settings.PrinterName.Length == 0) return dialog;
        try
        {
            using var server = new LocalPrintServer();
            dialog.PrintQueue = server.GetPrintQueues().FirstOrDefault(
                q => q.FullName == _settings.PrinterName) ?? dialog.PrintQueue;
        }
        catch (PrintSystemException) { } // removed/offline printer: native dialog chooses a valid printer
        return dialog;
    }
    private void ClearClick(object sender, RoutedEventArgs e)
    {
        _loading = true;
        _selectedContact = null; _selectedAddress = null; _selectedPhone = null;
        RecipientBox.Clear(); AddressBox.Clear(); PhoneBox.Clear(); PostalBox.Clear();
        AddressChoice.ItemsSource = null; PhoneChoice.ItemsSource = null;
        Suggestions.Visibility = Visibility.Collapsed;
        foreach (var item in _delivery.Values) item.IsChecked = false;
        ShowFrameBox.IsChecked = true;
        FrameChoice.SelectedIndex = FrameChoice.Items.Count > 0 ? 0 : -1;
        _loading = false;
        Refresh();
        RecipientBox.Focus();
    }
    private void ContactsClick(object sender, RoutedEventArgs e) =>
        new ContactWindow(_repository) { Owner = this }.ShowDialog();
    private void FormatClick(object sender, RoutedEventArgs e)
    {
        var window = new FormatWindow(_repository, _format) { Owner = this };
        if (window.ShowDialog() != true) return;
        _format = window.SelectedFormat;
        _settings.SelectedFormatId = _format.Id;
        _repository.SaveSettings(_settings);
        RebuildOptions();
        Refresh();
    }
    private void FrameClick(object sender, RoutedEventArgs e)
    {
        var window = new FrameWindow(_settings) { Owner = this };
        if (window.ShowDialog() != true) return;
        _repository.SaveSettings(_settings);
        RebuildOptions();
        Refresh();
    }
    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show($"目前格式：{_format.Name}\n印表機：{(_settings.PrinterName.Length == 0 ? "尚未選擇" : _settings.PrinterName)}",
            "設定", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
