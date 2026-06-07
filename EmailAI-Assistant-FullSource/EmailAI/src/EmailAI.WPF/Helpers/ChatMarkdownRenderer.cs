using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EmailAI.WPF.Helpers;

public static class ChatMarkdownRenderer
{
    private static readonly Regex TableSeparator = new(@"^\|[\s\-:|]+\|$", RegexOptions.Compiled);
    private static readonly Regex InlineFormat = new(@"\*\*(.+?)\*\*|\*(.+?)\*|\[(.+?)\]\((.+?)\)", RegexOptions.Compiled);

    public static void Render(StackPanel panel, string markdown)
    {
        panel.Children.Clear();
        if (string.IsNullOrWhiteSpace(markdown)) return;

        var theme = ChatTheme.Current;
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                panel.Children.Add(Spacer(8));
                i++;
                continue;
            }

            if (IsTableStart(lines, i))
            {
                var tableLines = new List<string>();
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    tableLines.Add(lines[i].Trim());
                    i++;
                }
                panel.Children.Add(BuildTable(tableLines, theme));
                continue;
            }

            if (line.StartsWith("## "))
            {
                panel.Children.Add(BuildSectionHeader(line[3..], theme, 16));
                i++;
                continue;
            }

            if (line.StartsWith("### "))
            {
                panel.Children.Add(BuildSectionHeader(line[4..], theme, 14));
                i++;
                continue;
            }

            if (line.StartsWith("#### "))
            {
                panel.Children.Add(BuildSubHeader(line[5..], theme));
                i++;
                continue;
            }

            if (line.StartsWith("---") || line.StartsWith("***"))
            {
                panel.Children.Add(BuildDivider(theme));
                i++;
                continue;
            }

            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                panel.Children.Add(BuildBullet(line[2..], theme));
                i++;
                continue;
            }

            if (Regex.IsMatch(line, @"^\d+\.\s"))
            {
                var m = Regex.Match(line, @"^(\d+)\.\s(.*)$");
                panel.Children.Add(BuildNumbered(m.Groups[1].Value, m.Groups[2].Value, theme));
                i++;
                continue;
            }

            if (IsInsightLine(line, out var insightLabel, out var insightBody))
            {
                panel.Children.Add(BuildInsightCard(insightLabel, insightBody, theme));
                i++;
                continue;
            }

            panel.Children.Add(BuildParagraph(line, theme));
            i++;
        }
    }

    private static bool IsTableStart(string[] lines, int index)
    {
        if (index >= lines.Length) return false;
        var line = lines[index].Trim();
        if (!line.StartsWith('|')) return false;
        if (index + 1 >= lines.Length) return false;
        return TableSeparator.IsMatch(lines[index + 1].Trim());
    }

    private static UIElement BuildTable(List<string> tableLines, ChatTheme theme)
    {
        var rows = new List<string[]>();
        foreach (var line in tableLines)
        {
            if (TableSeparator.IsMatch(line)) continue;
            var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length > 0) rows.Add(cells);
        }

        if (rows.Count == 0) return Spacer(4);

        var colCount = rows.Max(r => r.Length);
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 12) };

        var border = new Border
        {
            Background = theme.TableBg,
            BorderBrush = theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(2),
            Child = grid
        };

        for (int c = 0; c < colCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var numericCols = DetectNumericColumns(rows.Skip(1).ToList(), colCount);
        var colMax = ComputeColumnMaxima(rows.Skip(1).ToList(), colCount, numericCols);

        for (int r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int c = 0; c < colCount; c++)
            {
                var cellText = c < rows[r].Length ? rows[r][c] : "";
                var isHeader = r == 0;

                var cellBorder = new Border
                {
                    Background = isHeader ? theme.TableHeaderBg : (r % 2 == 0 ? theme.TableRowBg : theme.TableRowAltBg),
                    BorderBrush = theme.Border,
                    BorderThickness = new Thickness(c == 0 ? 0 : 1, r == 0 ? 0 : 1, 0, 0),
                    Padding = new Thickness(12, 10, 12, 10)
                };

                if (isHeader)
                {
                    cellBorder.Child = MakeText(cellText, 12, FontWeights.SemiBold, theme.TableHeaderText);
                }
                else if (numericCols[c] && TryParseNumber(cellText, out var num))
                {
                    cellBorder.Child = BuildTrendCell(cellText, num, colMax[c], theme);
                }
                else
                {
                    cellBorder.Child = MakeRichText(StripCellMarkdown(cellText), 12, theme.BodyText);
                }

                Grid.SetRow(cellBorder, r);
                Grid.SetColumn(cellBorder, c);
                grid.Children.Add(cellBorder);
            }
        }

        return border;
    }

    private static UIElement BuildTrendCell(string text, double value, double max, ChatTheme theme)
    {
        var stack = new StackPanel();
        stack.Children.Add(MakeText(text, 13, FontWeights.SemiBold, GetTrendColor(text, value, theme)));

        if (max > 0)
        {
            var ratio = Math.Min(1.0, value / max);
            var track = new Grid { Height = 4, Margin = new Thickness(0, 6, 0, 0) };
            track.Children.Add(new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = theme.Border
            });
            track.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(4, ratio * 120),
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = GetTrendBrush(value, max, theme)
            });
            stack.Children.Add(track);
        }

        return stack;
    }

    private static Brush GetTrendBrush(double value, double max, ChatTheme theme)
    {
        if (max <= 0) return theme.Accent;
        var ratio = value / max;
        return ratio >= 0.7 ? theme.Danger : ratio >= 0.4 ? theme.Warning : theme.Success;
    }

    private static Brush GetTrendColor(string text, double value, ChatTheme theme)
    {
        if (text.Contains('↑') || text.Contains('+')) return theme.Success;
        if (text.Contains('↓') || text.Contains('-')) return theme.Danger;
        return theme.PrimaryText;
    }

    private static bool TryParseNumber(string text, out double num)
    {
        var cleaned = Regex.Replace(text, @"[^\d.\-]", "");
        return double.TryParse(cleaned, out num);
    }

    private static bool[] DetectNumericColumns(List<string[]> dataRows, int colCount)
    {
        var result = new bool[colCount];
        for (int c = 0; c < colCount; c++)
        {
            var numeric = 0;
            var total = 0;
            foreach (var row in dataRows)
            {
                if (c >= row.Length || string.IsNullOrWhiteSpace(row[c])) continue;
                total++;
                if (TryParseNumber(row[c], out _)) numeric++;
            }
            result[c] = total > 0 && numeric >= Math.Max(1, total * 0.6);
        }
        return result;
    }

    private static double[] ComputeColumnMaxima(List<string[]> dataRows, int colCount, bool[] numericCols)
    {
        var max = new double[colCount];
        foreach (var row in dataRows)
        {
            for (int c = 0; c < colCount && c < row.Length; c++)
            {
                if (!numericCols[c]) continue;
                if (TryParseNumber(row[c], out var n))
                    max[c] = Math.Max(max[c], n);
            }
        }
        return max;
    }

    private static UIElement BuildSectionHeader(string text, ChatTheme theme, double size)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 14, 0, 8) };
        stack.Children.Add(MakeText(text.Trim(), size, FontWeights.Bold, theme.PrimaryText));
        stack.Children.Add(new Border
        {
            Height = 3,
            Width = 48,
            CornerRadius = new CornerRadius(2),
            Background = theme.Accent,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0)
        });
        return stack;
    }

    private static UIElement BuildSubHeader(string text, ChatTheme theme)
    {
        return MakeText(text.Trim(), 13, FontWeights.SemiBold, theme.Accent);
    }

    private static UIElement BuildDivider(ChatTheme theme)
    {
        return new Border
        {
            Height = 1,
            Background = theme.Border,
            Margin = new Thickness(0, 10, 0, 10)
        };
    }

    private static UIElement BuildBullet(string text, ChatTheme theme)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new Ellipse
        {
            Width = 6, Height = 6,
            Fill = theme.Accent,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 7, 10, 0)
        };
        Grid.SetColumn(dot, 0);
        grid.Children.Add(dot);

        var content = MakeRichText(text, 13, theme.BodyText);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    private static UIElement BuildNumbered(string num, string text, ChatTheme theme)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = new Border
        {
            Background = theme.AccentSoft,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 10, 0),
            Child = MakeText(num, 11, FontWeights.Bold, theme.Accent)
        };
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        var content = MakeRichText(text, 13, theme.BodyText);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    private static UIElement BuildInsightCard(string label, string body, ChatTheme theme)
    {
        var accentColor = label.Contains("urgent", StringComparison.OrdinalIgnoreCase)
                       || label.Contains("action", StringComparison.OrdinalIgnoreCase)
            ? theme.Warning
            : label.Contains("risk", StringComparison.OrdinalIgnoreCase)
                ? theme.Danger
                : theme.Success;

        var stack = new StackPanel();
        var border = new Border
        {
            Background = theme.InsightBg,
            BorderBrush = accentColor,
            BorderThickness = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 6, 0, 6),
            Child = stack
        };

        stack.Children.Add(MakeText(label, 11, FontWeights.Bold, accentColor));
        stack.Children.Add(MakeRichText(body, 13, theme.BodyText));
        return border;
    }

    private static UIElement BuildParagraph(string text, ChatTheme theme)
    {
        return MakeRichText(text, 13, theme.BodyText);
    }

    private static bool IsInsightLine(string line, out string label, out string body)
    {
        label = body = "";
        var m = Regex.Match(line, @"^\*\*(.+?):\*\*\s*(.*)$");
        if (!m.Success) return false;
        label = m.Groups[1].Value;
        body = m.Groups[2].Value;
        return true;
    }

    private static TextBlock MakeText(string text, double size, FontWeight weight, Brush brush)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = size * 1.5
        };
    }

    private static TextBlock MakeRichText(string text, double size, Brush defaultBrush)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = size,
            LineHeight = size * 1.55,
            Margin = new Thickness(0, 1, 0, 1)
        };
        AppendInlines(tb.Inlines, text, defaultBrush);
        return tb;
    }

    private static void AppendInlines(InlineCollection inlines, string text, Brush defaultBrush)
    {
        var accent = ChatTheme.Current.Accent;
        int pos = 0;
        foreach (Match match in InlineFormat.Matches(text))
        {
            if (match.Index > pos)
                inlines.Add(new Run(text[pos..match.Index]) { Foreground = defaultBrush });

            if (match.Groups[1].Success)
                inlines.Add(new Run(match.Groups[1].Value) { FontWeight = FontWeights.SemiBold, Foreground = defaultBrush });
            else if (match.Groups[2].Success)
                inlines.Add(new Run(match.Groups[2].Value) { FontStyle = FontStyles.Italic, Foreground = defaultBrush });
            else if (match.Groups[3].Success && match.Groups[4].Success)
            {
                var link = new Hyperlink(new Run(match.Groups[3].Value))
                {
                    NavigateUri = new Uri(match.Groups[4].Value, UriKind.RelativeOrAbsolute),
                    Foreground = accent,
                    TextDecorations = null
                };
                link.RequestNavigate += (_, e) =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                    }
                    catch { /* ignore */ }
                };
                inlines.Add(link);
            }

            pos = match.Index + match.Length;
        }

        if (pos < text.Length)
            inlines.Add(new Run(text[pos..]) { Foreground = defaultBrush });
        if (inlines.Count == 0)
            inlines.Add(new Run(text) { Foreground = defaultBrush });
    }

    private static Border Spacer(double h) => new() { Height = h };

    private static string StripCellMarkdown(string text) =>
        text.Replace("**", "").Trim();
}

internal sealed class ChatTheme
{
    public Brush PrimaryText { get; init; } = Brushes.White;
    public Brush BodyText { get; init; } = Brushes.LightGray;
    public Brush Accent { get; init; } = Brushes.DodgerBlue;
    public Brush AccentSoft { get; init; } = Brushes.Transparent;
    public Brush Success { get; init; } = Brushes.LimeGreen;
    public Brush Warning { get; init; } = Brushes.Orange;
    public Brush Danger { get; init; } = Brushes.Red;
    public Brush Border { get; init; } = Brushes.Gray;
    public Brush TableBg { get; init; } = Brushes.Transparent;
    public Brush TableHeaderBg { get; init; } = Brushes.Transparent;
    public Brush TableHeaderText { get; init; } = Brushes.White;
    public Brush TableRowBg { get; init; } = Brushes.Transparent;
    public Brush TableRowAltBg { get; init; } = Brushes.Transparent;
    public Brush InsightBg { get; init; } = Brushes.Transparent;

    public static ChatTheme Current
    {
        get
        {
            var app = System.Windows.Application.Current;
            if (app is null) return new ChatTheme();

            Brush B(string key) => app.FindResource(key) as Brush ?? Brushes.Gray;
            return new ChatTheme
            {
                PrimaryText = B("TextPrimaryBrush"),
                BodyText = B("TextSecondaryBrush"),
                Accent = B("AccentBrush"),
                AccentSoft = B("ChatAccentSoftBrush"),
                Success = B("SuccessBrush"),
                Warning = B("WarningBrush"),
                Danger = B("DangerBrush"),
                Border = B("BorderBrush"),
                TableBg = B("ChatTableBgBrush"),
                TableHeaderBg = B("ChatTableHeaderBrush"),
                TableHeaderText = B("TextPrimaryBrush"),
                TableRowBg = B("ChatTableRowBrush"),
                TableRowAltBg = B("ChatTableRowAltBrush"),
                InsightBg = B("ChatInsightBgBrush")
            };
        }
    }
}
