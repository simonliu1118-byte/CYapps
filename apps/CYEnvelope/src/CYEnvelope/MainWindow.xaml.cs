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
    // Mail types ticked on the envelope preview (ids of _format.Delivery).
    private readonly HashSet<string> _delivery = [];
    private TextBox? _directEditor;
    private ListBox? _directSuggestions;
    private bool _loading;
    // Address text the postal code was derived from or confirmed for; null means typed before any address.
    private string? _postalAddress;
    private bool _settingPostal;
    // Set when an unresolvable address cleared the previous code; printing stops once to confirm.
    // The address could not be resolved: the postal box shows a red frame and asks for a hand-typed code.
    private bool _postalNeeded;
    private static readonly Brush DangerBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0x37, 0x37));
    // Code last inferred from the address text; a hand-corrected code survives edits that keep the same area.
    private string? _lastInferredCode;
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
        _repository = OpenRepository(data);
        _settings = _repository.Settings();
        var formats = _repository.Formats();
        _format = formats.FirstOrDefault(f => f.Id == _settings.SelectedFormatId)
                  ?? formats.FirstOrDefault(f => f.IsDefault) ?? formats[0];
        var version = File.ReadAllText(Path.Combine(appRoot, "VERSION")).Trim();
        var build = File.ReadAllText(Path.Combine(appRoot, "BUILD")).Trim();
        Title = $"CYEnvelope V{version}" + (build != "0" ? $" Build {build}" : "");
        ShowFrameBox.IsChecked = true;
        ReprintButton.IsEnabled = _settings.LastPrint is not null;
        RebuildOptions();
        ApplyEntryMode();
        Refresh();
    }

    // A damaged database is never opened or overwritten: offer the newest daily backup instead.
    private static Repository OpenRepository(string path)
    {
        try { Repository.EnsureReadable(path); }
        catch (Repository.CorruptDatabaseException error)
        {
            var latest = Repository.LatestBackup(path);
            var restore = latest is not null && MessageBox.Show(
                $"{error.Message}\n\n要用最近一份備份（{Path.GetFileName(latest)}）還原嗎？\n損毀的檔案會保留為 .damaged-… 檔，不會刪除。",
                "資料庫損毀", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (!restore)
            {
                MessageBox.Show("未開啟資料庫。請保留 Data 資料夾並聯絡維護者，資料不會被覆蓋。", "資料庫損毀",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
            Repository.RestoreLatestBackup(path);
        }
        return new Repository(path);
    }

    // Rebuilds format-dependent options while keeping the envelope currently being prepared.
    private void RebuildOptions()
    {
        var frameText = FrameChoice.SelectedItem as string;
        _loading = true;
        _delivery.RemoveWhere(id => _format.Delivery.All(x => x.Id != id));
        // Left-hand checkboxes and the clickable envelope table edit the same set.
        DeliveryPanel.Children.Clear();
        foreach (var option in _format.Delivery)
        {
            var box = new CheckBox { Content = option.Label, Margin = new Thickness(0, 0, 8, 9), MinWidth = 76,
                                     Tag = option.Id, IsChecked = _delivery.Contains(option.Id) };
            box.Checked += DeliveryBoxChanged;
            box.Unchecked += DeliveryBoxChanged;
            DeliveryPanel.Children.Add(box);
        }
        FrameChoice.ItemsSource = _settings.FrameTexts.ToArray();
        var frameIndex = frameText is null ? -1 : _settings.FrameTexts.IndexOf(frameText);
        FrameChoice.SelectedIndex = frameIndex >= 0 ? frameIndex : _settings.FrameTexts.Count > 0 ? 0 : -1;
        _loading = false;
    }

    private void DeliveryBoxChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var box = (CheckBox)sender;
        var id = (string)box.Tag;
        if (box.IsChecked == true) _delivery.Add(id); else _delivery.Remove(id);
        Refresh();
    }

    // Shows the current mail-type set in the checkboxes (after a preview click, a customer pick or a clear).
    private void SyncDeliveryBoxes()
    {
        var loading = _loading;
        _loading = true;
        foreach (var box in DeliveryPanel.Children.OfType<CheckBox>()) box.IsChecked = _delivery.Contains((string)box.Tag);
        _loading = loading;
    }

    private void Status(string text) => StatusText.Text = text;

    private PrintData CurrentData() => new()
    {
        Recipient = Names.Normalize(RecipientBox.Text),
        Address = AddressBox.Text.Trim(),
        PostalCode = PostalBox.Text.Trim(),
        Phone = PhoneBox.Text.Trim(),
        DeliveryIds = _format.Delivery.Where(x => _delivery.Contains(x.Id)).Select(x => x.Id).ToList(),
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
        var matches = _repository.SearchContacts(query);
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
        _delivery.Clear();
        foreach (var id in contact.LastDeliveryIds.Where(id => _format.Delivery.Any(x => x.Id == id))) _delivery.Add(id);
        SyncDeliveryBoxes();
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
        if (code.Length > 0) MarkPostalNeeded(false);
    }

    // Pasting or typing an address fills the postal code at once (offline table, three digits).
    private void AddressTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
        {
            var code = Postal.Infer(AddressBox.Text);
            if (code is not null && code != _lastInferredCode)
            {
                SetPostal(code, AddressBox.Text);
            }
            _lastInferredCode = code;
        }
        Refresh();
    }

    private void PostalChanged(object sender, TextChangedEventArgs e)
    {
        if (!_settingPostal)
        {
            _postalAddress = AddressBox.Text.Trim().Length == 0 ? null : AddressBox.Text;
            if (PostalBox.Text.Length > 0) MarkPostalNeeded(false);
        }
        Refresh();
    }

    private void ApplyAddress(ContactAddress? address)
    {
        _selectedAddress = address;
        AddressBox.Text = address?.Value ?? "";
        // A saved address without a code (unresolved when it was saved) is looked up again.
        var code = address?.PostalCode ?? "";
        if (code.Length == 0 && address is not null) code = Postal.Infer(address.Value) ?? "";
        SetPostal(code, AddressBox.Text);
        MarkPostalNeeded(address is not null && code.Length == 0);
        _lastInferredCode = Postal.Infer(AddressBox.Text);
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
            Status(ReadyStatus);
        }
        else if (_postalAddress is null && PostalBox.Text.Length > 0)
        {
            _postalAddress = address; // typed before the address: keep the user's code for it
        }
        else
        {
            // Never keep the previous address's code; ask for one instead (no popup, printing is not blocked).
            SetPostal("", address);
            MarkPostalNeeded(true);
        }
    }

    // Inline validation: Danger frame plus a short text in the label, same size so nothing moves.
    private void MarkPostalNeeded(bool needed)
    {
        if (needed == _postalNeeded) return;
        _postalNeeded = needed;
        if (needed)
        {
            PostalBox.BorderBrush = DangerBrush;
            PostalLabel.Text = "請手動輸入";
            PostalLabel.Foreground = DangerBrush;
            Status("無法由地址判斷郵遞區號，請在紅框內手動輸入。");
        }
        else
        {
            PostalBox.ClearValue(Control.BorderBrushProperty);
            PostalLabel.Text = "郵遞區號";
            PostalLabel.ClearValue(TextBlock.ForegroundProperty);
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
        var saved = ContactSaver.Save(_repository, _selectedContact, _selectedAddress, _selectedPhone, data,
            request => ChoiceDialog.Ask(this, request));
        _selectedContact = saved.Contact;
        _selectedAddress = saved.Address;
        _selectedPhone = saved.Phone;
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
            {
                // Sent: remember it for "reprint last" and empty the entry for the next customer.
                _settings.LastPrint = data;
                _repository.SaveSettings(_settings);
                ReprintButton.IsEnabled = true;
                ResetEntry(resetFrame: false);
                Status($"已列印「{data.Recipient}」。可輸入下一筆；「重印上一筆」可帶回剛才的資料。");
            }
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
    private void ClearClick(object sender, RoutedEventArgs e) => ResetEntry(resetFrame: true);

    // Empties the entry for the next envelope. After a print the statement text choice is kept
    // (every envelope of a batch normally carries the same one); the Clear button resets it too.
    private void ResetEntry(bool resetFrame)
    {
        _loading = true;
        _selectedContact = null; _selectedAddress = null; _selectedPhone = null;
        RecipientBox.Clear(); AddressBox.Clear(); PhoneBox.Clear(); PostalBox.Clear();
        _postalAddress = null;
        MarkPostalNeeded(false);
        _lastInferredCode = null;
        AddressChoice.ItemsSource = null; PhoneChoice.ItemsSource = null;
        Suggestions.Visibility = Visibility.Collapsed;
        _delivery.Clear();
        SyncDeliveryBoxes();
        if (resetFrame)
        {
            ShowFrameBox.IsChecked = true;
            FrameChoice.SelectedIndex = FrameChoice.Items.Count > 0 ? 0 : -1;
        }
        _loading = false;
        Status(ReadyStatus);
        Refresh();
        RecipientBox.Focus();
    }

    private void ReprintClick(object sender, RoutedEventArgs e) => ReprintLast();

    // Brings the last printed envelope back exactly as it was sent; printing stays a deliberate click.
    private void ReprintLast()
    {
        if (_settings.LastPrint is not { } last) return;
        var contact = _repository.FindByName(last.Recipient).FirstOrDefault();
        var number = last.Phone.Length == 0 ? "" : PhoneFormatting.Format(last.Phone);
        _loading = true;
        RecipientBox.Text = last.Recipient;
        AddressBox.Text = last.Address;
        SetPostal(last.PostalCode, last.Address);
        _lastInferredCode = Postal.Infer(last.Address);
        PhoneBox.Text = last.Phone;
        _selectedContact = contact;
        _selectedAddress = contact?.Addresses.FirstOrDefault(x => Postal.SameAddress(x.Value, last.Address));
        _selectedPhone = contact?.Phones.FirstOrDefault(x => x.Display == number);
        BindChoices();
        Suggestions.Visibility = Visibility.Collapsed;
        _delivery.Clear();
        foreach (var id in last.DeliveryIds.Where(id => _format.Delivery.Any(x => x.Id == id))) _delivery.Add(id);
        SyncDeliveryBoxes();
        ShowFrameBox.IsChecked = last.ShowFrame;
        var frameIndex = _settings.FrameTexts.IndexOf(last.FrameText);
        if (frameIndex >= 0) FrameChoice.SelectedIndex = frameIndex;
        MarkPostalNeeded(false);
        _loading = false;
        Status($"已帶回上一筆「{last.Recipient}」；確認後按「列印信封」。");
        Refresh();
        PrintButton.Focus();
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
        DirectLayer.Visibility = Visibility.Visible; // mail-type and frame-text clicks work in both modes
        PreviewHint.Text = _settings.DirectEntry
            ? "點選藍色欄位輸入；點左側表格勾選郵件種類，點黑框選方框文字。"
            : "點左側表格勾選郵件種類，點黑框選方框文字。紅線為信封底圖，僅套印黑色內容。";
        RebuildDirectTargets();
    }
    private void RebuildDirectTargets()
    {
        DirectLayer.Children.Clear();
        _directEditor = null;
        _directSuggestions = null;
        DirectLayer.Width = _format.WidthMm * EnvelopeRenderer.DipPerMm;
        DirectLayer.Height = _format.HeightMm * EnvelopeRenderer.DipPerMm;
        Button Target(string style, string tip, object tag, RectMm rect, double offsetX, double offsetY, Action click)
        {
            var target = new Button
            {
                Width = Math.Max(4, rect.Width * EnvelopeRenderer.DipPerMm),
                Height = Math.Max(4, rect.Height * EnvelopeRenderer.DipPerMm),
                Style = (Style)FindResource(style), ToolTip = tip, Tag = tag
            };
            System.Windows.Automation.AutomationProperties.SetName(target, tip);
            target.Click += (_, _) => click(); // not marked Handled: DirectTargetClick is attached after it
            Canvas.SetLeft(target, (rect.X + offsetX) * EnvelopeRenderer.DipPerMm);
            Canvas.SetTop(target, (rect.Y + offsetY) * EnvelopeRenderer.DipPerMm);
            DirectLayer.Children.Add(target);
            return target;
        }
        if (_settings.DirectEntry)
            foreach (var (field, rect) in new[]
            {
                ("收件人", _format.Recipient.Rect), ("地址", _format.Address.Rect),
                ("電話", _format.Phone.Rect), ("郵遞區號", _format.PostalCode.Rect)
            })
            {
                RectMm area = new(rect.X, rect.Y, Math.Max(rect.Width, 6), Math.Max(rect.Height, 6));
                var button = Target("DirectTarget", $"點選輸入{field}", field, area, _format.OffsetX, _format.OffsetY, () => { });
                button.Click += DirectTargetClick;
            }
        // The black frame the statement text is printed in: click to pick from the saved list.
        Target(_settings.DirectEntry ? "DirectTarget" : "TickTarget", "選擇方框文字", "方框文字", _format.Frame,
            _format.OffsetX, _format.OffsetY, () => ShowFrameMenu());
        // Mail types sit on the printed table rows (not shifted by the printer offset): click a row to tick it.
        foreach (var item in _format.Delivery)
        {
            var row = _format.Landscape ? new RectMm(item.X - 1, item.Y - .8, 4.2, 4.2) : new RectMm(item.X - 1, item.Y - .3, 20, 4);
            var id = item.Id;
            Target("TickTarget", $"{item.Label}（點選勾選／取消）", id, row, 0, 0, () =>
            {
                if (!_delivery.Remove(id)) _delivery.Add(id);
                SyncDeliveryBoxes();
                Refresh();
            });
        }
    }

    // Statement text list on the envelope itself: pick one, or choose not to print it.
    private void ShowFrameMenu()
    {
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        var showing = ShowFrameBox.IsChecked == true;
        var current = FrameChoice.SelectedItem as string;
        var none = new MenuItem { Header = "不印方框文字", IsCheckable = true, IsChecked = !showing };
        none.Click += (_, _) => ShowFrameBox.IsChecked = false;
        menu.Items.Add(none);
        menu.Items.Add(new Separator());
        foreach (var text in _settings.FrameTexts)
        {
            var value = text;
            var item = new MenuItem { Header = value, IsCheckable = true, IsChecked = showing && value == current };
            item.Click += (_, _) => { FrameChoice.SelectedItem = value; ShowFrameBox.IsChecked = true; Refresh(); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var manage = new MenuItem { Header = "管理清單…" };
        manage.Click += (_, _) => FrameClick(this, new RoutedEventArgs());
        menu.Items.Add(manage);
        menu.IsOpen = true;
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
        if (source is null) return;
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
                var matches = _repository.SearchContacts(query, 10);
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
