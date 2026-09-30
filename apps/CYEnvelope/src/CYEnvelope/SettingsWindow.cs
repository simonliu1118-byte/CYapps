using System.Windows;
using System.Windows.Controls;

namespace CYEnvelope;

public sealed class SettingsWindow : Window
{
    public SettingsWindow(AppSettings settings)
    {
        Title = "設定"; Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        DesktopUi.Dialog(this);
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = panel;
        panel.Children.Add(DesktopUi.Heading("輸入方式"));
        var direct = new CheckBox { Content = "直接點選信封欄位輸入", IsChecked = settings.DirectEntry };
        panel.Children.Add(direct);
        panel.Children.Add(new TextBlock { Text = "關閉後，使用主畫面左側的收件資料欄。", TextWrapping = TextWrapping.Wrap,
            Foreground = DesktopUi.Muted, Margin = new Thickness(0, 8, 0, 20) });
        panel.Children.Add(DesktopUi.Heading("信封字體"));
        var choices = FontCatalog.Choices
            .Select(c => (Choice: c, Label: FontCatalog.IsAvailable(c) ? c.Display : c.Display + (c.ResourceFile is null ? "（本機未安裝）" : "（此版本未內建）")))
            .ToList();
        var font = new ComboBox { ItemsSource = choices.Select(c => c.Label).ToList() };
        font.SelectedIndex = Math.Max(0, choices.FindIndex(c => c.Choice.Family == settings.FontFamily));
        panel.Children.Add(font);
        var sample = new TextBlock { Text = "王小明　高雄市新興區範例路一號", FontSize = 20, Margin = new Thickness(0, 10, 0, 6) };
        void ShowSample() => sample.FontFamily = FontCatalog.Typeface(choices[Math.Max(0, font.SelectedIndex)].Choice.Family, useSelected: false).FontFamily;
        font.SelectionChanged += (_, _) => ShowSample();
        ShowSample();
        panel.Children.Add(sample);
        panel.Children.Add(new TextBlock { Text = "套用到收件人、地址、電話、郵遞區號與方框文字；預覽與列印相同。內建字體隨程式提供，授權見 FONT_LICENSES.txt。",
            TextWrapping = TextWrapping.Wrap, Foreground = DesktopUi.Muted, Margin = new Thickness(0, 0, 0, 24) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = DesktopUi.Action("取消", (_, _) => Close()); cancel.IsCancel = true;
        actions.Children.Add(cancel);
        var save = DesktopUi.Action("儲存", (_, _) =>
        {
            settings.DirectEntry = direct.IsChecked == true;
            settings.FontFamily = choices[Math.Max(0, font.SelectedIndex)].Choice.Family;
            DialogResult = true;
        }, true);
        save.IsDefault = true; save.Margin = new Thickness(0); actions.Children.Add(save);
        panel.Children.Add(actions);
    }
}
