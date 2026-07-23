using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ModpackServerLauncher.Models;
using ModpackServerLauncher.Services;
using ModpackServerLauncher.ViewModels;

namespace ModpackServerLauncher.Views;

public sealed class MainWindow : Window
{
    private readonly TextBlock _emptyHint = new();
    private readonly Border _dropZone = new();
    private readonly Border _serverCard = new();
    private readonly TextBlock _name = new();
    private readonly TextBlock _minecraft = new();
    private readonly TextBlock _loader = new();
    private readonly TextBlock _status = new();
    private readonly ComboBox _memory = new();
    private readonly ListBox _logs = new();
    private readonly TextBox _command = new();
    private readonly TextBlock _busy = new();
    private bool _logScrollPending;

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public MainWindow()
    {
        Title = "Server Java";
        Width = 980;
        Height = 680;
        MinWidth = 760;
        MinHeight = 560;
        Background = Brush.Parse("#0F1117");
        Content = BuildContent();

        Opened += async (_, _) =>
        {
            WireViewModel();
            if (ViewModel is not null)
            {
                await ViewModel.InitializeCommand.ExecuteAsync(null);
            }
        };
    }

    private Control BuildContent()
    {
        var root = new Grid
        {
            Margin = new Avalonia.Thickness(28),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 18
        };

        _dropZone.CornerRadius = new Avalonia.CornerRadius(8);
        _dropZone.BorderBrush = Brush.Parse("#384150");
        _dropZone.BorderThickness = new Avalonia.Thickness(1.5);
        _dropZone.Background = Brush.Parse("#171A22");
        _dropZone.Padding = new Avalonia.Thickness(28);
        DragDrop.SetAllowDrop(_dropZone, true);
        _dropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        _dropZone.AddHandler(DragDrop.DropEvent, OnDrop);

        var dropPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12
        };
        dropPanel.Children.Add(new TextBlock
        {
            Text = "Перетащите сюда архив модпака",
            Foreground = Brushes.White,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        dropPanel.Children.Add(new TextBlock
        {
            Text = "или",
            Foreground = Brush.Parse("#8E98A8"),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var choose = new Button
        {
            Content = "Выбрать архив",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Avalonia.Thickness(18, 9)
        };
        choose.Click += async (_, _) =>
        {
            if (ViewModel is not null)
            {
                await ViewModel.ChooseArchiveCommand.ExecuteAsync(null);
            }
        };
        dropPanel.Children.Add(choose);
        var fullCleanup = new Button
        {
            Content = "Полная очистка",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Avalonia.Thickness(18, 9)
        };
        fullCleanup.Click += async (_, _) =>
        {
            if (ViewModel is not null)
            {
                await ViewModel.FullCleanupCommand.ExecuteAsync(null);
            }
        };
        dropPanel.Children.Add(fullCleanup);
        _dropZone.Child = dropPanel;
        Grid.SetRow(_dropZone, 0);
        root.Children.Add(_dropZone);

        _serverCard.CornerRadius = new Avalonia.CornerRadius(8);
        _serverCard.Background = Brush.Parse("#171A22");
        _serverCard.Padding = new Avalonia.Thickness(20);
        _serverCard.Child = BuildServerCard();
        Grid.SetRow(_serverCard, 1);
        root.Children.Add(_serverCard);

        _logs.Background = Brush.Parse("#0B0D12");
        _logs.BorderBrush = Brush.Parse("#2C3441");
        _logs.Foreground = Brush.Parse("#D7DEE9");
        _logs.FontFamily = FontFamily.Parse("Consolas");
        _logs.FontSize = 12;
        Grid.SetRow(_logs, 2);
        root.Children.Add(_logs);

        var commandPanel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10
        };
        _command.PlaceholderText = "Команда сервера";
        _command.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && ViewModel is not null)
            {
                await ViewModel.SendCommandCommand.ExecuteAsync(null);
            }
        };
        commandPanel.Children.Add(_command);

        var send = new Button { Content = "Отправить", MinWidth = 106 };
        send.Click += async (_, _) =>
        {
            if (ViewModel is not null)
            {
                await ViewModel.SendCommandCommand.ExecuteAsync(null);
            }
        };
        Grid.SetColumn(send, 1);
        commandPanel.Children.Add(send);

        var clear = new Button { Content = "Очистить", MinWidth = 94 };
        clear.Click += (_, _) => ViewModel?.ClearLogsCommand.Execute(null);
        Grid.SetColumn(clear, 2);
        commandPanel.Children.Add(clear);

        Grid.SetRow(commandPanel, 3);
        root.Children.Add(commandPanel);

        _emptyHint.Text = "Серверов пока нет";
        _emptyHint.Foreground = Brush.Parse("#8E98A8");
        _emptyHint.HorizontalAlignment = HorizontalAlignment.Right;
        _emptyHint.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRow(_emptyHint, 1);
        root.Children.Add(_emptyHint);

        _busy.Foreground = Brush.Parse("#8E98A8");
        _busy.HorizontalAlignment = HorizontalAlignment.Right;
        _busy.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetRow(_busy, 0);
        root.Children.Add(_busy);

        return root;
    }

    private Control BuildServerCard()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            RowSpacing = 14,
            ColumnSpacing = 18
        };

        _name.FontSize = 21;
        _name.FontWeight = FontWeight.SemiBold;
        _name.Foreground = Brushes.White;
        grid.Children.Add(_name);

        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 18
        };
        _minecraft.Foreground = Brush.Parse("#C8D0DC");
        _loader.Foreground = Brush.Parse("#C8D0DC");
        _status.Foreground = Brush.Parse("#C8D0DC");
        meta.Children.Add(_minecraft);
        meta.Children.Add(_loader);
        meta.Children.Add(_status);
        Grid.SetRow(meta, 1);
        grid.Children.Add(meta);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        controls.Children.Add(new TextBlock
        {
            Text = "RAM",
            Foreground = Brush.Parse("#8E98A8"),
            VerticalAlignment = VerticalAlignment.Center
        });
        _memory.Width = 92;
        _memory.SelectionChanged += async (_, _) =>
        {
            if (ViewModel is not null && _memory.SelectedItem is string value)
            {
                await ViewModel.SetMemoryAsync(value);
            }
        };
        controls.Children.Add(_memory);

        controls.Children.Add(CreateButton("> Запустить", async () =>
        {
            if (ViewModel is not null)
            {
                await ViewModel.StartServerCommand.ExecuteAsync(null);
            }
        }));
        controls.Children.Add(CreateButton("[] Остановить", async () =>
        {
            if (ViewModel is not null)
            {
                await ViewModel.StopServerCommand.ExecuteAsync(null);
            }
        }));
        controls.Children.Add(CreateButton("Папка", () =>
        {
            ViewModel?.OpenFolderCommand.Execute(null);
            return Task.CompletedTask;
        }));
        controls.Children.Add(CreateButton("Логи", () =>
        {
            _logs.Focus();
            return Task.CompletedTask;
        }));
        controls.Children.Add(CreateButton("Настройки", async () =>
        {
            if (ViewModel is not null)
            {
                await ViewModel.OpenSettingsCommand.ExecuteAsync(null);
            }
        }));
        controls.Children.Add(CreateButton("Очистка", async () =>
        {
            if (ViewModel is not null)
            {
                await ViewModel.FullCleanupCommand.ExecuteAsync(null);
            }
        }));

        Grid.SetRow(controls, 2);
        Grid.SetColumnSpan(controls, 2);
        grid.Children.Add(controls);

        return grid;
    }

    private static Button CreateButton(string text, Func<Task> action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Avalonia.Thickness(12, 8),
            MinWidth = 82
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private void WireViewModel()
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.PickArchiveRequested += PickArchiveAsync;
        ViewModel.ExistingServerChoiceRequested += AskExistingServerAsync;
        ViewModel.EulaAcceptanceRequested += AskEulaAsync;
        ViewModel.FullCleanupConfirmationRequested += AskFullCleanupAsync;
        ViewModel.SettingsRequested += OpenSettingsAsync;
        ViewModel.OpenFolderRequested += OpenFolder;
        ViewModel.ErrorRequested += ShowError;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Servers.CollectionChanged += OnServersChanged;
        ViewModel.Logs.CollectionChanged += OnLogsChanged;
        _logs.ItemsSource = ViewModel.Logs;
        _memory.ItemsSource = ViewModel.MemoryOptions;
        RefreshAll();
    }

    private async Task<string?> PickArchiveAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Архивы модпаков")
                {
                    Patterns = ["*.mrpack", "*.zip", "*.rar", "*.7z"]
                }
            ]
        });

        var filePath = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            return filePath;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку модпака",
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<ExistingServerChoice> AskExistingServerAsync(ServerProfile profile)
    {
        var dialog = new DialogWindow<ExistingServerChoice>(
            "Сервер уже существует",
            $"Сборка \"{profile.Name}\" уже импортирована.",
            [
                ("Обновить", ExistingServerChoice.Update),
                ("Заменить", ExistingServerChoice.Replace),
                ("Отмена", ExistingServerChoice.Cancel)
            ]);
        return await dialog.ShowDialog<ExistingServerChoice>(this);
    }

    private async Task<bool> AskEulaAsync()
    {
        var dialog = new DialogWindow<bool>(
            "Minecraft EULA",
            "Вы принимаете лицензионное соглашение Minecraft Server (EULA)?",
            [("Принять", true), ("Отмена", false)]);
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task<bool> AskFullCleanupAsync()
    {
        var dialog = new DialogWindow<bool>(
            "Сброс приложения",
            "Вернуть приложение в состояние первого запуска? Будут удалены все серверы, моды, миры, логи, backups, настройки и скачанная Java.",
            [("Сбросить", true), ("Отмена", false)]);
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task OpenSettingsAsync(SettingsViewModel viewModel)
    {
        var window = new SettingsWindow(viewModel);
        await window.ShowDialog(this);
    }

    private void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    private void ShowError(string message)
    {
        _ = new DialogWindow<bool>("Ошибка", message, [("OK", true)]).ShowDialog<bool>(this);
    }

    private void OnDragOver(object? sender, DragEventArgs args)
    {
        args.DragEffects = args.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        var file = args.DataTransfer.TryGetFiles()?.FirstOrDefault();
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path) && ViewModel is not null)
        {
            await ViewModel.ImportArchiveAsync(path);
        }
    }

    private void OnServersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        RefreshAll();
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (ViewModel?.Logs.Count <= 0 || _logScrollPending)
        {
            return;
        }

        _logScrollPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (ViewModel?.Logs.Count > 0)
                {
                    _logs.ScrollIntoView(ViewModel.Logs[^1]);
                }
            }
            catch (InvalidOperationException exception)
            {
                AppLogger.Error(exception, "Log view auto-scroll failed.");
            }
            finally
            {
                _logScrollPending = false;
            }
        }, DispatcherPriority.Background);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.SelectedServer) or nameof(MainViewModel.IsBusy) or nameof(MainViewModel.BusyText))
        {
            RefreshAll();
        }

        if (args.PropertyName == nameof(MainViewModel.CommandText) && _command.Text != ViewModel?.CommandText)
        {
            _command.Text = ViewModel?.CommandText;
        }
    }

    private void RefreshAll()
    {
        var vm = ViewModel;
        var selected = vm?.SelectedServer;
        _serverCard.IsVisible = selected is not null;
        _emptyHint.IsVisible = selected is null;
        _busy.Text = vm?.IsBusy == true ? vm.BusyText : "";
        _command.TextChanged -= OnCommandChanged;
        _command.Text = vm?.CommandText ?? "";
        _command.TextChanged += OnCommandChanged;

        if (selected is null)
        {
            return;
        }

        _name.Text = selected.Name;
        _minecraft.Text = "Minecraft " + selected.MinecraftVersion;
        _loader.Text = selected.Loader;
        _status.Text = "Статус: " + selected.Status;
        _memory.ItemsSource = vm?.MemoryOptions;
        _memory.SelectedItem = selected.Profile.MemoryOption;
    }

    private void OnCommandChanged(object? sender, TextChangedEventArgs args)
    {
        if (ViewModel is not null)
        {
            ViewModel.CommandText = _command.Text ?? "";
        }
    }
}
