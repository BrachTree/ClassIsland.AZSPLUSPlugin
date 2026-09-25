using Avalonia;
using Avalonia.Media;

namespace ClassIsland.AZSMYPlugin.Helpers;

/// <summary>
/// 根据单色或渐变色创建文本画刷。
/// </summary>
public static class TextBrushFactory
{
    /// <summary>
    /// 创建文本画刷：渐变色数量 ≥ 2 时创建水平方向（从左到右）的线性渐变画刷，否则创建单色画刷。
    /// </summary>
    /// <param name="color">单色颜色，渐变色不足 2 个时使用。</param>
    /// <param name="gradientColors">渐变色列表，按从左到右的顺序排列；为 null 或少于 2 个颜色时使用 <paramref name="color"/>。</param>
    public static IBrush Create(Color color, IReadOnlyList<Color>? gradientColors)
    {
        if (gradientColors == null || gradientColors.Count < 2)
            return new SolidColorBrush(color);

        var stops = new GradientStops();
        int count = gradientColors.Count;
        for (int i = 0; i < count; i++)
        {
            stops.Add(new GradientStop(gradientColors[i], (double)i / (count - 1)));
        }

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = stops
        };
    }
}
