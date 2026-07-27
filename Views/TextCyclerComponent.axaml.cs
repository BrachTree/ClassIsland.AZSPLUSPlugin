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
/// 支持淡入淡出、上下左右滚动过渡动画，以及长文本水平滚动（仅单句）。
/// </summary>
[ComponentInfo("A3F5E2B1-7C4D-4E8F-9A2B-5D1E7F3A6B8C", "文本轮播 - AZS.Plus", "\ue8a5", "从本地 txt 文件读取文本并轮播显示。支持单句/组句/混合句式、五种过渡动画（淡入淡出、上下左右滚动）、长文本水平滚动（<long> 标记）、自定义颜色与时长、随机/顺序轮播。早期版本，可能存在些许问题。此插件由 AI 开发。")]
public partial class TextCyclerComponent : ComponentBase<TextCyclerSettings>
{
    private class DisplayEntry
    {
        public string Text { get; set; } = "";
        public Color Color { get; set; } = Colors.White;
        public double Duration { get; set; } = 5.0;
        public bool UseTransition { get; set; } = false;
        public double ScrollSpeed { get; set; } = 0;
        public bool PauseAfterScroll { get; set; } = true;
        public bool IsSingleSentence { get; set; } = true;
        public int FrameIndex { get; set; } = 0;
        /// <summary>
        /// 紧贴前缀文本（固定显示），null 表示无前缀。
        /// </summary>
        public string? AttachedPrefixText { get; set; }
        public Color AttachedPrefixColor { get; set; } = Colors.White;
        /// <summary>
        /// 该条目是否属于同一帧内且紧贴前缀帧的组内条目（前缀应保持显示）。
        /// </summary>
        public bool ShowAttachedPrefix { get; set; } = false;
        /// <summary>
        /// 动画类型覆盖。null/空 = 使用组件设置，A-E = 指定动画类型。
        /// </summary>
        public string? AnimationTypeOverride { get; set; }
        /// <summary>
        /// 是否为长文本（需水平滚动）。通过 &lt;long&gt; 参数显式指定。
        /// </summary>
        public bool IsLongText { get; set; } = false;
    }

    private readonly List<DisplayEntry> _entries = new();
    private readonly Queue<int> _randomFramePlaylist = new();
    private readonly List<int> _frameFirstIndices = new();
    private int _currentIndex = -1;
    private bool _isTransitioning = false;

    private TranslateTransform? _textTransform;
    private static readonly Transitions YTransitions = new()
    {
        new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(300) }
    };
    private static readonly Transitions XTransitions = new()
    {
        new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(300) }
    };
    private static readonly Transitions XYTransitions = new()
    {
        new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(300) },
        new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(300) }
    };

    // 水平滚动状态
    private readonly DispatcherTimer _scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _scrollStartTime;
    private double _scrollDistance = 0;
    private double _effectiveScrollSpeed = 50;
    private bool _scrollPausedAtEnd = false;
    private bool _isScrolling = false;
    private bool _isLongText = false;
    private bool _scrollFinished = false;
    private bool _isPauseMode = false;

    // 当前显示的紧贴前缀信息
    private string? _currentAttachedPrefixText;
    private Color _currentAttachedPrefixColor = Colors.White;

    private DispatcherTimer Timer { get; } = new() { Interval = TimeSpan.FromSeconds(5) };

    public TextCyclerComponent()
    {
        InitializeComponent();
        _textTransform = new TranslateTransform();
        MainTextBlock.RenderTransform = _textTransform;
        _scrollTimer.Tick += OnScrollTick;

        AttachedToVisualTree += (_, _) =>
        {
            Settings.PropertyChanged += OnSettingsPropertyChanged;
            Settings.Frames.CollectionChanged += OnFramesCollectionChanged;
            if (!Settings.IsLoaded) Settings.LoadFromFile();
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
            CreateRandomFramePlaylist();
        else if (e.PropertyName == nameof(TextCyclerSettings.DefaultDuration) ||
                 e.PropertyName == nameof(TextCyclerSettings.EnableTransition) ||
                 e.PropertyName == nameof(TextCyclerSettings.AnimationType))
        {
            RebuildEntries(); ShowFirst(); StartTimer();
        }
        else if (e.PropertyName == nameof(TextCyclerSettings.DefaultScrollSpeed) ||
                 e.PropertyName == nameof(TextCyclerSettings.ContainerWidth))
        {
            RebuildEntries(); ShowFirst(); StartTimer();
        }
    }

    private void OnFramesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildEntries(); ShowFirst(); StartTimer();
    }

    private void RebuildEntries()
    {
        _entries.Clear();
        _frameFirstIndices.Clear();

        for (int fi = 0; fi < Settings.Frames.Count; fi++)
        {
            var frame = Settings.Frames[fi];
            _frameFirstIndices.Add(_entries.Count);

            // 紧贴前缀
            string? attachedText = null;
            Color attachedColor = Colors.White;
            if (frame.HasAttachedPrefix)
            {
                attachedText = frame.AttachedPrefix!.Text;
                attachedColor = frame.AttachedPrefix.Color;
            }

            // 前缀单句
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
                    FrameIndex = fi,
                    IsLongText = frame.Prefix.IsLongText
                });
            }

            // 组内句子
            if (frame.HasGroup)
            {
                bool groupUseTransition = Settings.EnableTransition && !frame.DisableTransition;
                foreach (var item in frame.GroupItems)
                {
                    if (string.IsNullOrWhiteSpace(item.Text)) continue;
                    _entries.Add(new DisplayEntry
                    {
                        Text = item.Text,
                        Color = item.Color,
                        Duration = frame.PerItemDuration > 0 ? frame.PerItemDuration : Settings.DefaultDuration,
                        UseTransition = groupUseTransition,
                        IsSingleSentence = false,
                        FrameIndex = fi,
                        AttachedPrefixText = attachedText,
                        AttachedPrefixColor = attachedColor,
                        ShowAttachedPrefix = attachedText != null,
                        AnimationTypeOverride = frame.GroupAnimationType
                    });
                }
            }
        }

        if (Settings.IsRandomOrder) CreateRandomFramePlaylist();
        _currentIndex = -1;
    }

    private void CreateRandomFramePlaylist()
    {
        _randomFramePlaylist.Clear();
        if (_frameFirstIndices.Count <= 0) return;
        int[] frameList = new int[_frameFirstIndices.Count];
        for (int i = 0; i < _frameFirstIndices.Count; i++) frameList[i] = i;
        Random rand = new();
        rand.Shuffle(frameList);
        foreach (var fi in frameList) _randomFramePlaylist.Enqueue(fi);
    }

    private int GetNextIndex()
    {
        if (_entries.Count == 0) return -1;
        if (_entries.Count == 1) return 0;

        if (_currentIndex >= 0)
        {
            int currentFrame = _entries[_currentIndex].FrameIndex;
            for (int i = _currentIndex + 1; i < _entries.Count; i++)
                if (_entries[i].FrameIndex == currentFrame)
                    return i;
        }
        return GetNextFrameFirstIndex();
    }

    private int GetNextFrameFirstIndex()
    {
        if (_frameFirstIndices.Count == 0) return 0;
        int currentFrame = _currentIndex >= 0 ? _entries[_currentIndex].FrameIndex : -1;
        if (Settings.IsRandomOrder)
        {
            if (_randomFramePlaylist.Count <= 0) CreateRandomFramePlaylist();
            int nextFrame = _randomFramePlaylist.Dequeue();
            if (nextFrame == currentFrame && _randomFramePlaylist.Count > 0)
            {
                int fallback = _randomFramePlaylist.Dequeue();
                _randomFramePlaylist.Enqueue(nextFrame);
                nextFrame = fallback;
            }
            return _frameFirstIndices[nextFrame];
        }
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
            PrefixTextBlock.IsVisible = false;
            return;
        }

        if (Settings.IsRandomOrder)
        {
            if (_randomFramePlaylist.Count <= 0) CreateRandomFramePlaylist();
            int firstFrame = _randomFramePlaylist.Dequeue();
            _currentIndex = _frameFirstIndices[firstFrame];
        }
        else
            _currentIndex = 0;

        ApplyEntry(_entries[_currentIndex]);
        _ = StartHorizontalScrollIfNeededAsync(_entries[_currentIndex]);
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isTransitioning || _entries.Count == 0) return;
        if (_isLongText && _isPauseMode && !_scrollFinished) return;

        // 安全校验：如果状态标记为长文本但当前条目不是，则重置状态
        if (_isLongText && _currentIndex >= 0 && _currentIndex < _entries.Count && !_entries[_currentIndex].IsLongText)
        {
            StopHorizontalScroll();
        }

        _currentIndex = GetNextIndex();
        if (_currentIndex < 0 || _currentIndex >= _entries.Count) return;
        await ApplyEntryAsync(_entries[_currentIndex]);
    }

    // === 前缀显示 ===

    private void UpdateAttachedPrefix(string? text, Color color)
    {
        _currentAttachedPrefixText = text;
        _currentAttachedPrefixColor = color;
        if (text != null)
        {
            PrefixTextBlock.Text = text;
            PrefixTextBlock.Foreground = new SolidColorBrush(color);
            PrefixTextBlock.IsVisible = true;
        }
        else
        {
            PrefixTextBlock.IsVisible = false;
        }
    }

    // === 垂直过渡动画 ===

    private double GetVerticalScrollDistance()
    {
        double h = MainTextBlock.Bounds.Height;
        return h > 0 ? h + 10 : Settings.FontSize * 1.5;
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
        _textTransform.Transitions = null;
        _textTransform.X = value;
    }

    private void SetXAnimated(double value, int ms)
    {
        if (_textTransform == null) return;
        // 同步 X 过渡时长
        foreach (var t in XTransitions)
            if (t is DoubleTransition dt) dt.Duration = TimeSpan.FromMilliseconds(ms);
        _textTransform.Transitions = XTransitions;
        _textTransform.X = value;
    }

    private void SetXYInstant(double x, double y)
    {
        if (_textTransform == null) return;
        _textTransform.Transitions = null;
        _textTransform.X = x;
        _textTransform.Y = y;
    }

    /// <summary>
    /// 根据条目时长计算过渡动画时长。
    /// 每步动画时长 = 显示时长的25%，总动画 = 2步 = 50%。
    /// 最小30ms保证极短时长也有动画，最大400ms避免长时长动画过慢。
    /// </summary>
    private int GetTransitionDurationMs(double entryDuration)
    {
        int maxMs = (int)(entryDuration * 1000 * 0.25);
        if (maxMs < 30) maxMs = 30;
        if (maxMs > 400) maxMs = 400;
        return maxMs;
    }

    /// <summary>
    /// 解析动画类型。优先使用条目覆盖，否则使用组件设置。
    /// A=淡入淡出(0)，B=向上滚动(1)，C=向下滚动(2)，D=向左滚动(3)，E=向右滚动(4)
    /// </summary>
    private int ResolveAnimationType(DisplayEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.AnimationTypeOverride))
        {
            return entry.AnimationTypeOverride.ToUpperInvariant() switch
            {
                "A" => 0,
                "B" => 1,
                "C" => 2,
                "D" => 3,
                "E" => 4,
                _ => Settings.AnimationType
            };
        }
        return Settings.AnimationType;
    }

    private async Task ApplyEntryAsync(DisplayEntry entry)
    {
        _isTransitioning = true;
        Timer.Stop();
        StopHorizontalScroll();

        try
        {
            // 更新紧贴前缀
            UpdateAttachedPrefix(entry.ShowAttachedPrefix ? entry.AttachedPrefixText : null,
                                  entry.ShowAttachedPrefix ? entry.AttachedPrefixColor : Colors.White);

            int transMs = GetTransitionDurationMs(entry.Duration);

            // 立即启动 Timer，显示时长包含动画时间
            Timer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, entry.Duration));
            Timer.Start();

            if (!entry.UseTransition)
            {
                SetXYInstant(0, 0);
                MainTextBlock.Text = entry.Text;
                MainTextBlock.Foreground = new SolidColorBrush(entry.Color);
                MainTextBlock.Opacity = 1;
                await Task.Delay(30);
            }
            else
            {
                int animType = ResolveAnimationType(entry);
                switch (animType)
                {
                    case 1: await ApplyScrollVerticalAsync(entry, true, transMs); break;
                    case 2: await ApplyScrollVerticalAsync(entry, false, transMs); break;
                    case 3: await ApplyScrollHorizontalAsync(entry, true, transMs); break;
                    case 4: await ApplyScrollHorizontalAsync(entry, false, transMs); break;
                    default: await ApplyFadeAsync(entry, transMs); break;
                }
            }
        }
        finally { _isTransitioning = false; }

        await StartHorizontalScrollIfNeededAsync(entry);
    }

    private async Task ApplyFadeAsync(DisplayEntry entry, int transMs)
    {
        SetXYInstant(0, 0);
        // 同步 opacity 过渡时长与 transMs
        if (MainTextBlock.Transitions != null)
        {
            foreach (var t in MainTextBlock.Transitions)
            {
                if (t is DoubleTransition dt)
                    dt.Duration = TimeSpan.FromMilliseconds(transMs);
            }
        }
        MainTextBlock.Opacity = 0;
        await Task.Delay(transMs);
        MainTextBlock.Text = entry.Text;
        MainTextBlock.Foreground = new SolidColorBrush(entry.Color);
        MainTextBlock.Opacity = 1;
        await Task.Delay(transMs);
    }

    private async Task ApplyScrollVerticalAsync(DisplayEntry entry, bool scrollUp, int transMs)
    {
        double scrollDist = GetVerticalScrollDistance();
        MainTextBlock.Opacity = 1;

        // 同步 Y 过渡时长
        foreach (var t in YTransitions)
            if (t is DoubleTransition dt) dt.Duration = TimeSpan.FromMilliseconds(transMs);

        // Step 1: 旧文本滚出
        double outY = scrollUp ? -scrollDist : scrollDist;
        SetYAnimated(outY);
        await Task.Delay(transMs);

        // Step 2: 瞬间切换文本并定位到对面
        MainTextBlock.Text = entry.Text;
        MainTextBlock.Foreground = new SolidColorBrush(entry.Color);
        SetXYInstant(0, scrollUp ? scrollDist : -scrollDist);

        // Step 3: 新文本滚入到中心
        SetYAnimated(0);
        await Task.Delay(transMs);

        // 动画完成后清除过渡，避免干扰后续滚动
        if (_textTransform != null) _textTransform.Transitions = null;
    }

    /// <summary>
    /// 无缝水平滚动过渡：旧文本向左/右滚出，新文本从另一侧滚入。
    /// </summary>
    private async Task ApplyScrollHorizontalAsync(DisplayEntry entry, bool scrollLeft, int transMs)
    {
        double scrollDist = GetHorizontalScrollDistance();
        MainTextBlock.Opacity = 1;

        // Step 1: 旧文本向指定方向滚出
        double outX = scrollLeft ? -scrollDist : scrollDist;
        SetXAnimated(outX, transMs);
        await Task.Delay(transMs);

        // Step 2: 瞬间切换文本并定位到对面
        MainTextBlock.Text = entry.Text;
        MainTextBlock.Foreground = new SolidColorBrush(entry.Color);
        SetXYInstant(scrollLeft ? scrollDist : -scrollDist, 0);

        // Step 3: 新文本从另一侧滚入到中心
        SetXAnimated(0, transMs);
        await Task.Delay(transMs);

        // 动画完成后清除过渡，避免干扰后续水平滚动
        if (_textTransform != null) _textTransform.Transitions = null;
    }

    /// <summary>
    /// 获取水平滚动距离（使用容器宽度）
    /// </summary>
    private double GetHorizontalScrollDistance()
    {
        double w = ContainerScroll.Bounds.Width;
        return w > 0 ? w + 10 : Settings.ContainerWidth + 10;
    }

    // === 水平滚动（仅独立单句） ===

    private double MeasureTextWidth()
    {
        MainTextBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return MainTextBlock.DesiredSize.Width;
    }

    private async Task StartHorizontalScrollIfNeededAsync(DisplayEntry entry)
    {
        _isLongText = false;
        _scrollFinished = false;
        _isPauseMode = false;
        _scrollDistance = 0;
        _effectiveScrollSpeed = 0;
        // 清除可能残留的过渡动画，避免干扰水平滚动
        if (_textTransform != null) _textTransform.Transitions = null;
        Dispatcher.UIThread.Post(() => { ContainerScroll.MaxWidth = double.PositiveInfinity; });

        if (!entry.IsSingleSentence) { SetXInstant(0); _isScrolling = false; return; }

        // 长文本判定：仅当显式标记 <long> 时才滚动
        if (!entry.IsLongText)
        {
            SetXInstant(0); _isScrolling = false; return;
        }

        double containerWidth = Settings.ContainerWidth;

        // 限制 ScrollViewer 宽度并等待布局完成
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ContainerScroll.MaxWidth = containerWidth;
            ContainerScroll.InvalidateMeasure();
        });

        // 等待布局渲染完成
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

        double textWidth = MeasureTextWidth();

        // 标记 <long> 的文本一律滚动，无论是否超过容器宽度
        _isLongText = true;
        _isPauseMode = entry.PauseAfterScroll;
        _scrollDistance = Math.Max(0, textWidth - containerWidth);
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
        _isScrolling = false; _isLongText = false;
        _scrollPausedAtEnd = false; _scrollFinished = false; _isPauseMode = false;
        _scrollDistance = 0; _effectiveScrollSpeed = 0;
        SetXInstant(0);
        // 清除残留过渡动画
        if (_textTransform != null) _textTransform.Transitions = null;
        Dispatcher.UIThread.Post(() => { ContainerScroll.MaxWidth = double.PositiveInfinity; });
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        if (!_isScrolling || _textTransform == null || _scrollPausedAtEnd) return;

        // 安全校验：当前条目是否真的是长文本，防止状态错乱导致非长文本被滚动
        if (_currentIndex >= 0 && _currentIndex < _entries.Count && !_entries[_currentIndex].IsLongText)
        {
            StopHorizontalScroll();
            return;
        }

        double elapsed = (DateTime.UtcNow - _scrollStartTime).TotalSeconds;
        double offset = elapsed * _effectiveScrollSpeed;

        if (offset >= _scrollDistance)
        {
            SetXInstant(-_scrollDistance);
            if (_entries.Count > 0 && _currentIndex >= 0 && _currentIndex < _entries.Count)
            {
                var entry = _entries[_currentIndex];
                if (entry.PauseAfterScroll)
                {
                    _scrollTimer.Stop(); // 滚动完成，停止滚动定时器
                    _scrollPausedAtEnd = true; _scrollFinished = true;
                    Timer.Stop();
                    Timer.Interval = TimeSpan.FromSeconds(Math.Max(0.5, entry.Duration));
                    Timer.Start();
                }
                else
                {
                    _scrollPausedAtEnd = true;
                    _ = ResetScrollAfterDelayAsync(1000);
                }
            }
        }
        else SetXInstant(-offset);
    }

    private async Task ResetScrollAfterDelayAsync(int delayMs)
    {
        await Task.Delay(delayMs);
        if (!_isScrolling) return;
        _scrollStartTime = DateTime.UtcNow;
        _scrollPausedAtEnd = false;
        SetXInstant(0);
    }

    private void ApplyEntry(DisplayEntry entry)
    {
        SetXYInstant(0, 0);
        UpdateAttachedPrefix(entry.ShowAttachedPrefix ? entry.AttachedPrefixText : null,
                              entry.ShowAttachedPrefix ? entry.AttachedPrefixColor : Colors.White);
        MainTextBlock.Text = entry.Text;
        MainTextBlock.Foreground = new SolidColorBrush(entry.Color);
        MainTextBlock.Opacity = 1;
    }

    private void StartTimer()
    {
        if (_entries.Count == 0) return;
        double duration = 5.0;
        if (_currentIndex >= 0 && _currentIndex < _entries.Count)
            duration = _entries[_currentIndex].Duration;
        Timer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, duration));
        Timer.Stop();
        Timer.Start();
    }
}
