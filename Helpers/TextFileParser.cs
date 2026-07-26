using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Media;
using ClassIsland.AZSMYPlugin.Models;

namespace ClassIsland.AZSMYPlugin.Helpers;

/// <summary>
/// 解析文本文件，将每一行转换为 <see cref="DisplayFrame"/>。
///
/// 参数使用 &lt;&gt; 包裹，例如 &lt;5&gt; 表示显示时长5秒，&lt;#FF0000&gt; 表示红色。
/// 文本中的下划线 _ 会被替换为空格，用于在组内创建含空格的句子。
///
/// 支持的格式（颜色均可有可无，默认白色）：
/// 1. 单句：  句子 &lt;显示时长&gt; &lt;颜色&gt; &lt;滚动速度&gt; &lt;是否滚动完暂停&gt;
/// 2. 组：    [句子1&lt;颜色&gt; 句子2 句子3 &lt;每句显示时长&gt; &lt;是否禁用过渡动画&gt; &lt;滚动速度&gt; &lt;是否滚动完暂停&gt;]
/// 3. 混合：  句子&lt;颜色&gt; [句子1 句子2 句子3 &lt;每句时长&gt; &lt;是否禁用过渡动画&gt; &lt;滚动速度&gt; &lt;是否滚动完暂停&gt;]
///
/// 参数说明：
/// - &lt;滚动速度&gt;：水平滚动速度（px/秒），仅当文本超出容器宽度（350px）时生效。
/// - &lt;是否滚动完暂停&gt;：true=滚动完成后暂停在末尾，false=循环滚动。默认 false。
/// - &lt;是否禁用过渡动画&gt;：true=禁用过渡动画，false=跟随组件设置。默认 false。
/// </summary>
public static class TextFileParser
{
    /// <summary>
    /// 匹配独立的 &lt;参数&gt; token（整个 token 就是 &lt;...&gt;）
    /// </summary>
    private static readonly Regex StandaloneParamRegex =
        new(@"^<([^>]*)>$", RegexOptions.Compiled);

    /// <summary>
    /// 匹配附着在文本末尾的 &lt;颜色&gt;，如 Hello&lt;#FF0000&gt;
    /// </summary>
    private static readonly Regex AttachedColorRegex =
        new(@"^(.+?)<([^>]+)>$", RegexOptions.Compiled);

    /// <summary>
    /// 从字符串末尾提取独立的 &lt;参数&gt; token。
    /// </summary>
    private static readonly Regex TrailingParamRegex =
        new(@"<([^>]*)>\s*$", RegexOptions.Compiled);

    /// <summary>
    /// 解析整个文本文件内容。
    /// </summary>
    public static List<DisplayFrame> Parse(string content)
    {
        var frames = new List<DisplayFrame>();
        if (string.IsNullOrWhiteSpace(content))
            return frames;

        var lines = content.Replace("\r\n", "\n").Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.StartsWith("//"))
                continue;

            var frame = ParseLine(line);
            if (frame != null)
                frames.Add(frame);
        }
        return frames;
    }

    private static DisplayFrame? ParseLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line))
            return null;

        int bracketIndex = line.IndexOf('[');
        if (bracketIndex >= 0)
            return ParseCombinedLine(line, bracketIndex);

        return ParseSingleSentence(line);
    }

    /// <summary>
    /// 解析纯单句行：句子 &lt;显示时长&gt; &lt;颜色&gt; &lt;滚动速度&gt; &lt;是否滚动完暂停&gt;
    /// </summary>
    private static DisplayFrame ParseSingleSentence(string line)
    {
        var (text, duration, color, scrollSpeed, pauseAfterScroll) = ExtractTextAndParams(line, 0);
        return new DisplayFrame
        {
            Prefix = new DisplayItem
            {
                Text = ReplaceUnderscores(text),
                Color = color,
                Duration = duration,
                ScrollSpeed = scrollSpeed,
                PauseAfterScroll = pauseAfterScroll
            }
        };
    }

    /// <summary>
    /// 解析包含组的行（可能同时包含前缀单句）。
    /// 紧贴模式（[ 前无空格）：前缀单独存储到 AttachedPrefix，组内句子轮播时前缀固定不变。
    /// 正常模式（[ 前有空格）：前缀作为独立单句先显示，再轮播组内句子。
    /// </summary>
    private static DisplayFrame? ParseCombinedLine(string line, int bracketIndex)
    {
        var frame = new DisplayFrame();

        int closeBracket = line.IndexOf(']', bracketIndex);
        if (closeBracket < 0)
            closeBracket = line.Length;

        string prefixPart = line.Substring(0, bracketIndex).Trim();
        string groupContent = line.Substring(bracketIndex + 1, closeBracket - bracketIndex - 1).Trim();

        // 先解析组内容
        ParseGroup(groupContent, frame);

        if (!string.IsNullOrWhiteSpace(prefixPart))
        {
            var (text, duration, color, scrollSpeed, pauseAfterScroll) = ExtractTextAndParams(prefixPart, 0);
            var prefixText = ReplaceUnderscores(text);

            // 判断 [ 前面是否有空格分隔
            bool attached = bracketIndex > 0 && !char.IsWhiteSpace(line[bracketIndex - 1]);

            if (attached && frame.HasGroup && !string.IsNullOrWhiteSpace(prefixText))
            {
                // 紧贴模式：前缀单独存储，组内轮播时前缀固定显示不变
                frame.AttachedPrefix = new DisplayItem
                {
                    Text = prefixText,
                    Color = color != Colors.White ? color : Colors.White
                };
            }
            else
            {
                // 正常模式：前缀作为独立单句
                frame.Prefix = new DisplayItem
                {
                    Text = prefixText,
                    Color = color,
                    Duration = duration,
                    ScrollSpeed = scrollSpeed,
                    PauseAfterScroll = pauseAfterScroll
                };
            }
        }

        if (!frame.HasPrefix && !frame.HasGroup && !frame.HasAttachedPrefix)
            return null;

        return frame;
    }

    /// <summary>
    /// 解析组内容。
    /// 组尾部参数顺序：&lt;每句时长&gt; &lt;是否禁用过渡动画&gt; &lt;滚动速度&gt; &lt;是否滚动完暂停&gt;
    /// 类型模式：num, bool, num, bool
    /// </summary>
    private static void ParseGroup(string content, DisplayFrame frame)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        var tokens = TokenizeRespectingBrackets(content);
        if (tokens.Count == 0)
            return;

        // 从末尾收集独立的 <参数> token
        var trailingParams = new List<string>();
        int paramStart = tokens.Count;
        for (int i = tokens.Count - 1; i >= 0; i--)
        {
            if (IsStandaloneParam(tokens[i]))
            {
                trailingParams.Insert(0, tokens[i]);
                paramStart = i;
            }
            else
                break;
        }

        // 分类参数：num, bool, num, bool
        var numbers = new List<double>();
        var bools = new List<bool>();

        foreach (var param in trailingParams)
        {
            var inner = GetParamInner(param);
            if (TryParseBool(inner, out bool b))
                bools.Add(b);
            else if (double.TryParse(inner, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                numbers.Add(n);
            // 颜色在组级别不处理（颜色附着在句子上）
        }

        // 按位置赋值
        // numbers: [perItemDuration, scrollSpeed]
        // bools: [disableTransition, pauseAfterScroll]
        if (numbers.Count >= 1)
            frame.PerItemDuration = numbers[0];
        if (numbers.Count >= 2)
            frame.GroupScrollSpeed = numbers[1];

        if (bools.Count >= 1)
            frame.DisableTransition = bools[0];
        if (bools.Count >= 2)
            frame.GroupPauseAfterScroll = bools[1];

        // 剩余的 token 是句子
        for (int i = 0; i < paramStart; i++)
        {
            var (text, color) = ExtractSentenceAndColor(tokens[i]);
            var finalText = ReplaceUnderscores(text);
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                frame.GroupItems.Add(new DisplayItem
                {
                    Text = finalText,
                    Color = color
                });
            }
        }
    }

    /// <summary>
    /// 从一段文本中提取尾部 &lt;参数&gt; 和剩余文本。
    /// 参数顺序：&lt;显示时长&gt; &lt;颜色&gt; &lt;滚动速度&gt; &lt;是否滚动完暂停&gt;
    /// 类型模式：num, color, num, bool
    /// </summary>
    private static (string text, double duration, Color color, double scrollSpeed, bool pauseAfterScroll)
        ExtractTextAndParams(string line, double defaultDuration)
    {
        line = line.Trim();

        // 从末尾收集所有 <参数>
        var paramList = new List<string>();
        while (true)
        {
            var match = TrailingParamRegex.Match(line);
            if (!match.Success)
                break;

            string param = match.Value.Trim();
            paramList.Insert(0, param);
            line = line.Substring(0, match.Index).Trim();
        }

        // 分类参数
        var numbers = new List<double>();
        var colors = new List<Color>();
        var bools = new List<bool>();

        foreach (var param in paramList)
        {
            var inner = GetParamInner(param);
            if (TryParseBool(inner, out bool b))
                bools.Add(b);
            else if (TryParseColor(inner, out Color c))
                colors.Add(c);
            else if (double.TryParse(inner, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                numbers.Add(n);
        }

        // 按位置赋值
        // numbers: [duration, scrollSpeed]
        // colors: [color]
        // bools: [pauseAfterScroll]
        double duration = numbers.Count >= 1 ? numbers[0] : defaultDuration;
        double scrollSpeed = numbers.Count >= 2 ? numbers[1] : 0;
        Color color = colors.Count >= 1 ? colors[0] : Colors.White;
        bool pauseAfterScroll = bools.Count >= 1 ? bools[0] : true;

        return (line, duration, color, scrollSpeed, pauseAfterScroll);
    }

    /// <summary>
    /// 从一个 token 中提取文本和颜色。
    /// </summary>
    private static (string text, Color color) ExtractSentenceAndColor(string token)
    {
        token = token.Trim();

        if (IsStandaloneParam(token))
        {
            var inner = GetParamInner(token);
            if (TryParseColor(inner, out Color paramColor))
                return ("", paramColor);
            return (token, Colors.White);
        }

        var match = AttachedColorRegex.Match(token);
        if (match.Success)
        {
            string text = match.Groups[1].Value.Trim();
            string colorStr = match.Groups[2].Value;
            if (TryParseColor(colorStr, out Color color))
                return (text, color);
        }

        return (token, Colors.White);
    }

    /// <summary>
    /// 按空格分词，但 &lt;...&gt; 内的内容不被拆分。
    /// </summary>
    private static List<string> TokenizeRespectingBrackets(string content)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inBracket = false;

        foreach (char c in content)
        {
            if (c == '<')
            {
                inBracket = true;
                current.Append(c);
            }
            else if (c == '>')
            {
                inBracket = false;
                current.Append(c);
            }
            else if (char.IsWhiteSpace(c) && !inBracket)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private static bool IsStandaloneParam(string token)
    {
        return StandaloneParamRegex.IsMatch(token);
    }

    private static string GetParamInner(string paramToken)
    {
        var match = StandaloneParamRegex.Match(paramToken);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static string ReplaceUnderscores(string text)
    {
        return text.Replace('_', ' ');
    }

    private static bool TryParseColor(string str, out Color color)
    {
        if (string.IsNullOrWhiteSpace(str))
        {
            color = Colors.White;
            return false;
        }

        str = str.Trim();

        if (Color.TryParse(str, out color))
            return true;

        color = Colors.White;
        return false;
    }

    private static bool TryParseBool(string str, out bool result)
    {
        if (string.IsNullOrWhiteSpace(str))
        {
            result = false;
            return false;
        }

        str = str.Trim().ToLowerInvariant();
        switch (str)
        {
            case "true":
            case "1":
            case "yes":
            case "是":
            case "禁用":
            case "暂停":
                result = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "否":
            case "跟随":
            case "循环":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }
}
