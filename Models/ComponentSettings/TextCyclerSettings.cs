using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Avalonia.Media;
using ClassIsland.AZSMYPlugin.Helpers;
using ClassIsland.AZSMYPlugin.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AZSMYPlugin.Models.ComponentSettings;

/// <summary>
/// 文本轮播组件的设置。
/// </summary>
public class TextCyclerSettings : ObservableRecipient
{
    private string _filePath = "";
    private int _slideMode = 0;
    private int _fontSize = 16;
    private double _defaultDuration = 5.0;
    private string _loadStatus = "未选择文件";
    private bool _enableTransition = false;
    private int _animationType = 0;
    private double _defaultScrollSpeed = 50.0;
    private int _containerWidth = 350;

    /// <summary>
    /// txt 文件路径
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
    /// 轮播方式。0=随机，1=顺序
    /// </summary>
    public int SlideMode
    {
        get => _slideMode;
        set
        {
            if (value == _slideMode) return;
            _slideMode = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 是否随机顺序显示（兼容属性）
    /// </summary>
    [JsonIgnore]
    public bool IsRandomOrder => SlideMode == 0;

    /// <summary>
    /// 字体大小
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
    /// 默认显示时长（秒），当文件中未指定时使用
    /// </summary>
    public double DefaultDuration
    {
        get => _defaultDuration;
        set
        {
            if (value == _defaultDuration) return;
            _defaultDuration = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 是否全局启用过渡动画。开启后所有句子默认使用过渡动画，
    /// 除非组内明确指定禁用。
    /// </summary>
    public bool EnableTransition
    {
        get => _enableTransition;
        set
        {
            if (value == _enableTransition) return;
            _enableTransition = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 过渡动画类型。0=淡入淡出，1=向上滚动，2=向下滚动
    /// </summary>
    public int AnimationType
    {
        get => _animationType;
        set
        {
            if (value == _animationType) return;
            _animationType = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 默认水平滚动速度（px/秒）。当文件中未指定滚动速度时使用。
    /// 仅当文本超出容器宽度时生效。
    /// </summary>
    public double DefaultScrollSpeed
    {
        get => _defaultScrollSpeed;
        set
        {
            if (value == _defaultScrollSpeed) return;
            _defaultScrollSpeed = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 文本容器宽度（px）。固定为 350px。
    /// </summary>
    public int ContainerWidth
    {
        get => _containerWidth;
        set
        {
            if (value == _containerWidth) return;
            _containerWidth = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 加载状态信息（不序列化）
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
    /// 从文件解析出的显示帧列表（不序列化，运行时加载）
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<DisplayFrame> Frames { get; } = new();

    /// <summary>
    /// 当前是否已成功加载文件
    /// </summary>
    [JsonIgnore]
    public bool IsLoaded => Frames.Count > 0;

    /// <summary>
    /// 从指定文件路径重新加载并解析文本。
    /// </summary>
    /// <returns>是否加载成功</returns>
    public bool LoadFromFile()
    {
        Frames.Clear();

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
            var content = File.ReadAllText(FilePath);
            var frames = TextFileParser.Parse(content);
            foreach (var f in frames)
                Frames.Add(f);

            LoadStatus = $"已加载 {Frames.Count} 条";
            return Frames.Count > 0;
        }
        catch (Exception ex)
        {
            LoadStatus = $"加载失败：{ex.Message}";
            return false;
        }
    }
}
