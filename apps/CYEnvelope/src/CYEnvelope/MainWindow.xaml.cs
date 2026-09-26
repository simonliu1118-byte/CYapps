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
    private TextBox? _directEditor;
    private ListBox? _directSuggestions;
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
        ApplyEntryMode();
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
        UpdatePostalFromAddress();
    }
    private void UpdatePostalFromAddress()
    {
        var code = Postal.Infer(AddressBox.Text);
        if (code is not null) PostalBox.Text = code; // unknown: preserve the user's previous code
    }
    private void PhoneLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        FormatPhone();
    }
    private void FormatPhone()
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
        RebuildDirectTargets();
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
        var dialog = new Window
        {
            Title = "設定", Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 360, Height = 210, ResizeMode = ResizeMode.NoResize,
            FontFamily = FontFamily, FontSize = 15
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "輸入方式", FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12) });
        var direct = new CheckBox { Content = "直接點選信封欄位輸入", IsChecked = _settings.DirectEntry };
        panel.Children.Add(direct);
        panel.Children.Add(new TextBlock { Text = "關閉後使用左側資料欄輸入。",
            Foreground = Brushes.DimGray, Margin = new Thickness(0, 10, 0, 14) });
        var save = new Button { Content = "儲存", Width = 90, HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) => { _settings.DirectEntry = direct.IsChecked == true; dialog.DialogResult = true; };
        panel.Children.Add(save);
        if (dialog.ShowDialog() == true)
        {
            _repository.SaveSettings(_settings);
            ApplyEntryMode();
        }
    }

    private void ApplyEntryMode()
    {
        EntryPanel.Visibility = _settings.DirectEntry ? Visibility.Collapsed : Visibility.Visible;
        EntryColumn.Width = new GridLength(_settings.DirectEntry ? 0 : 370);
        EntryGap.Width = new GridLength(_settings.DirectEntry ? 0 : 16);
        DirectLayer.Visibility = _settings.DirectEntry ? Visibility.Visible : Visibility.Collapsed;
        RebuildDirectTargets();
    }
    private void RebuildDirectTargets()
    {
        DirectLayer.Children.Clear();
        _directEditor = null;
        _directSuggestions = null;
        DirectLayer.Width = _format.WidthMm * EnvelopeRenderer.DipPerMm;
        DirectLayer.Height = _format.HeightMm * EnvelopeRenderer.DipPerMm;
        foreach (var (field, rect) in new[]
        {
            ("收件人", _format.Recipient.Rect), ("地址", _format.Address.Rect),
            ("電話", _format.Phone.Rect), ("郵遞區號", _format.PostalCode.Rect),
            ("方框文字", _format.Frame)
        })
        {
            var target = new Border
            {
                Width = Math.Max(20, rect.Width * EnvelopeRenderer.DipPerMm),
                Height = Math.Max(20, rect.Height * EnvelopeRenderer.DipPerMm),
                Background = Brushes.Transparent,
                ToolTip = $"點選輸入{field}",
                Tag = field
            };
            target.MouseEnter += (_, _) => target.BorderBrush = Brushes.SteelBlue;
            target.MouseLeave += (_, _) => target.BorderBrush = Brushes.Transparent;
            target.BorderThickness = new Thickness(1);
            target.MouseLeftButtonDown += DirectTargetClick;
            Canvas.SetLeft(target, rect.X * EnvelopeRenderer.DipPerMm);
            Canvas.SetTop(target, rect.Y * EnvelopeRenderer.DipPerMm);
            DirectLayer.Children.Add(target);
        }
        foreach (var item in _format.Delivery)
        {
            var target = new Button
            {
                Width = 16, Height = 16, Opacity = .15, Padding = new Thickness(0),
                ToolTip = $"勾選／取消{item.Label}", Tag = item.Id
            };
            target.Click += (_, _) =>
            {
                if (_delivery.TryGetValue((string)target.Tag, out var check))
                    check.IsChecked = check.IsChecked != true;
            };
            Canvas.SetLeft(target, item.X * EnvelopeRenderer.DipPerMm);
            Canvas.SetTop(target, item.Y * EnvelopeRenderer.DipPerMm);
            DirectLayer.Children.Add(target);
        }
    }
    private void DirectTargetClick(object sender, MouseButtonEventArgs e)
    {
        var target = (Border)sender;
        var field = (string)target.Tag;
        if (_directEditor is not null) DirectLayer.Children.Remove(_directEditor);
        if (_directSuggestions is not null) DirectLayer.Children.Remove(_directSuggestions);
        _directSuggestions = null;
        var source = field switch
        {
            "收件人" => RecipientBox,
            "地址" => AddressBox,
            "電話" => PhoneBox,
            "郵遞區號" => PostalBox,
            _ => null
        };
        if (source is null)
        {
            ShowFrameBox.IsChecked = ShowFrameBox.IsChecked != true;
            return;
        }
        var editor = new TextBox
        {
            Text = source.Text, Width = Math.Min(200, DirectLayer.Width - 12),
            FontSize = 16, Background = Brushes.White, BorderBrush = Brushes.SteelBlue,
            BorderThickness = new Thickness(2), Padding = new Thickness(4),
            ToolTip = $"輸入{field}，按 Enter 完成"
        };
        var original = source.Text;
        _directEditor = editor;
        void ChooseSuggestion()
        {
            if (_directSuggestions?.SelectedItem is not Contact chosen) return;
            ChooseContact(chosen);
            if (_directEditor is not null) DirectLayer.Children.Remove(_directEditor);
            DirectLayer.Children.Remove(_directSuggestions);
            _directEditor = null; _directSuggestions = null;
        }
        if (field == "收件人")
        {
            var suggestions = new ListBox
            {
                Width = editor.Width, MaxHeight = 105,
                DisplayMemberPath = "Name", Background = Brushes.White,
                BorderBrush = Brushes.SteelBlue
            };
            _directSuggestions = suggestions;
            suggestions.MouseDoubleClick += (_, _) => ChooseSuggestion();
            suggestions.KeyDown += (_, args) =>
            {
                if (args.Key != Key.Enter) return;
                ChooseSuggestion();
                args.Handled = true;
            };
            DirectLayer.Children.Add(suggestions);
            Canvas.SetLeft(suggestions, Math.Clamp(Canvas.GetLeft(target), 2,
                Math.Max(2, DirectLayer.Width - editor.Width - 2)));
            Canvas.SetTop(suggestions, Math.Clamp(Canvas.GetTop(target) + 40, 2,
                Math.Max(2, DirectLayer.Height - 110)));
        }
        editor.TextChanged += (_, _) =>
        {
            source.Text = editor.Text;
            if (_directSuggestions is not null)
            {
                var query = editor.Text.Trim();
                var matches = query.Length == 0 ? [] : _repository.Contacts()
                    .Where(c => c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    .Take(10).ToList();
                _directSuggestions.ItemsSource = matches;
                _directSuggestions.Visibility = matches.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            Refresh();
        };
        editor.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Down && _directSuggestions?.Items.Count > 0)
            {
                _directSuggestions.SelectedIndex = 0;
                _directSuggestions.Focus();
                args.Handled = true;
                return;
            }
            if (args.Key != Key.Enter && args.Key != Key.Escape) return;
            if (args.Key == Key.Enter && _directSuggestions?.Items.Count == 1)
            {
                _directSuggestions.SelectedIndex = 0;
                ChooseSuggestion();
                args.Handled = true;
                return;
            }
            if (args.Key == Key.Escape) source.Text = original;
            DirectLayer.Children.Remove(editor);
            if (_directSuggestions is not null) DirectLayer.Children.Remove(_directSuggestions);
            _directSuggestions = null;
            _directEditor = null;
            args.Handled = true;
        };
        editor.LostKeyboardFocus += (_, _) =>
        {
            if (field == "地址") UpdatePostalFromAddress();
            if (field == "電話") FormatPhone();
            Dispatcher.BeginInvoke(() =>
            {
                DirectLayer.Children.Remove(editor);
                if (ReferenceEquals(_directEditor, editor)) _directEditor = null;
                if (_directSuggestions is not null && !_directSuggestions.IsKeyboardFocusWithin)
                {
                    DirectLayer.Children.Remove(_directSuggestions);
                    _directSuggestions = null;
                }
            });
        };
        DirectLayer.Children.Add(editor);
        Canvas.SetLeft(editor, Math.Clamp(Canvas.GetLeft(target), 2, Math.Max(2, DirectLayer.Width - editor.Width - 2)));
        Canvas.SetTop(editor, Math.Clamp(Canvas.GetTop(target), 2, Math.Max(2, DirectLayer.Height - 40)));
        editor.Focus();
        editor.SelectAll();
        e.Handled = true;
    }
}
