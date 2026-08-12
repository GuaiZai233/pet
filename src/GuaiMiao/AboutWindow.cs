using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GuaiMiao;

internal sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = $"关于 {AppInfo.ProductName}";
        Width = 410;
        Height = 210;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        Background = System.Windows.Media.Brushes.White;

        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock
        {
            Text = AppInfo.AboutText,
            FontSize = 16,
            Margin = new Thickness(0, 4, 0, 20),
            TextWrapping = TextWrapping.Wrap
        });

        var linkText = new TextBlock { FontSize = 15, Margin = new Thickness(0, 0, 0, 22) };
        var link = new Hyperlink { NavigateUri = new Uri(AppInfo.HomepageUrl) };
        link.Inlines.Add(AppInfo.HomepageLabel);
        link.RequestNavigate += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = AppInfo.HomepageUrl,
            UseShellExecute = true
        });
        linkText.Inlines.Add(link);
        panel.Children.Add(linkText);

        var close = new System.Windows.Controls.Button
        {
            Content = "关闭",
            Width = 92,
            Height = 32,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            IsDefault = true,
            IsCancel = true
        };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;
    }
}
