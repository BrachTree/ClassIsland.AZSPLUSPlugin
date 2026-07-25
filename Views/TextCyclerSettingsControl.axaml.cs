using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClassIsland.AZSMYPlugin.Models.ComponentSettings;
using ClassIsland.Core.Abstractions.Controls;

namespace ClassIsland.AZSMYPlugin.Views;

/// <summary>
/// 文本轮播组件的设置界面。
/// </summary>
public partial class TextCyclerSettingsControl : ComponentBase<TextCyclerSettings>
{
    public TextCyclerSettingsControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 点击"浏览"按钮，打开文件选择器。
    /// </summary>
    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择文本文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("文本文件")
                {
                    Patterns = new[] { "*.txt" },
                    MimeTypes = new[] { "text/plain" }
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

    /// <summary>
    /// 点击"刷新"按钮，重新加载文件内容。
    /// </summary>
    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        Settings.LoadFromFile();
    }
}
