using System.Text;
using System.Text.RegularExpressions;

namespace EmailAI.UI.Shared.Helpers;

public static class EmailBodyFormatter
{
    private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);

    public static string EnhanceForMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('|'))
            return text;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var output = new StringBuilder();
        var tableRows = new List<string[]>();

        void FlushTable()
        {
            if (tableRows.Count == 0) return;

            if (tableRows.Count >= 2 && tableRows[0].Length >= 2)
            {
                output.AppendLine("| " + string.Join(" | ", tableRows[0]) + " |");
                output.AppendLine("| " + string.Join(" | ", tableRows[0].Select(_ => "---")) + " |");
                for (var i = 1; i < tableRows.Count; i++)
                    output.AppendLine("| " + string.Join(" | ", PadRow(tableRows[i], tableRows[0].Length)) + " |");
            }
            else
            {
                foreach (var row in tableRows)
                    output.AppendLine(string.Join("  ", row));
            }

            tableRows.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                FlushTable();
                output.AppendLine();
                continue;
            }

            var cols = SplitColumns(line);
            if (cols.Length >= 2 && cols.All(c => c.Length <= 48))
                tableRows.Add(cols);
            else
            {
                FlushTable();
                output.AppendLine(line);
            }
        }

        FlushTable();
        return output.ToString().TrimEnd();
    }

    private static string[] PadRow(string[] row, int width)
    {
        if (row.Length >= width) return row;
        var padded = new string[width];
        Array.Copy(row, padded, row.Length);
        for (var i = row.Length; i < width; i++)
            padded[i] = "";
        return padded;
    }

    private static string[] SplitColumns(string line)
    {
        if (line.Contains('\t'))
            return line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return MultiSpace.Split(line.Trim())
            .Where(c => c.Length > 0)
            .ToArray();
    }
}
