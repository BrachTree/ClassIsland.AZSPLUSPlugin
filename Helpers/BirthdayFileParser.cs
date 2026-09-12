using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClassIsland.AZSMYPlugin.Models;

namespace ClassIsland.AZSMYPlugin.Helpers;

/// <summary>
/// 解析生日表格文件。支持：
/// - Excel 2007+（.xlsx）：使用内置 Zip/Xml 直接解析，无需第三方库。
/// - 分隔文本（.csv / .txt / .tsv）：自动识别逗号、制表符、分号、竖线分隔符。
///
/// 生日只提取月/日，支持 2005-3-5、2005/3/5、3月5日、3-5、3/5、3.5、0305、Excel 日期序列号等格式。
/// </summary>
public static class BirthdayFileParser
{
    /// <summary>读取文件并自动识别姓名列/生日列后解析出生日条目。</summary>
    public static List<BirthdayEntry> Parse(string filePath)
    {
        var table = ReadTable(filePath);
        return BuildEntries(table, DetectNameColumn(table), DetectBirthdayColumn(table));
    }

    /// <summary>读取文件为二维表格。</summary>
    public static List<List<string>> ReadTable(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext == ".xlsx" ? ParseXlsx(filePath) : ParseDelimited(File.ReadAllText(filePath));
    }

    /// <summary>根据表头（或无表头时按列序号）生成列选项，供设置界面选择姓名列/生日列。</summary>
    public static List<string> GetColumnOptions(List<List<string>> table)
    {
        var options = new List<string>();
        if (table.Count == 0) return options;

        int colCount = table.Max(r => r.Count);
        bool hasHeader = HasHeader(table);
        for (int i = 0; i < colCount; i++)
        {
            string label = hasHeader && i < table[0].Count ? table[0][i].Trim() : "";
            if (string.IsNullOrWhiteSpace(label)) label = $"第{i + 1}列";
            options.Add(label);
        }
        return options;
    }

    /// <summary>自动识别姓名列。</summary>
    public static int DetectNameColumn(List<List<string>> table)
    {
        if (table.Count == 0) return 0;
        for (int i = 0; i < table[0].Count; i++)
            if (IsNameHeader(Normalize(table[0][i]))) return i;
        return 0;
    }

    /// <summary>自动识别生日列。</summary>
    public static int DetectBirthdayColumn(List<List<string>> table)
    {
        if (table.Count == 0) return 1;
        for (int i = 0; i < table[0].Count; i++)
            if (IsBirthdayHeader(Normalize(table[0][i]))) return i;
        int nameCol = DetectNameColumn(table);
        return nameCol == 1 ? 0 : 1;
    }

    /// <summary>按指定的姓名列、生日列解析条目。</summary>
    public static List<BirthdayEntry> BuildEntries(List<List<string>> table, int nameCol, int birthdayCol)
    {
        var result = new List<BirthdayEntry>();
        if (table.Count == 0) return result;

        int colCount = table.Max(r => r.Count);
        if (nameCol < 0 || nameCol >= colCount) nameCol = 0;
        if (birthdayCol < 0 || birthdayCol >= colCount) birthdayCol = 1;

        bool hasHeader = HasHeader(table);
        int startRow = hasHeader ? 1 : 0;
        for (int r = startRow; r < table.Count; r++)
        {
            var row = table[r];
            string name = GetCell(row, nameCol).Trim();
            string birthday = GetCell(row, birthdayCol).Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(birthday)) continue;
            if (!TryParseBirthday(birthday, out int month, out int day)) continue;
            result.Add(new BirthdayEntry { Name = name, Month = month, Day = day });
        }

        return result;
    }

    private static bool HasHeader(List<List<string>> table)
    {
        if (table.Count == 0) return false;
        foreach (var cell in table[0])
        {
            var h = Normalize(cell);
            if (IsNameHeader(h) || IsBirthdayHeader(h)) return true;
        }
        return false;
    }

    private static string GetCell(List<string> row, int col)
        => col >= 0 && col < row.Count ? row[col] : "";

    private static string Normalize(string s)
    {
        s = s.Trim();
        if (s.Length > 0 && s[0] == '\uFEFF') s = s[1..];
        return s.ToLowerInvariant();
    }

    private static bool IsNameHeader(string h) =>
        h is "姓名" or "名字" or "名称" or "学生姓名" or "人名" or "name";

    private static bool IsBirthdayHeader(string h) =>
        h.Contains("生日") || h.Contains("出生") || h.Contains("诞辰") ||
        h is "birthday" or "birth" or "date" or "birthdate";

    // === 生日日期解析 ===

    internal static bool TryParseBirthday(string raw, out int month, out int day)
    {
        month = day = 0;
        var s = raw.Trim();
        if (string.IsNullOrWhiteSpace(s)) return false;

        // 去掉时间部分：2005-03-05 00:00:00 / 2020-03-05T00:00:00
        s = Regex.Replace(s, @"\s+T?\d{1,2}:\d{2}(:\d{2})?\s*$", "").Trim();

        // 纯数字
        if (Regex.IsMatch(s, @"^\d+$"))
        {
            // 3-4 位：305 / 0305 → 月日（305 视为 3 月 5 日）
            if (s.Length is 3 or 4)
            {
                string digits = s.Length == 4 ? s : "0" + s;
                month = int.Parse(digits.Substring(0, 2));
                day = int.Parse(digits.Substring(2));
                return IsValidDate(month, day);
            }

            // 5 位以上：Excel 日期序列号
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial) &&
                serial >= 10000 && serial <= 80000)
            {
                var dt = new DateTime(1899, 12, 30).AddDays(serial);
                month = dt.Month;
                day = dt.Day;
                return true;
            }

            return false;
        }

        // 去掉可能的前导年份：2005-3-5 / 2005/3/5 / 2005.3.5 / 2005年3月5日
        var noYear = Regex.Replace(s, @"^(19|20)\d{2}\s*[年/\-.]?", "").Trim();

        // 中文「3月5日 / 3月5号 / 3月5」
        var m = Regex.Match(noYear, @"(\d{1,2})\s*月\s*(\d{1,2})\s*[日号]?");
        if (m.Success)
        {
            month = int.Parse(m.Groups[1].Value);
            day = int.Parse(m.Groups[2].Value);
            return IsValidDate(month, day);
        }

        // 分隔符「3-5 / 3/5 / 3.5」
        m = Regex.Match(noYear, @"^(\d{1,2})\s*[-/.]\s*(\d{1,2})\s*$");
        if (m.Success)
        {
            month = int.Parse(m.Groups[1].Value);
            day = int.Parse(m.Groups[2].Value);
            return IsValidDate(month, day);
        }

        // DateTime 兜底
        if (DateTime.TryParse(noYear, out var dt2))
        {
            month = dt2.Month;
            day = dt2.Day;
            return true;
        }

        return false;
    }

    private static bool IsValidDate(int month, int day)
        => month is >= 1 and <= 12 && day is >= 1 and <= 31;

    // === 分隔文本（CSV/TSV/TXT）解析 ===

    private static List<List<string>> ParseDelimited(string content)
    {
        var result = new List<List<string>>();
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var nonEmpty = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (nonEmpty.Count == 0) return result;

        char delimiter = DetectDelimiter(nonEmpty[0]);
        foreach (var line in nonEmpty)
            result.Add(SplitDelimitedLine(line, delimiter));
        return result;
    }

    private static char DetectDelimiter(string line)
    {
        int best = -1;
        char bestChar = ',';
        foreach (var c in new[] { ',', '\t', ';', '|' })
        {
            int count = line.Count(ch => ch == c);
            if (count > best)
            {
                best = count;
                bestChar = c;
            }
        }
        return best > 0 ? bestChar : ',';
    }

    private static List<string> SplitDelimitedLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == delimiter && !inQuotes)
            {
                fields.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(ch);
            }
        }
        fields.Add(sb.ToString().Trim());
        return fields;
    }

    // === Excel .xlsx 解析（仅依赖 System.IO.Compression + System.Xml.Linq） ===

    private static readonly XNamespace MainNs =
        XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
    private static readonly XNamespace RelNs =
        XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
    private static readonly XNamespace PkgRelNs =
        XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");

    private static List<List<string>> ParseXlsx(string filePath)
    {
        var table = new List<List<string>>();
        using var zip = ZipFile.OpenRead(filePath);

        var sharedStrings = ReadSharedStrings(zip);
        var sheetEntry = FindFirstSheetEntry(zip);
        if (sheetEntry == null) return table;

        using var sheetStream = sheetEntry.Open();
        var sheetDoc = XDocument.Load(sheetStream);

        foreach (var rowEl in sheetDoc.Descendants(MainNs + "row"))
        {
            var row = new List<string>();
            foreach (var cellEl in rowEl.Elements(MainNs + "c"))
            {
                string? cellRef = (string?)cellEl.Attribute("r");
                int colIndex = ParseColumnIndex(cellRef);
                if (colIndex < 0) colIndex = row.Count;

                string? type = (string?)cellEl.Attribute("t");
                string value = type switch
                {
                    "s" => SharedString(cellEl, sharedStrings),
                    "inlineStr" => string.Concat(cellEl.Descendants(MainNs + "t").Select(x => x.Value)),
                    _ => cellEl.Element(MainNs + "v")?.Value ?? ""
                };

                while (row.Count <= colIndex) row.Add("");
                row[colIndex] = value;
            }

            while (row.Count > 0 && string.IsNullOrWhiteSpace(row[^1])) row.RemoveAt(row.Count - 1);
            if (row.Count > 0) table.Add(row);
        }

        return table;
    }

    private static string SharedString(XElement cellEl, List<string> sharedStrings)
    {
        var v = cellEl.Element(MainNs + "v")?.Value;
        if (v != null && int.TryParse(v, out int idx) && idx >= 0 && idx < sharedStrings.Count)
            return sharedStrings[idx];
        return "";
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return list;

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        foreach (var si in doc.Descendants(MainNs + "si"))
            list.Add(string.Concat(si.Descendants(MainNs + "t").Select(t => t.Value)));
        return list;
    }

    private static ZipArchiveEntry? FindFirstSheetEntry(ZipArchive zip)
    {
        var direct = zip.GetEntry("xl/worksheets/sheet1.xml");
        if (direct != null) return direct;

        var wbEntry = zip.GetEntry("xl/workbook.xml");
        if (wbEntry == null) return null;

        using var wbStream = wbEntry.Open();
        var wbDoc = XDocument.Load(wbStream);
        var firstSheet = wbDoc.Descendants(MainNs + "sheet").FirstOrDefault();
        var rid = (string?)firstSheet?.Attribute(RelNs + "id");
        if (rid == null) return null;

        var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry == null) return null;

        using var relsStream = relsEntry.Open();
        var relsDoc = XDocument.Load(relsStream);
        var rel = relsDoc.Descendants(PkgRelNs + "Relationship")
            .FirstOrDefault(e => (string?)e.Attribute("Id") == rid);
        var target = (string?)rel?.Attribute("Target");
        if (target == null) return null;

        target = target.Replace('\\', '/');
        var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        return zip.GetEntry(path);
    }

    private static int ParseColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return -1;
        int col = 0;
        foreach (char ch in cellRef)
        {
            if (char.IsLetter(ch))
                col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            else
                break;
        }
        return col - 1;
    }
}
