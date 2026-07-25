namespace ClassIsland.AZSMYPlugin.Models;

/// <summary>
/// 表示文件中的一行——一个显示帧。可以是单句、一组句子，或单句 + 组合的混合形式。
/// </summary>
public class DisplayFrame
{
    /// <summary>
    /// 前缀单句（可选）。对应 "句子 &lt;颜色&gt;" 部分。
    /// </summary>
    public DisplayItem? Prefix { get; set; }

    /// <summary>
    /// 组内句子列表（可选）。对应 "[句子1 句子2 ...]" 部分。
    /// </summary>
    public List<DisplayItem> GroupItems { get; set; } = new();

    /// <summary>
    /// 组内每个句子的显示时长（秒）。
    /// </summary>
    public double PerItemDuration { get; set; } = 3.0;

    /// <summary>
    /// 组内是否禁用过渡动画。
    /// true = 禁用过渡动画（无动画），false = 跟随组件设置（默认）。
    /// </summary>
    public bool DisableTransition { get; set; } = false;

    /// <summary>
    /// 组内水平滚动速度（px/秒）。0 表示使用默认速度。
    /// </summary>
    public double GroupScrollSpeed { get; set; } = 0;

    /// <summary>
    /// 组内滚动完成后是否暂停。
    /// </summary>
    public bool GroupPauseAfterScroll { get; set; } = true;

    /// <summary>
    /// 是否包含组
    /// </summary>
    public bool HasGroup => GroupItems.Count > 0;

    /// <summary>
    /// 是否包含前缀单句
    /// </summary>
    public bool HasPrefix => Prefix != null;

    /// <summary>
    /// 获取此帧的总显示时长（秒），用于主轮播定时器。
    /// </summary>
    public double TotalDuration
    {
        get
        {
            double total = 0;
            if (HasPrefix)
                total += Prefix!.Duration;
            if (HasGroup)
                total += GroupItems.Count * PerItemDuration;
            if (total <= 0)
                total = 5.0;
            return total;
        }
    }
}
