using Avalonia.Media;

namespace ClassIsland.AZSMYPlugin.Models;

/// <summary>
/// 表示一个要显示的文本项（单句）。
/// </summary>
public class DisplayItem
{
    /// <summary>
    /// 文本内容
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// 文本颜色，默认白色。当 <see cref="GradientColors"/> 指定了至少两个颜色时，此属性不再生效。
    /// </summary>
    public Color Color { get; set; } = Colors.White;

    /// <summary>
    /// 文本渐变色列表（至少 2 个颜色时按水平方向从左到右渐变）。
    /// 通过 &lt;#FF0000#00FF00#0000FF&gt; 参数指定，null 时使用 <see cref="Color"/> 单色。
    /// </summary>
    public List<Color>? GradientColors { get; set; }

    /// <summary>
    /// 显示时长（秒）。仅对独立单句有效；组内句子的时长由组的 PerItemDuration 统一控制。
    /// </summary>
    public double Duration { get; set; } = 5.0;

    /// <summary>
    /// 水平滚动速度（px/秒）。0 表示使用组件设置的默认速度。
    /// 仅当标记 IsLongText 时生效。
    /// </summary>
    public double ScrollSpeed { get; set; } = 0;

    /// <summary>
    /// 滚动完成后是否暂停（停留在末尾）。
    /// true = 滚动完成后暂停，false = 滚动完成后循环重新开始。
    /// </summary>
    public bool PauseAfterScroll { get; set; } = true;

    /// <summary>
    /// 是否为长文本（需水平滚动）。通过 &lt;long&gt; 参数显式指定。
    /// 仅对独立单句有效。
    /// </summary>
    public bool IsLongText { get; set; } = false;

    public override string ToString() => Text;
}
