using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.AZSMYPlugin.Models;
using ClassIsland.AZSMYPlugin.Models.ComponentSettings;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;

namespace ClassIsland.AZSMYPlugin.Views;

/// <summary>
/// 生日显示组件。从表格文档导入生日，显示今日过生日的人数与名字。
/// </summary>
[ComponentInfo("D4A5E6B7-C8F9-4D0A-B1C2-E3F4A5B6C7D8", "生日显示 - AZS.Plus", "\uE8AC",
    "从表格文档导入生日，显示今日过生日的人数与名字。支持 CSV / Excel(.xlsx)，可设置最多显示人数、无人生日时隐藏与彩色渐变文字。")]
public partial class BirthdayComponent : ComponentBase<BirthdaySettings>
{
    private static readonly SolidColorBrush DefaultBrush = new(Colors.White);
    private static readonly LinearGradientBrush BirthdayGradient = CreateBirthdayGradient();

    private readonly DispatcherTimer _dayTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private int _lastCheckedDay = -1;

    public BirthdayComponent()
    {
        InitializeComponent();
        _dayTimer.Tick += (_, _) => RefreshIfDayChanged();

        AttachedToVisualTree += (_, _) =>
        {
            Settings.PropertyChanged += OnSettingsPropertyChanged;
            Settings.Entries.CollectionChanged += OnEntriesCollectionChanged;
            if (!Settings.IsLoaded) Settings.LoadFromFile();
            Refresh();
            _dayTimer.Start();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            Settings.PropertyChanged -= OnSettingsPropertyChanged;
            Settings.Entries.CollectionChanged -= OnEntriesCollectionChanged;
            _dayTimer.Stop();
        };
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BirthdaySettings.MaxNamesToShow)
            or nameof(BirthdaySettings.UseGradientColor)
            or nameof(BirthdaySettings.HideWhenNoBirthday))
            Refresh();
    }

    private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void RefreshIfDayChanged()
    {
        int today = DateTime.Today.DayOfYear;
        if (today != _lastCheckedDay)
        {
            _lastCheckedDay = today;
            Refresh();
        }
    }

    private void Refresh()
    {
        _lastCheckedDay = DateTime.Today.DayOfYear;

        var todayBirthdays = Settings.Entries.Where(e => e.IsToday).OrderBy(e => e.Name).ToList();

        // 无人生日且开启隐藏时，直接隐藏整个组件
        if (todayBirthdays.Count == 0 && Settings.HideWhenNoBirthday)
        {
            IsVisible = false;
            return;
        }
        IsVisible = true;

        MainTextBlock.Text = BuildText(todayBirthdays);
        MainTextBlock.Foreground = Settings.UseGradientColor ? BirthdayGradient : DefaultBrush;
    }

    private string BuildText(List<BirthdayEntry> todayBirthdays)
    {
        if (todayBirthdays.Count == 0)
            return "今日生日:无";

        var names = todayBirthdays.Select(e => e.Name).ToList();
        int max = Math.Max(1, Settings.MaxNamesToShow);

        // 全部显示时不需要人数；只有名字被隐藏（超出上限）时才显示「共 N 人」
        if (names.Count <= max)
            return $"今日生日:{string.Join(" ", names)}";

        return $"今日生日:{string.Join(" ", names.Take(max))}（共{names.Count}人）";
    }

    private static LinearGradientBrush CreateBirthdayGradient()
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse("#FF6B9D"), 0.0),
                new GradientStop(Color.Parse("#FF8E5E"), 0.2),
                new GradientStop(Color.Parse("#FFD93D"), 0.4),
                new GradientStop(Color.Parse("#6BCB77"), 0.6),
                new GradientStop(Color.Parse("#4D96FF"), 0.8),
                new GradientStop(Color.Parse("#B06AB3"), 1.0),
            }
        };
    }
}
