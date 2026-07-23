using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ModpackServerLauncher.Views;

public sealed class DialogWindow<T> : Window
{
    public DialogWindow(string title, string message, IReadOnlyList<(string Text, T Value)> buttons)
    {
        Title = title;
        Width = 420;
        Height = 190;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111318");

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(22),
            Spacing = 18
        };

        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            FontSize = 15
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };

        foreach (var (text, value) in buttons)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 92,
                Padding = new Avalonia.Thickness(14, 8)
            };
            button.Click += (_, _) => Close(value);
            buttonPanel.Children.Add(button);
        }

        panel.Children.Add(buttonPanel);
        Content = panel;
    }
}
