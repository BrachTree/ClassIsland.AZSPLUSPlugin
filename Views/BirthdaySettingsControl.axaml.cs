using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClassIsland.AZSMYPlugin.Models.ComponentSettings;
using ClassIsland.Core.Abstractions.Controls;

namespace ClassIsland.AZSMYPlugin.Views;

/// <summary>
/// 生日显示组件的设置界面。
/// </summary>
public partial class BirthdaySettingsControl : ComponentBase<BirthdaySettings>
{
    public BirthdaySettingsControl()
    {
        InitializeComponent();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择生日表格文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("表格文件")
                {
                    Patterns = new[] { "*.xlsx", "*.csv", "*.txt", "*.tsv" },
                    MimeTypes = new[] { "text/plain", "text/csv" }
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = new[] { "*.*" },
                    MimeTypes = new[] { "*/*" }
                }
            }
        });

        if (files.Count > 0)
        {
            Settings.FilePath = files[0].Path.LocalPath;
            Settings.LoadFromFile();
        }
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        Settings.LoadFromFile();
    }

    private void OnNameSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int idx = NamesCombo.SelectedIndex;
        if (idx >= 0 && idx < Settings.Entries.Count)
        {
            var entry = Settings.Entries[idx];
            BirthdayTextBlock.Text = $"{entry.Month}月{entry.Day}日";
        }
        else
        {
            BirthdayTextBlock.Text = "";
        }
    }
}
