using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    // Address text the postal code was derived from or confirmed for; null means typed before any address.
    private string? _postalAddress;
    private bool _settingPostal;
    // Set when an unresolvable address cleared the previous code; printing stops once to confirm.
    private bool _postalCleared;
    private const string ReadyStatus = "請核對收件資料，再列印至預印信封。";

    public MainWindow()
    {
        InitializeComponent();
        var programDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var appRoot = Path.GetFileName(programDirectory)
            .Equals("Runtime", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(programDirectory)!
            : programDirectory;
        var data = Path.Combine(appRoot, "Data", "CYEnvelope.db");
        _repository = new Repository(data);
        _settings = _repository.Settings();
        var formats = _repository.Formats();
        _format = formats.FirstOrDefault(f => f.Id == _settings.SelectedFormatId)
                  ?? formats.FirstOrDefault(f => f.IsDefault) ?? formats[0];
        var version = File.ReadAllText(Path.Combine(appRoot, "VERSION")).Trim();
        var build = File.ReadAllText(Path.Combine(appRoot, "BUILD")).Trim();
        Title = $"CYEnvelope V{version}" + (build != "0" ? $" Build {build}" : "");
        ShowFrameBox.IsChecked = true;
        RebuildOptions();
        ApplyEntryMode();
        Refresh();
    }

    // Rebuilds format-dependent options while keeping the envelope currently being prepared.
    private void RebuildOptions()
    {
        var checkedIds = _delivery.Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToHashSet();
        var frameText = FrameChoice.SelectedItem as string;
        _loading = true;
        DeliveryPanel.Children.Clear();
        _delivery.Clear();
        foreach (var option in _format.Delivery)
        {
            var item = new CheckBox { Content = option.Label, Margin = new Thickness(0, 0, 8, 9),
                                      MinWidth = 76, Tag = option.Id, IsChecked = checkedIds.Contains(option.Id) };
            item.Checked += InputChanged;
            item.Unchecked += InputChanged;
            DeliveryPanel.Children.Add(item);
            _delivery[option.Id] = item;
        }
        FrameChoice.ItemsSource = _settings.FrameTexts.ToArray();
        var frameIndex = frameText is null ? -1 : _settings.FrameTexts.IndexOf(frameText);
        FrameChoice.SelectedIndex = frameIndex >= 0 ? frameIndex : _settings.FrameTexts.Count > 0 ? 0 : -1;
        _loading = false;
    }

    private void Status(string text) => StatusText.Text = text;

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
        // Editing the name only detaches the chosen contact; typed address/phone stay (清空 resets all).
        if (_selectedContact is not null && RecipientBox.Text.Trim() != _selectedContact.Name) Detach();
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

    private void Detach()
    {
        _selectedContact = null;
        _selectedAddress = null;
        _selectedPhone = null;
        BindChoices();
    }

    // Saved-address/phone lists always show the selected contact's current objects.
    private void BindChoices()
    {
        var loading = _loading;
        _loading = true;
        AddressChoice.ItemsSource = _selectedContact?.Addresses;
        AddressChoice.SelectedItem = _selectedAddress;
        PhoneChoice.ItemsSource = _selectedContact?.Phones;
        PhoneChoice.SelectedItem = _selectedPhone;
        _loading = loading;
    }

    private void SetPostal(string code, string address)
    {
        _settingPostal = true;
        PostalBox.Text = code;
        _settingPostal = false;
        _postalAddress = address;
    }

    private void PostalChanged(object sender, TextChangedEventArgs e)
    {
        if (!_settingPostal)
        {
            _postalAddress = AddressBox.Text.Trim().Length == 0 ? null : AddressBox.Text;
            _postalCleared = false;
        }
        Refresh();
    }

    private void ApplyAddress(ContactAddress? address)
    {
        _selectedAddress = address;
        AddressBox.Text = address?.Value ?? "";
        SetPostal(address?.PostalCode ?? "", AddressBox.Text);
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
    // Re-derive the postal code whenever the address it belongs to changed. An address that
    // cannot be resolved must not keep the previous address's code (no guessed codes).
    private void UpdatePostalFromAddress()
    {
        var address = AddressBox.Text;
        if (address.Trim().Length == 0) return;
        if (_postalAddress is not null && Postal.SameAddress(_postalAddress, address) && PostalBox.Text.Length > 0)
            return;
        var code = Postal.Infer(address);
        if (code is not null)
        {
            SetPostal(code, address);
            _postalCleared = false;
            Status(ReadyStatus);
        }
        else if (_postalAddress is null && PostalBox.Text.Length > 0)
        {
            _postalAddress = address; // typed before the address: keep the user's code for it
        }
        else
        {
            if (PostalBox.Text.Length > 0)
            {
                _postalCleared = true;
                Status("無法由地址判斷郵遞區號，已清除原區號；請手動輸入。");
            }
            SetPostal("", address);
        }
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
        // Always merge into the stored record: the contacts window may have edited or deleted it.
        var contacts = _repository.Contacts();
        var contact = _selectedContact is null ? null : contacts.FirstOrDefault(x => x.Id == _selectedContact.Id);
        if (contact is null)
        {
            var matches = contacts.Where(x => x.Name == data.Recipient).ToList();
            contact = matches.Count == 1 ? matches[0] : new Contact { Name = data.Recipient };
        }
        contact.Name = data.Recipient;
        var address = _selectedAddress is null ? null : contact.Addresses.FirstOrDefault(x => x.Id == _selectedAddress.Id);
        if (address is not null && !Postal.SameAddress(address.Value, data.Address))
        {
            var choice = MessageBox.Show(this, "地址已變更。選「是」覆蓋原地址，選「否」另存為新地址。",
                "保存地址", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) throw new OperationCanceledException();
            if (choice == MessageBoxResult.No) address = null;
        }
        address ??= contact.Addresses.FirstOrDefault(x => Postal.SameAddress(x.Value, data.Address));
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
            var phone = _selectedPhone is null ? null : contact.Phones.FirstOrDefault(x => x.Id == _selectedPhone.Id);
            if (phone is not null && phone.Display != number)
            {
                var choice = MessageBox.Show(this, "電話已變更。選「是」覆蓋原電話，選「否」另存新電話。",
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
            _selectedPhone = phone;
        }
        contact.LastDeliveryIds = data.DeliveryIds;
        _repository.SaveContact(contact);
        _selectedContact = contact;
        _selectedAddress = address;
        BindChoices();
    }

    private void PrintClick(object sender, RoutedEventArgs e)
    {
        UpdatePostalFromAddress();
        FormatPhone();
        var data = CurrentData();
        if (data.Recipient.Length == 0 || data.Address.Length == 0)
        {
            MessageBox.Show(this, "請輸入收件人及地址。", "資料不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_postalCleared && data.PostalCode.Length == 0)
        {
            _postalCleared = false;
            MessageBox.Show(this, "無法由目前地址判斷郵遞區號，原本的區號已清除。\n\n請輸入郵遞區號；確定不需要時再按一次「列印信封」。",
                "郵遞區號已清除", MessageBoxButton.OK, MessageBoxImage.Information);
            PostalBox.Focus();
            return;
        }
        var overflow = EnvelopeRenderer.Overflows(_format, data);
        if (overflow.Count > 0 && MessageBox.Show(this,
                $"{string.Join("、", overflow)}的文字超出欄位範圍，縮小字級後仍放不下，超出的部分不會印出。\n\n請調整格式設定或縮短文字。仍要保存並列印嗎？",
                "文字超出範圍", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try { SaveBeforePrinting(data); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失敗", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        Status($"已保存「{data.Recipient}」。");
        try
        {
            if (SendToPrinter(origin => EnvelopeRenderer.Draw(_format, data, false, printerOrigin: origin),
                    $"CYEnvelope - {data.Recipient}"))
                Status($"已保存「{data.Recipient}」並送出列印。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "列印失敗；聯絡人資料已保存", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Test print for plain paper: field boxes, sample text and a millimetre ruler drawn with the same
    // geometry, offset and page setup as a real print, so the result can be laid over the envelope.
    private void CalibrationClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SendToPrinter(origin => EnvelopeRenderer.DrawCalibration(_format, origin), "CYEnvelope - 校正列印"))
                Status("已送出校正列印；請以普通紙印出後疊在信封上對光檢查。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "校正列印失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Shared by real and calibration prints. Returns false when the user cancelled at any dialog.
    private bool SendToPrinter(Func<Vector, DrawingVisual> render, string title)
    {
        var dialog = CreatePrintDialog();
        if (dialog.ShowDialog() != true) return false;
        _settings.PrinterName = dialog.PrintQueue.FullName;
        _repository.SaveSettings(_settings);
        if (!PreparePage(dialog, out var origin)) return false;
        dialog.PrintVisual(render(origin), title);
        return true;
    }

    // Requests the envelope size from the driver, confirms what it accepted, and returns the
    // imageable-area origin: PrintVisual draws from that origin, the renderer from the paper edge.
    private bool PreparePage(PrintDialog dialog, out Vector origin)
    {
        origin = default;
        var ticket = dialog.PrintTicket;
        var shortSide = Math.Min(_format.WidthMm, _format.HeightMm) * EnvelopeRenderer.DipPerMm;
        var longSide = Math.Max(_format.WidthMm, _format.HeightMm) * EnvelopeRenderer.DipPerMm;
        ticket.PageOrientation = _format.Landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
        ticket.PageMediaSize = new PageMediaSize(shortSide, longSide); // media size is always portrait
        var accepted = dialog.PrintQueue.MergeAndValidatePrintTicket(dialog.PrintQueue.UserPrintTicket, ticket)
            .ValidatedPrintTicket;
        var media = accepted.PageMediaSize;
        const double tolerance = 1.5 * EnvelopeRenderer.DipPerMm;
        if (media?.Width is not double width || media.Height is not double height ||
            Math.Abs(width - shortSide) > tolerance || Math.Abs(height - longSide) > tolerance)
        {
            var actual = media?.Width is double w && media.Height is double h
                ? $"{w / EnvelopeRenderer.DipPerMm:0} × {h / EnvelopeRenderer.DipPerMm:0} mm" : "未知尺寸";
            if (MessageBox.Show(this,
                    $"印表機驅動程式未接受 {_format.WidthMm:0.#} × {_format.HeightMm:0.#} mm 信封尺寸，改用 {actual}。\n\n位置可能偏移。仍要列印嗎？",
                    "紙張尺寸不符", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return false;
        }
        dialog.PrintTicket = accepted;
        var area = dialog.PrintQueue.GetPrintCapabilities(accepted).PageImageableArea;
        if (area is not null) origin = new Vector(area.OriginWidth, area.OriginHeight);
        return true;
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
        _postalAddress = null;
        _postalCleared = false;
        AddressChoice.ItemsSource = null; PhoneChoice.ItemsSource = null;
        Suggestions.Visibility = Visibility.Collapsed;
        foreach (var item in _delivery.Values) item.IsChecked = false;
        ShowFrameBox.IsChecked = true;
        FrameChoice.SelectedIndex = FrameChoice.Items.Count > 0 ? 0 : -1;
        _loading = false;
        Status(ReadyStatus);
        Refresh();
        RecipientBox.Focus();
    }
    private void ContactsClick(object sender, RoutedEventArgs e)
    {
        new ContactWindow(_repository) { Owner = this }.ShowDialog();
        // Re-read the chosen contact so later saves never write back an outdated copy.
        if (_selectedContact is null) return;
        var current = _repository.Contacts().FirstOrDefault(x => x.Id == _selectedContact.Id);
        if (current is null)
        {
            Detach();
            Status("原選取的聯絡人已刪除；列印時會以目前輸入的資料另存。");
            return;
        }
        _selectedContact = current;
        _selectedAddress = current.Addresses.FirstOrDefault(x => x.Id == _selectedAddress?.Id);
        _selectedPhone = current.Phones.FirstOrDefault(x => x.Id == _selectedPhone?.Id);
        BindChoices();
    }
    private void FormatClick(object sender, RoutedEventArgs e)
    {
        var window = new FormatWindow(_repository, _format) { Owner = this };
        var chosen = window.ShowDialog() == true;
        // The window saves directly; reload even when it was closed without choosing a format.
        var formats = _repository.Formats();
        var id = chosen ? window.SelectedFormat.Id : _format.Id;
        _format = formats.FirstOrDefault(f => f.Id == id) ?? formats.FirstOrDefault(f => f.IsDefault) ?? formats[0];
        if (_settings.SelectedFormatId != _format.Id)
        {
            _settings.SelectedFormatId = _format.Id;
            _repository.SaveSettings(_settings);
        }
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
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _repository.SaveSettings(_settings);
        ApplyEntryMode();
    }
    private void ApplyEntryMode()
    {
        EntryPanel.Visibility = _settings.DirectEntry ? Visibility.Collapsed : Visibility.Visible;
        EntryColumn.Width = new GridLength(_settings.DirectEntry ? 0 : 400);
        EntryGap.Width = new GridLength(_settings.DirectEntry ? 0 : 20);
        DirectLayer.Visibility = _settings.DirectEntry ? Visibility.Visible : Visibility.Collapsed;
        PreviewHint.Text = _settings.DirectEntry ? "點選藍色欄位輸入；也可用 Tab、Enter 操作。" : "紅線為信封底圖，僅套印黑色內容。";
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
            var target = new Button
            {
                Width = Math.Max(20, rect.Width * EnvelopeRenderer.DipPerMm),
                Height = Math.Max(20, rect.Height * EnvelopeRenderer.DipPerMm),
                Style = (Style)FindResource("DirectTarget"),
                ToolTip = $"點選輸入{field}",
                Tag = field
            };
            System.Windows.Automation.AutomationProperties.SetName(target, $"編輯{field}");
            target.Click += DirectTargetClick;
            Canvas.SetLeft(target, (rect.X + _format.OffsetX) * EnvelopeRenderer.DipPerMm);
            Canvas.SetTop(target, (rect.Y + _format.OffsetY) * EnvelopeRenderer.DipPerMm);
            DirectLayer.Children.Add(target);
        }
        foreach (var item in _format.Delivery)
        {
            var target = new Button
            {
                Width = 18, Height = 18, Style = (Style)FindResource("DirectTarget"),
                ToolTip = $"勾選／取消{item.Label}", Tag = item.Id
            };
            target.Click += (_, _) =>
            {
                if (_delivery.TryGetValue((string)target.Tag, out var check))
                    check.IsChecked = check.IsChecked != true;
            };
            Canvas.SetLeft(target, (item.X + _format.OffsetX - 1) * EnvelopeRenderer.DipPerMm);
            Canvas.SetTop(target, (item.Y + _format.OffsetY - .8) * EnvelopeRenderer.DipPerMm);
            DirectLayer.Children.Add(target);
        }
    }
    private void DirectTargetClick(object sender, RoutedEventArgs e)
    {
        var target = (Button)sender;
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
        var previewScale = Math.Max(.1, PreviewHost.TransformToAncestor(this)
            .TransformBounds(new Rect(0, 0, 1, 1)).Width);
        var editorHeight = 34 / previewScale;
        var suggestionHeight = 120 / previewScale;
        var editor = new TextBox
        {
            Text = source.Text, Width = Math.Min(300 / previewScale, DirectLayer.Width - 12),
            MinHeight = editorHeight, FontSize = 14 / previewScale, Background = Brushes.White, BorderBrush = Brushes.SteelBlue,
            BorderThickness = new Thickness(1 / previewScale), Padding = new Thickness(6 / previewScale, 4 / previewScale, 6 / previewScale, 4 / previewScale),
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
                Width = editor.Width, MaxHeight = suggestionHeight, FontSize = 14 / previewScale,
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
            Canvas.SetTop(suggestions, Math.Clamp(Canvas.GetTop(target) + editorHeight, 2,
                Math.Max(2, DirectLayer.Height - suggestionHeight - 2)));
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
        Canvas.SetTop(editor, Math.Clamp(Canvas.GetTop(target), 2, Math.Max(2, DirectLayer.Height - editorHeight - 2)));
        editor.Focus();
        editor.SelectAll();
        e.Handled = true;
    }
}
