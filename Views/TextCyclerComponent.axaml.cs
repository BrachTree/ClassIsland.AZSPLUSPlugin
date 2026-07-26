using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.AZSMYPlugin.Models;
using ClassIsland.AZSMYPlugin.Models.ComponentSettings;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;

namespace ClassIsland.AZSMYPlugin.Views;

/// <summary>
/// 文本轮播显示组件。从 txt 文件读取句子，按设定的时长随机或顺序轮播显示。
/// 支持淡入淡出、上下滚动过渡动画，以及长文本水平滚动（仅单句）。
/// </summary>
[ComponentInfo("A3F5E2B1-7C4D-4E8F-9A2B-5D1E7F3A6B8C", "文本轮播 - AZS.Plus", "\ue8a5", "从本地 txt 文件读取句子并轮播显示。")]
public partial class TextCyclerComponent : ComponentBase<TextCyclerSettings>
{
    /// <summary>
    /// 扁平化后的显示条目
    /// </summary>
    private class DisplayEntry
    {
        public string Text { get; set; } = "";
        public Color Color { get; set; } = Colors.White;
        public double Duration { get; set; } = 5.0;
        public bool UseTransition { get; set; } = false;
        public double ScrollSpeed { get; set; } = 0;
        public bool PauseAfterScroll { get; set; } = true;
        /// <summary>
        /// 是否为独立单句（非组内句子），只有独立单句才判定长文本滚动。
        /// </summary>
        public bool IsSingleSentence { get; set; } = true;
        /// <summary>
        /// 所属帧索引。同帧的条目（前缀+组内句子）连续排列。
        /// </summary>
        public int FrameIndex { get; set; } = 0;
    }

    private readonly List<DisplayEntry> _entries = new();
    private readonly Queue<int> _randomFramePlaylist = new();
    private readonly List<int> _frameFirstIndices = new();
    private int _currentIndex = -1;
    private bool _isTransitioning = false;

    // 垂直过渡动画用的位移变换
    private TranslateTransform? _textTransform;

    // Y 属性过渡（垂直滚动/淡入淡出用）
    private static readonly Transitions YTransitions = new()
    {
        new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(300) }
    };

    // === 水平滚动状态 ===
    private readonly DispatcherTimer _scrollTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(16) // ~60fps
    };
    private DateTime _scrollStartTime;
    private double _scrollDistance = 0;
    private double _effectiveScrollSpeed = 50;
    private bool _scrollPausedAtEnd = false;
    private bool _isScrolling = false;
    private bool _isLongText = false;

    /// <summary>
    /// 长文本滚动完成后的信号。定时器检测到后切换下一条。
    /// </summary>
    private bool _scrollFinished = false;

    /// <summary>
    /// 当前长文本条目是否为暂停模式（PauseAfterScroll=true）。
    /// 暂停模式下 Timer 等滚动完成才切换；循环模式下 Timer 到期即切换。
    /// </summary>
    private bool _isPauseMode = false;

    private DispatcherTimer Timer { get; } = new()
    {
        Interval = TimeSpan.FromSeconds(5)
    };

    public TextCyclerComponent()
    {
        InitializeComponent();

        // 创建位移变换并应用到文本块
        _textTransform = new TranslateTransform();
        MainTextBlock.RenderTransform = _textTransform;

        _scrollTimer.Tick += OnScrollTick;

        AttachedToVisualTree += (_, _) =>
        {
            Settings.PropertyChanged += OnSettingsPropertyChanged;
            Settings.Frames.CollectionChanged += OnFramesCollectionChanged;

            if (!Settings.IsLoaded)
                Settings.LoadFromFile();

            RebuildEntries();
            ShowFirst();

            Timer.Tick += OnTimerTick;
            StartTimer();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            Settings.PropertyChanged -= OnSettingsPropertyChanged;
            Settings.Frames.CollectionChanged -= OnFramesCollectionChanged;
            Timer.Stop();
            Timer.Tick -= OnTimerTick;
            _scrollTimer.Stop();
            _scrollTimer.Tick -= OnScrollTick;
        };
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextCyclerSettings.SlideMode))
        {
            CreateRandomFramePlaylist();
        }
        else if (e.PropertyName == nameof(TextCyclerSettings.DefaultDuration) ||
                 e.PropertyName == nameof(TextCyclerSettings.EnableTransition) ||
                 e.PropertyName == nameof(TextCyclerSettings.AnimationType))
        {
            RebuildEntries();
            ShowFirst();
            StartTimer();
        }
        else if (e.PropertyName == nameof(TextCyclerSettings.DefaultScrollSpeed) ||
                 e.PropertyName == nameof(TextCyclerSettings.ContainerWidth))
        {
            // 默认速度或容器宽度变更：保留原始 ScrollSpeed 值，重新判定长文本并重启
            RebuildEntries();
            ShowFirst();
            StartTimer();
        }
    }

    private void OnFramesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildEntries();
        ShowFirst();
        StartTimer();
    }

    /// <summary>
    /// 从 Settings.Frames 重建扁平化显示条目列表。
    /// 同帧的条目连续排列，记录每帧的起始索引用于帧级轮播。
    /// </summary>
    private void RebuildEntries()
    {
        _entries.Clear();
        _frameFirstIndices.Clear();

        for (int fi = 0; fi < Settings.Frames.Count; fi++)
        {
            var frame = Settings.Frames[fi];
            _frameFirstIndices.Add(_entries.Count);

            // 前缀单句：IsSingleSentence = true
            if (frame.HasPrefix && !string.IsNullOrWhiteSpace(frame.Prefix!.Text))
            {
                _entries.Add(new DisplayEntry
                {
                    Text = frame.Prefix.Text,
                    Color = frame.Prefix.Color,
                    Duration = frame.Prefix.Duration > 0 ? frame.Prefix.Duration : Settings.DefaultDuration,
                    UseTransition = Settings.EnableTransition,
                    ScrollSpeed = frame.Prefix.ScrollSpeed,
                    PauseAfterScroll = frame.Prefix.PauseAfterScroll,
                    IsSingleSentence = true,
                    FrameIndex = fi
                });
            }

            // 组内句子：IsSingleSentence = false，不判定长文本
            if (frame.HasGroup)
            {
                bool groupUseTransition = Settings.EnableTransition && !frame.DisableTransition;

                foreach (var item in frame.GroupItems)
                {
                    if (string.IsNullOrWhiteSpace(item.Text))
                        continue;
                    _entries.Add(new DisplayEntry
                    {
                        Text = item.Text,
                        Color = item.Color,
                        Duration = frame.PerItemDuration > 0 ? frame.PerItemDuration : Settings.DefaultDuration,
                        UseTransition = groupUseTransition,
                        IsSingleSentence = false,
                        FrameIndex = fi
                    });
                }
            }
        }

        if (Settings.IsRandomOrder)
            CreateRandomFramePlaylist();

        _currentIndex = -1;
    }

    /// <summary>
    /// 创建帧级别的随机播放列表（打乱帧顺序，帧内句子保持原序）。
    /// </summary>
    private void CreateRandomFramePlaylist()
    {
        _randomFramePlaylist.Clear();
        if (_frameFirstIndices.Count <= 0)
            return;

        int[] frameList = new int[_frameFirstIndices.Count];
        for (int i = 0; i < _frameFirstIndices.Count; i++)
            frameList[i] = i;

        Random rand = new();
        rand.Shuffle(frameList);
        foreach (var fi in frameList)
            _randomFramePlaylist.Enqueue(fi);
    }

    /// <summary>
    /// 获取下一个条目索引。
    /// 逻辑：先检查同帧内是否有下一个条目（组内句子顺序切换），
    /// 如果没有则切换到下一个帧（随机或顺序），返回该帧的第一个条目。
    /// </summary>
    private int GetNextIndex()
    {
        if (_entries.Count == 0)
            return -1;
        if (_entries.Count == 1)
            return 0;

        // 检查同帧内是否有下一个条目
        if (_currentIndex >= 0)
        {
            int currentFrame = _entries[_currentIndex].FrameIndex;
            // 向后找同帧的下一个条目
            for (int i = _currentIndex + 1; i < _entries.Count; i++)
            {
                if (_entries[i].FrameIndex == currentFrame)
                    return i;
            }
        }

        // 当前帧已播完，切换到下一个帧
        int nextFrameFirstIndex = GetNextFrameFirstIndex();
        return nextFrameFirstIndex;
    }

    /// <summary>
    /// 获取下一个帧的第一个条目索引。
    /// </summary>
    private int GetNextFrameFirstIndex()
    {
        if (_frameFirstIndices.Count == 0)
            return 0;

        int currentFrame = _currentIndex >= 0 ? _entries[_currentIndex].FrameIndex : -1;

        if (Settings.IsRandomOrder)
        {
            if (_randomFramePlaylist.Count <= 0)
                CreateRandomFramePlaylist();
            int nextFrame = _randomFramePlaylist.Dequeue();
            // 避免连续播放同一帧
            if (nextFrame == currentFrame && _randomFramePlaylist.Count > 0)
            {
                int fallback = _randomFramePlaylist.Dequeue();
                _randomFramePlaylist.Enqueue(nextFrame);
                nextFrame = fallback;
            }
            return _frameFirstIndices[nextFrame];
        }

        // 顺序模式
        int nextFrameIdx = (currentFrame + 1) % _frameFirstIndices.Count;
        return _frameFirstIndices[nextFrameIdx];
    }

    private void ShowFirst()
    {
        StopHorizontalScroll();

        if (_entries.Count == 0)
        {
            MainTextBlock.Text = Settings.IsLoaded ? "（文件为空）" : "请选择文本文件…";
            MainTextBlock.Foreground = new SolidColorBrush(Colors.White);
            return;
        }

        if (Settings.IsRandomOrder)
        {
            if (_randomFramePlaylist.Count <= 0)
                CreateRandomFramePlaylist();
            int firstFrame = _randomFramePlaylist.Dequeue();
            _currentIndex = _frameFirstIndices[firstFrame];
        }
        else
        {
            _currentIndex = 0;
        }

        ApplyEntry(_entries[_currentIndex]);
        _ = StartHorizontalScrollIfNeededAsync(_entries[_currentIndex]);
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isTransitioning)
            return;
        if (_entries.Count == 0)
            return;

        // 长文本暂停模式：滚动未完成时跳过切换，等滚动完成后 Timer 才放行
        // 长文本循环模式：不阻塞，显示时长到期即切换
        if (_isLongText && _isPauseMode && !_scrollFinished)
            return;

        _currentIndex = GetNextIndex();
        if (_currentIndex < 0 || _currentIndex >= _entries.Count)
            return;

        var entry = _entries[_currentIndex];
        await ApplyEntryAsync(entry);
    }

    // ========================================
    //  垂直过渡动画
    // ========================================

    private double GetVerticalScrollDistance()
    {
        double h = MainTextBlock.Bounds.Height;
        if (h > 0)
            return h + 10;
        return Settings.FontSize * 1.5;
    }

    private void SetYAnimated(double value)
    {
        if (_textTransform == null) return;
        _textTransform.Transitions = YTransitions;
        _textTransform.Y = value;
    }

    private void SetYInstant(double value)
    {
        if (_textTransform == null) return;
        _textTransform.Transitions = null;
        _textTransform.Y = value;
    }

    private void SetXInstant(double value)
    {
        if (_textTransform == null) return;
        _textTransform.X = value;
    }

    private async Task ApplyEntryAsync(DisplayEntry entry)
    {
        _isTransitioning = true;
        Timer.Stop();
        StopHorizontalScroll();

        try
        {
            if (!entry.UseTransition)
            {
                SetYInstant(0);
                SetXInstant(0);
                MainTextBlock.Text = entry.Text;
                MainTextBlock.Foreground = new SolidColorBrush(entry.Color);
                MainTextBlock.Opacity = 1;
                await Task.Delay(50);
            }
            else
            {
                switch (Settings.AnimationType)
                {
                    case 1:
                        await ApplyScrollVerticalAsync(entry, scrollUp: true);
                        break;
                    case 2:
                        await ApplyScrollVerticalAsync(entry, scrollUp: false);
                        break;
                    default:
                        await ApplyFadeAsync(entry);
                        break;
                }
            }
        }
        finally
        {
            _isTransitioning = false;
        }

        // 启动水平滚动（仅独立单句判定长文本）
        await StartHorizontalScrollIfNeededAsync(entry);

        // 重新启动定时器
        Timer.Interval = TimeSpan.FromSeconds(Math.Max(0.5, entry.Duration));
        Timer.Start();
    }

    private async Task ApplyFadeAsync(DisplayEntry entry)
    {
        SetYInstant(0);
        SetXInstant(0);
        MainTextBlock.Opacity = 0;
        await Task.Delay(300);
        MainTextBlock.Text = entry.Text;
        MainTextBlock.Foreground = new SolidColorBrush(entry.Color);
        MainTextBlock.Opacity = 1;
        await Task.Delay(300);
    }

    private async Task ApplyScrollVerticalAsync(DisplayEntry entry, bool scrollUp)
    {
        double scrollDist = GetVerticalScrollDistance();
        MainTextBlock.Opacity = 1;

        // Step 1: 旧文本滚出
        double outY = scrollUp ? -scrollDist : scrollDist;
        SetYAnimated(outY);
        await Task.Delay(300);

        // Step 2: 更新内容
        MainTextBlock.Text = entry.Text;
        MainTextBlock.Foreground = new SolidColorBrush(entry.Color);

        // Step 3: 瞬间定位到对面
        double inY = scrollUp ? scrollDist : -scrollDist;
        SetYInstant(inY);
        SetXInstant(0);
        await Task.Delay(50);

        // Step 4: 滚入
        SetYAnimated(0);
        await Task.Delay(300);
    }

    // ========================================
    //  水平滚动（仅独立单句）
    // ========================================

    /// <summary>
    /// 测量文本的自然宽度（不受容器约束）。
    /// </summary>
    private double MeasureTextWidth()
    {
        MainTextBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return MainTextBlock.DesiredSize.Width;
    }

    /// <summary>
    /// 仅对独立单句判定长文本并启动滚动。组内句子不判定。
    /// 判定标准：文本自然宽度超过组件实际渲染宽度。
    /// </summary>
    private async Task StartHorizontalScrollIfNeededAsync(DisplayEntry entry)
    {
        _isLongText = false;
        _scrollFinished = false;
        _isPauseMode = false;

        // 先清除容器固定宽度，让 ScrollViewer 自适应内容
        Dispatcher.UIThread.Post(() => { ContainerScroll.Width = double.NaN; });

        // 组内句子不判定长文本
        if (!entry.IsSingleSentence)
        {
            SetXInstant(0);
            _isScrolling = false;
            return;
        }

        // 等待布局完成
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        // 使用组件实际渲染宽度作为判定基准；若尚未布局则回退到设置值
        double containerWidth = Bounds.Width > 0 ? Bounds.Width : Settings.ContainerWidth;
        double textWidth = MeasureTextWidth();

        if (textWidth <= containerWidth + 1)
        {
            // 短文本：不需要滚动，ScrollViewer 自适应文本宽度
            SetXInstant(0);
            _isScrolling = false;
            return;
        }

        // 长文本：固定 ScrollViewer 宽度作为视窗，TextBlock 完整渲染后通过 TranslateTransform 移动
        // ClipToBounds 裁剪超出 Viewport 的内容
        Dispatcher.UIThread.Post(() => { ContainerScroll.Width = containerWidth; });

        // 等待宽度生效后重新测量
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        textWidth = MeasureTextWidth();

        _isLongText = true;
        _isPauseMode = entry.PauseAfterScroll;
        _scrollDistance = textWidth - containerWidth;
        // ScrollSpeed=0 表示文件中未指定，使用组件设置中的默认速度
        _effectiveScrollSpeed = entry.ScrollSpeed > 0 ? entry.ScrollSpeed : Settings.DefaultScrollSpeed;
        _scrollPausedAtEnd = false;
        _scrollStartTime = DateTime.UtcNow;
        _isScrolling = true;

        SetXInstant(0);

        _scrollTimer.Interval = TimeSpan.FromMilliseconds(16);
        _scrollTimer.Start();
    }

    private void StopHorizontalScroll()
    {
        _scrollTimer.Stop();
        _isScrolling = false;
        _isLongText = false;
        _scrollPausedAtEnd = false;
        _scrollFinished = false;
        _isPauseMode = false;
        SetXInstant(0);
        // 清除固定宽度，让下一次重新设置
        Dispatcher.UIThread.Post(() => { ContainerScroll.Width = double.NaN; });
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        if (!_isScrolling || _textTransform == null)
            return;

        if (_scrollPausedAtEnd)
            return;

        var now = DateTime.UtcNow;
        double elapsed = (now - _scrollStartTime).TotalSeconds;
        double offset = elapsed * _effectiveScrollSpeed;

        if (offset >= _scrollDistance)
        {
            SetXInstant(-_scrollDistance);

            if (_entries.Count > 0 && _currentIndex >= 0 && _currentIndex < _entries.Count)
            {
                var entry = _entries[_currentIndex];
                if (entry.PauseAfterScroll)
                {
                    // 暂停模式：滚动完成，从此时开始重新计时显示时长
                    _scrollPausedAtEnd = true;
                    _scrollFinished = true;
                    // 重新启动 Timer，让显示时长从滚动完成开始算
                    Timer.Stop();
                    Timer.Interval = TimeSpan.FromSeconds(Math.Max(0.5, entry.Duration));
                    Timer.Start();
                }
                else
                {
                    // 循环模式：等1秒后重新开始
                    _scrollPausedAtEnd = true;
                    _ = ResetScrollAfterDelayAsync(1000);
                }
            }
        }
        else
        {
            SetXInstant(-offset);
        }
    }

    private async Task ResetScrollAfterDelayAsync(int delayMs)
    {
        await Task.Delay(delayMs);
        if (!_isScrolling)
            return;

        _scrollStartTime = DateTime.UtcNow;
        _scrollPausedAtEnd = false;
        SetXInstant(0);
    }

    private void ApplyEntry(DisplayEntry entry)
    {
        SetYInstant(0);
        SetXInstant(0);
        MainTextBlock.Text = entry.Text;
        MainTextBlock.Foreground = new SolidColorBrush(entry.Color);
        MainTextBlock.Opacity = 1;
    }

    private void StartTimer()
    {
        if (_entries.Count == 0)
            return;

        double duration = 5.0;
        if (_currentIndex >= 0 && _currentIndex < _entries.Count)
            duration = _entries[_currentIndex].Duration;

        Timer.Interval = TimeSpan.FromSeconds(Math.Max(0.5, duration));
        Timer.Stop();
        Timer.Start();
    }
}
