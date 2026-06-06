using DocumentFormat.OpenXml.Packaging;
using EmailAI.Core.Interfaces;
using ExcelDataReader;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Data;

namespace EmailAI.Infrastructure.Services;

/// <summary>
/// Extracts plain text from common attachment types:
/// PDF, DOCX, XLSX, TXT (and CSV, HTML, XML as text variants).
/// </summary>
public sealed class AttachmentExtractor : IAttachmentExtractor
{
    private readonly ILogger<AttachmentExtractor> _logger;

    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
        "text/plain",
        "text/csv",
        "text/html",
        "application/msword"
    };

    public AttachmentExtractor(ILogger<AttachmentExtractor> logger) => _logger = logger;

    public bool CanExtract(string contentType, string fileName)
    {
        if (SupportedTypes.Contains(contentType)) return true;
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext is "pdf" or "docx" or "xlsx" or "xls" or "txt" or "csv" or "html" or "htm";
    }

    public async Task<string> ExtractTextAsync(byte[] content, string contentType, string fileName, CancellationToken ct = default)
    {
        try
        {
            var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();

            if (contentType.Contains("pdf") || ext == "pdf")
                return await ExtractPdfAsync(content, ct);

            if (contentType.Contains("wordprocessingml") || ext == "docx")
                return ExtractDocx(content);

            if (contentType.Contains("spreadsheetml") || ext is "xlsx" or "xls")
                return ExtractXlsx(content, ext);

            if (contentType.StartsWith("text/") || ext is "txt" or "csv")
                return Encoding.UTF8.GetString(content);

            if (contentType.Contains("html") || ext is "html" or "htm")
                return StripHtml(Encoding.UTF8.GetString(content));

            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract text from {FileName}", fileName);
            return string.Empty;
        }
    }

    private static async Task<string> ExtractPdfAsync(byte[] content, CancellationToken ct)
    {
        await Task.CompletedTask; // iText7 is synchronous
        var sb = new StringBuilder();

        using var ms = new MemoryStream(content);
        using var reader = new iText.Kernel.Pdf.PdfReader(ms);
        using var doc = new iText.Kernel.Pdf.PdfDocument(reader);

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);
            var text = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(page);
            sb.AppendLine(text);
        }

        return sb.ToString();
    }

    private static string ExtractDocx(byte[] content)
    {
        using var ms = new MemoryStream(content);
        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var para in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            sb.AppendLine(para.InnerText);
        }
        return sb.ToString();
    }

    private static string ExtractXlsx(byte[] content, string ext)
    {
        using var ms = new MemoryStream(content);
        var reader = ext == "xls"
            ? ExcelDataReader.ExcelReaderFactory.CreateBinaryReader(ms)
            : ExcelDataReader.ExcelReaderFactory.CreateOpenXmlReader(ms);

        var dataSet = reader.AsDataSet(new ExcelDataReader.ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataReader.ExcelDataTableConfiguration { UseHeaderRow = false }
        });

        var sb = new StringBuilder();
        foreach (DataTable table in dataSet.Tables)
        {
            sb.AppendLine($"Sheet: {table.TableName}");
            foreach (DataRow row in table.Rows)
            {
                sb.AppendLine(string.Join("\t", row.ItemArray.Select(v => v?.ToString() ?? "")));
            }
        }
        return sb.ToString();
    }

    private static string StripHtml(string html)
    {
        return System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", " ")
               .Replace("&nbsp;", " ")
               .Replace("&amp;", "&")
               .Replace("&lt;", "<")
               .Replace("&gt;", ">")
               .Trim();
    }
}
