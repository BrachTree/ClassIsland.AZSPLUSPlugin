namespace ClassIsland.AZSMYPlugin.Models;

/// <summary>
/// 一条生日信息。只保存姓名与月/日，不保存年份。
/// </summary>
public class BirthdayEntry
{
    /// <summary>姓名</summary>
    public string Name { get; set; } = "";

    /// <summary>出生月份（1-12）</summary>
    public int Month { get; set; }

    /// <summary>出生日（1-31）</summary>
    public int Day { get; set; }

    /// <summary>今天是否生日</summary>
    public bool IsToday => Month == DateTime.Today.Month && Day == DateTime.Today.Day;

    public override string ToString() => $"{Name}（{Month}月{Day}日）";
}
