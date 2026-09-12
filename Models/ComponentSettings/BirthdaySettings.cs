using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using ClassIsland.AZSMYPlugin.Helpers;
using ClassIsland.AZSMYPlugin.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AZSMYPlugin.Models.ComponentSettings;

/// <summary>
/// 生日显示组件的设置。
/// </summary>
public class BirthdaySettings : ObservableRecipient
{
    private string _filePath = "";
    private int _maxNamesToShow = 5;
    private bool _useGradientColor = false;
    private int _fontSize = 16;
    private bool _hideWhenNoBirthday = false;
    private string _loadStatus = "未选择文件";

    /// <summary>
    /// 生日表格文件路径（.xlsx / .csv / .txt / .tsv）。
    /// </summary>
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (value == _filePath) return;
            _filePath = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 最多显示多少个名字。超过时直接不显示多余名字。
    /// </summary>
    public int MaxNamesToShow
    {
        get => _maxNamesToShow;
        set
        {
            if (value == _maxNamesToShow) return;
            _maxNamesToShow = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 是否使用彩色渐变文字。
    /// </summary>
    public bool UseGradientColor
    {
        get => _useGradientColor;
        set
        {
            if (value == _useGradientColor) return;
            _useGradientColor = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 字体大小。
    /// </summary>
    public int FontSize
    {
        get => _fontSize;
        set
        {
            if (value == _fontSize) return;
            _fontSize = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 没有人生日时是否隐藏组件。
    /// </summary>
    public bool HideWhenNoBirthday
    {
        get => _hideWhenNoBirthday;
        set
        {
            if (value == _hideWhenNoBirthday) return;
            _hideWhenNoBirthday = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 加载状态信息（不序列化）。
    /// </summary>
    [JsonIgnore]
    public string LoadStatus
    {
        get => _loadStatus;
        set
        {
            if (value == _loadStatus) return;
            _loadStatus = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 从文件读取到的姓名列表（供设置界面查看）。
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<string> Names { get; } = new();

    /// <summary>
    /// 从文件解析出的生日列表（不序列化，运行时加载）。
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<BirthdayEntry> Entries { get; } = new();

    /// <summary>
    /// 当前是否已成功加载文件。
    /// </summary>
    [JsonIgnore]
    public bool IsLoaded => Entries.Count > 0;

    /// <summary>
    /// 从指定文件路径重新加载并解析生日表格。
    /// </summary>
    public bool LoadFromFile()
    {
        Entries.Clear();
        Names.Clear();

        if (string.IsNullOrWhiteSpace(FilePath))
        {
            LoadStatus = "未选择文件";
            return false;
        }

        if (!File.Exists(FilePath))
        {
            LoadStatus = $"文件不存在：{Path.GetFileName(FilePath)}";
            return false;
        }

        try
        {
            var entries = BirthdayFileParser.Parse(FilePath);
            foreach (var e in entries)
            {
                Entries.Add(e);
                Names.Add(e.Name);
            }

            LoadStatus = $"已加载 {Entries.Count} 条生日";
            return Entries.Count > 0;
        }
        catch (Exception ex)
        {
            LoadStatus = $"加载失败：{ex.Message}";
            return false;
        }
    }
}
