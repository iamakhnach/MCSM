using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModpackServerLauncher.ViewModels;

namespace ModpackServerLauncher.Views;

public sealed class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Title = "Настройки";
        Width = 520;
        Height = 360;
        MinWidth = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111318");

        var root = new StackPanel
        {
            Margin = new Avalonia.Thickness(22),
            Spacing = 14
        };

        root.Children.Add(CreateLabel("Путь хранения серверов"));
        var serversPath = new TextBox { Text = viewModel.ServersPath };
        serversPath.TextChanged += (_, _) => viewModel.ServersPath = serversPath.Text ?? "";
        root.Children.Add(serversPath);

        root.Children.Add(CreateLabel("Путь к Java"));
        var javaPath = new TextBox { Text = viewModel.JavaPath, PlaceholderText = "Автоматически" };
        javaPath.TextChanged += (_, _) => viewModel.JavaPath = javaPath.Text ?? "";
        root.Children.Add(javaPath);

        root.Children.Add(CreateLabel("Максимальная память, ГБ"));
        var memory = new NumericUpDown
        {
            Value = viewModel.MaxMemoryGb,
            Minimum = 2,
            Maximum = 64,
            Increment = 1
        };
        memory.ValueChanged += (_, args) => viewModel.MaxMemoryGb = (int)(args.NewValue ?? 8);
        root.Children.Add(memory);

        var darkTheme = new CheckBox { Content = "Темная тема", IsChecked = viewModel.DarkTheme };
        darkTheme.IsCheckedChanged += (_, _) => viewModel.DarkTheme = darkTheme.IsChecked == true;
        root.Children.Add(darkTheme);

        var autoUpdate = new CheckBox { Content = "Автоматическое обновление приложения", IsChecked = viewModel.AutoUpdate };
        autoUpdate.IsCheckedChanged += (_, _) => viewModel.AutoUpdate = autoUpdate.IsChecked == true;
        root.Children.Add(autoUpdate);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };

        var cancel = new Button { Content = "Отмена", MinWidth = 94 };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(cancel);

        var save = new Button { Content = "Сохранить", MinWidth = 110 };
        save.Click += async (_, _) =>
        {
            await _viewModel.SaveAsync();
            Close();
        };
        buttons.Children.Add(save);
        root.Children.Add(buttons);

        Content = root;
    }

    private static TextBlock CreateLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brush.Parse("#C8D0DC"),
            FontSize = 13
        };
    }
}
