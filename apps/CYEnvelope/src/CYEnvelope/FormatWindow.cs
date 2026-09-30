using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CYEnvelope;

public sealed class FormatWindow : Window
{
    private readonly Repository _repository;
    private readonly ComboBox _formats = new() { DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 6, 8) };
    private readonly ComboBox _fields = new() { Margin = new Thickness(0, 4, 0, 8) };
    private readonly VisualHost _preview = new();
    private readonly TextBox _name = new(), _width = new(), _height = new();
    private readonly TextBox _x = new(), _y = new(), _w = new(), _h = new();
    private readonly TextBox _size = new(), _columns = new();
    private readonly TextBox _offsetX = new(), _offsetY = new();
    private readonly CheckBox _landscape = new() { Content = "橫式", Margin = new Thickness(0, 8, 0, 8) };
    private EnvelopeFormat _working;
    private bool _loading;
    public EnvelopeFormat SelectedFormat { get; private set; }

    public FormatWindow(Repository repository, EnvelopeFormat selected)
    {
        _repository = repository;
        SelectedFormat = selected;
        _working = Copy(selected);
        Title = "格式設定";
        Width = 1040; Height = 820; MinWidth = 900; MinHeight = 720;
        DesktopUi.Dialog(this);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Microsoft JhengHei UI");
        FontSize = 14;
        var root = new Grid { Margin = new Thickness(16) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(365) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        var shell = new DockPanel(); Content = shell;
        var footer = Buttons(("儲存", (_, _) => Save()), ("設為預設", (_, _) => SetDefault()), ("選用並關閉", (_, _) => SelectAndClose()));
        footer.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Margin = new Thickness(20, 12, 14, 12);
        ((Button)footer.Children[2]).SetResourceReference(StyleProperty, "PrimaryButton");
        var footerLine = new Border { Background = Brushes.White, BorderBrush = DesktopUi.Muted,
            BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        DockPanel.SetDock(footerLine, Dock.Bottom); shell.Children.Add(footerLine);
        shell.Children.Add(root);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0) };
        Grid.SetColumn(scroll, 0); root.Children.Add(scroll);
        var left = new StackPanel(); scroll.Content = left;
        left.Children.Add(new TextBlock { Text = "信封格式", FontSize = 16, FontWeight = FontWeights.SemiBold });
        left.Children.Add(_formats);
        _formats.SelectionChanged += (_, _) =>
        {
            if (_loading || _formats.SelectedItem is not EnvelopeFormat selectedFormat) return;
            _working = Copy(selectedFormat);
            Load();
        };
        left.Children.Add(Buttons(("新增", (_, _) => New()), ("複製", (_, _) => Duplicate()),
            ("刪除", (_, _) => Delete())));
        AddField(left, "名稱", _name);
        var dimensions = new Grid();
        dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        dimensions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(AddField(dimensions, "寬（mm）", _width, 0), 0);
        Grid.SetColumn(AddField(dimensions, "高（mm）", _height, 2), 2);
        left.Children.Add(dimensions);
        left.Children.Add(_landscape);
        _landscape.Checked += Changed; _landscape.Unchecked += Changed;
        left.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 12) });
        left.Children.Add(new TextBlock { Text = "選取欄位（所有欄位仍同時顯示）", FontWeight = FontWeights.SemiBold });
        _fields.ItemsSource = new[] { "收件人", "地址", "電話", "郵遞區號", "方框文字" };
        _fields.SelectedIndex = 0;
        _fields.SelectionChanged += (_, _) => LoadField();
        left.Children.Add(_fields);
        var position = new Grid();
        position.ColumnDefinitions.Add(new ColumnDefinition());
        position.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        position.ColumnDefinitions.Add(new ColumnDefinition());
        AddField(position, "X（mm）", _x, 0);
        AddField(position, "Y（mm）", _y, 2);
        AddField(position, "寬（mm）", _w, 0, 1);
        AddField(position, "高（mm）", _h, 2, 1);
        left.Children.Add(position);
        var typography = new Grid();
        typography.ColumnDefinitions.Add(new ColumnDefinition());
        typography.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        typography.ColumnDefinitions.Add(new ColumnDefinition());
        AddField(typography, "字級（pt）", _size, 0);
        AddField(typography, "直排列數", _columns, 2);
        left.Children.Add(typography);
        left.Children.Add(new TextBlock { Text = "印表機偏移（mm）", Margin = new Thickness(0, 8, 0, 4) });
        var offsets = new Grid();
        offsets.ColumnDefinitions.Add(new ColumnDefinition());
        offsets.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        offsets.ColumnDefinitions.Add(new ColumnDefinition());
        AddField(offsets, "水平", _offsetX, 0);
        AddField(offsets, "垂直", _offsetY, 2);
        left.Children.Add(offsets);
        foreach (var box in new[] { _name, _width, _height, _x, _y, _w, _h, _size, _columns, _offsetX, _offsetY })
            box.LostKeyboardFocus += Changed;
        var frame = new Border { Background = Brushes.White, BorderBrush = Brushes.LightGray,
                                 BorderThickness = new Thickness(1), Padding = new Thickness(12) };
        Grid.SetColumn(frame, 2); root.Children.Add(frame);
        frame.Child = new Viewbox { Stretch = Stretch.Uniform, Child = _preview };
        ReloadFormats(selected.Id);
    }

    private static StackPanel AddField(Panel panel, string title, TextBox entry, int column = 0, int row = 0)
    {
        var group = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
        group.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 4) });
        entry.MinHeight = 30;
        group.Children.Add(entry);
        if (panel is Grid grid)
        {
            while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(group, row); Grid.SetColumn(group, column);
        }
        panel.Children.Add(group);
        return group;
    }
    private static StackPanel Buttons(params (string Label, RoutedEventHandler Action)[] values)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 7) };
        foreach (var (label, action) in values)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 0) };
            button.Click += action; panel.Children.Add(button);
        }
        return panel;
    }
    private static EnvelopeFormat Copy(EnvelopeFormat format) =>
        JsonSerializer.Deserialize<EnvelopeFormat>(JsonSerializer.Serialize(format))!;
    private void ReloadFormats(string id)
    {
        _loading = true;
        var formats = _repository.Formats();
        _formats.ItemsSource = formats;
        _formats.SelectedItem = formats.FirstOrDefault(x => x.Id == id) ?? formats.First();
        _working = Copy((EnvelopeFormat)_formats.SelectedItem);
        _loading = false;
        Load();
    }
    private void Load()
    {
        _loading = true;
        _name.Text = _working.Name;
        _width.Text = _working.WidthMm.ToString("0.##");
        _height.Text = _working.HeightMm.ToString("0.##");
        _landscape.IsChecked = _working.Landscape;
        _offsetX.Text = _working.OffsetX.ToString("0.##");
        _offsetY.Text = _working.OffsetY.ToString("0.##");
        _loading = false;
        LoadField();
    }
    private void LoadField()
    {
        if (_loading) return;
        _loading = true;
        var p = Placement();
        var r = p?.Rect ?? _working.Frame;
        _x.Text = r.X.ToString("0.##"); _y.Text = r.Y.ToString("0.##");
        _w.Text = r.Width.ToString("0.##"); _h.Text = r.Height.ToString("0.##");
        _size.Text = (p?.FontSize ?? 11).ToString("0.##");
        _columns.Text = (p?.Columns ?? 1).ToString();
        _size.IsEnabled = _columns.IsEnabled = p is not null;
        _loading = false;
        Draw();
    }
    private TextPlacement? Placement() => (_fields.SelectedItem as string) switch
    {
        "收件人" => _working.Recipient,
        "地址" => _working.Address,
        "電話" => _working.Phone,
        "郵遞區號" => _working.PostalCode,
        _ => null
    };
    private void Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (ReferenceEquals(sender, _landscape) && _working.Landscape != (_landscape.IsChecked == true))
        {
            var oldOrientation = _working.Landscape;
            if (!Apply()) { Load(); return; }
            _working.Landscape = oldOrientation;
            FormatGeometry.Rotate(_working);
            Load();
            return;
        }
        if (!Apply()) return;
        Draw();
    }
    private bool Apply()
    {
        static bool Num(TextBox box, out double n) =>
            double.TryParse(box.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture, out n) && double.IsFinite(n);
        if (!Num(_width, out var width) || !Num(_height, out var height) ||
            !Num(_x, out var x) || !Num(_y, out var y) ||
            !Num(_w, out var w) || !Num(_h, out var h) ||
            !Num(_offsetX, out var ox) || !Num(_offsetY, out var oy) ||
            width <= 0 || height <= 0 || w <= 0 || h <= 0)
            return false;
        _working.Name = _name.Text.Trim();
        _working.WidthMm = width; _working.HeightMm = height;
        _working.Landscape = _landscape.IsChecked == true;
        _working.OffsetX = ox; _working.OffsetY = oy;
        var p = Placement();
        var r = p?.Rect ?? _working.Frame;
        r.X = x; r.Y = y; r.Width = w; r.Height = h;
        if (p is not null)
        {
            if (!Num(_size, out var size) || size < 5 || size > 72 ||
                !int.TryParse(_columns.Text, out var columns) || columns < 1 || columns > 8) return false;
            p.FontSize = size; p.Columns = columns;
        }
        return true;
    }
    private void Draw()
    {
        var sample = new PrintData
        {
            Recipient = "收件人", Address = "高雄市新興區範例路一號", PostalCode = "800",
            Phone = "0912-345-678", DeliveryIds = ["delivery-1"], FrameText = "內附對帳單"
        };
        _preview.Show(EnvelopeRenderer.Draw(_working, sample, true, _fields.SelectedItem as string),
            _working.WidthMm * EnvelopeRenderer.DipPerMm, _working.HeightMm * EnvelopeRenderer.DipPerMm);
    }
    private void New()
    {
        _working = new EnvelopeFormat { IsDefault = false, Name = "自訂信封" };
        Load();
    }
    private void Duplicate()
    {
        _working = Copy(_working);
        _working.Id = Guid.NewGuid().ToString("N");
        _working.Name += " 複本";
        _working.IsDefault = false;
        Load();
    }
    private void Delete()
    {
        if (_working.Id == "format-15k" || _working.IsDefault)
        {
            MessageBox.Show(this, "內建或目前預設格式不可刪除。", "無法刪除", MessageBoxButton.OK, MessageBoxImage.Information); return;
        }
        if (MessageBox.Show(this, "確定刪除此格式？", "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _repository.DeleteFormat(_working.Id);
        ReloadFormats("format-15k");
    }
    private bool Save()
    {
        if (!Apply() || _working.Name.Length == 0)
        {
            MessageBox.Show(this, "請檢查名稱與毫米、字級數值。", "數值有誤", MessageBoxButton.OK, MessageBoxImage.Warning); return false;
        }
        _repository.SaveFormat(_working);
        var id = _working.Id;
        ReloadFormats(id);
        return true;
    }
    private void SetDefault()
    {
        if (!Save()) return;
        _repository.SetDefaultFormat(_working.Id);
        ReloadFormats(_working.Id);
    }
    private void SelectAndClose()
    {
        if (!Save()) return;
        SelectedFormat = _working;
        DialogResult = true; Close();
    }
}
