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
    /// 紧贴前缀文本（可选）。紧贴模式（[ 前无空格）时，前缀文本单独存储在此，
    /// 组内句子轮播时前缀始终固定显示不变。
    /// </summary>
    public DisplayItem? AttachedPrefix { get; set; }

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
    /// 组内过渡动画类型。
    /// A=淡入淡出，B=向上滚动，C=向下滚动，D=向左滚动，E=向右滚动。
    /// 默认 B（向上滚动）。仅当过渡动画启用时生效。
    /// </summary>
    public string GroupAnimationType { get; set; } = "B";

    /// <summary>
    /// 是否包含组
    /// </summary>
    public bool HasGroup => GroupItems.Count > 0;

    /// <summary>
    /// 是否包含前缀单句（Prefix 不为 null 且文本非空）
    /// </summary>
    public bool HasPrefix => Prefix != null && !string.IsNullOrWhiteSpace(Prefix.Text);

    /// <summary>
    /// 是否包含紧贴前缀（AttachedPrefix 不为 null 且文本非空）
    /// </summary>
    public bool HasAttachedPrefix => AttachedPrefix != null && !string.IsNullOrWhiteSpace(AttachedPrefix.Text);

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