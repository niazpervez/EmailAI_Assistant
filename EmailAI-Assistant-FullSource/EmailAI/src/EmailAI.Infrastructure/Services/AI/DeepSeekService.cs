using EmailAI.Core;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmailAI.Infrastructure.Services.AI;

/// <summary>
/// DeepSeek API integration for chat completions and embeddings.
/// DeepSeek is OpenAI-API-compatible, so we use the same HTTP format.
/// </summary>
public sealed class DeepSeekAIService : IAIService
{
    private readonly HttpClient _http;
    private readonly ILogger<DeepSeekAIService> _logger;
    private const string BaseUrl = "https://api.deepseek.com/v1";
    private const string Model = "deepseek-chat";

    public DeepSeekAIService(HttpClient http, ILogger<DeepSeekAIService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string> ChatAsync(
        string userMessage,
        IEnumerable<Email> contextEmails,
        IEnumerable<ChatMessage> history,
        CancellationToken ct = default)
    {
        var messages = new List<object>();

        // System prompt
        messages.Add(new { role = "system", content = BuildSystemPrompt() });

        // Recent history (last 10 turns)
        foreach (var h in history.TakeLast(10))
            messages.Add(new { role = h.Role, content = h.Content });

        // Inject email context
        var emailContext = BuildEmailContext(contextEmails);
        if (!string.IsNullOrEmpty(emailContext))
        {
            messages.Add(new
            {
                role = "user",
                content = $"Here are the most relevant emails from the mailbox:\n\n{emailContext}\n\nNow answer: {userMessage}"
            });
        }
        else
        {
            messages.Add(new { role = "user", content = userMessage });
        }

        return await CallChatApiAsync(messages, ct);
    }

    public async Task<string> SummarizeEmailsAsync(
        IEnumerable<Email> emails, string summaryType, CancellationToken ct = default)
    {
        var emailList = emails.ToList();
        var context = BuildEmailContext(emailList);

        var prompt = summaryType switch
        {
            "daily" => $"""
                Provide a concise daily email summary using this structure:
                ## Daily Summary
                ### Overview
                (2-3 sentences)
                ### Email highlights (table if multiple items)
                | Topic | Sender | Action needed |
                |-------|--------|---------------|
                (rows)
                **Key insight:** (one line)
                **Action required:** (one line)
                
                Emails:
                {context}
                """,
            "weekly" => $"""
                Provide a weekly email summary report using:
                ## Weekly Report
                ### Major themes
                (bullets)
                ### Data summary (markdown table for counts, customers, issues)
                | Category | Count/Detail | Trend |
                |----------|--------------|-------|
                (rows with ↑↓ trends where applicable)
                **Key insight:** ...
                **Action required:** ...
                
                Emails:
                {context}
                """,
            "customer" => $"Summarize all customer-related emails. Group by customer. Highlight:\n- Customer requests\n- Complaints\n- Order status\n- Pending responses\n\nEmails:\n{context}",
            _ => $"Summarize these emails concisely, highlighting key points and action items:\n\n{context}"
        };

        var messages = new List<object>
        {
            new { role = "system", content = "You are an expert email analyst. Be concise, structured, and actionable." },
            new { role = "user", content = prompt }
        };

        return await CallChatApiAsync(messages, ct);
    }

    public async Task<string> GenerateReplyAsync(
        Email email, string replyType, string? userInstructions = null, CancellationToken ct = default)
    {
        var tone = replyType switch
        {
            "professional" => "professional and polished",
            "friendly"     => "warm and friendly",
            "formal"       => "very formal and business-like",
            "short"        => "brief and to the point (3-4 sentences max)",
            _              => "professional"
        };

        var prompt = $"""
            Draft a {tone} email reply to the following email.
            
            Original Email:
            From: {email.SenderName} <{email.Sender}>
            Subject: {email.Subject}
            Date: {email.ReceivedDate:f}
            
            Body:
            {email.BodyText.Truncate(2000)}
            
            {(userInstructions is not null ? $"Additional instructions: {userInstructions}" : "")}
            
            Write only the reply body (no subject line or headers). Do not include placeholders like [Your Name].
            """;

        var messages = new List<object>
        {
            new { role = "system", content = "You are an expert email writer. Write clear, appropriate email replies." },
            new { role = "user", content = prompt }
        };

        return await CallChatApiAsync(messages, ct);
    }

    public async Task<string> ExtractActionItemsAsync(
        IEnumerable<Email> emails, string? supplementaryContext = null, CancellationToken ct = default)
    {
        var context = BuildEmailContext(emails.Take(20));
        var extra = string.IsNullOrWhiteSpace(supplementaryContext)
            ? ""
            : $"\n\n{supplementaryContext}\n\nPrioritize action items related to sent follow-ups that still await a reply.";
        var messages = new List<object>
        {
            new { role = "system", content = "You are an expert at extracting action items from email conversations." },
            new
            {
                role = "user",
                content = $"""
                    Extract all action items, tasks, and follow-ups from these emails.
                    Include reminders to chase replies when a sent follow-up has no response yet.
                    Format as a numbered list. Include who is responsible and any deadlines mentioned.
                    If no action items exist, say "No action items found."
                    {extra}
                    Emails:
                    {context}
                    """
            }
        };

        return await CallChatApiAsync(messages, ct);
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private async Task<string> CallChatApiAsync(List<object> messages, CancellationToken ct)
    {
        var request = new
        {
            model = Model,
            messages,
            temperature = 0.7,
            max_tokens = 2048,
            stream = false
        };

        try
        {
            var response = await _http.PostAsJsonAsync($"{BaseUrl}/chat/completions", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new InvalidOperationException(
                        "AI service error: DeepSeek rejected the API key (401). Open Settings → DeepSeek AI, paste a fresh key from platform.deepseek.com, and click Save API Key. Then restart the app.");

                response.EnsureSuccessStatusCode();
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "No response generated.";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "DeepSeek API call failed");
            throw new InvalidOperationException($"AI service error: {ex.Message}", ex);
        }
    }

    private static string BuildSystemPrompt() => """
        You are EmailAI Assistant, an intelligent email management AI.
        You help users understand and manage their email inbox.
        You receive ranked email context with excerpts, thread messages, and attachment text when available.
        
        Format every answer for rich UI rendering using Markdown:
        - Start with a ## section title
        - Use markdown TABLES for any numeric or comparison data (customers, counts, dates, amounts)
        - Use **Label:** insight lines for key findings, risks, and action items
        - Use bullet lists for details; use numbered lists for prioritized steps
        - Highlight trends with ↑ (up) or ↓ (down) where relevant
        - Structure summaries as: Overview → Data table → Key insights → Recommended actions
        
        Guidelines:
        - Be concise and accurate
        - Cite specific emails (sender, subject, date) when answering
        - Never fabricate email content not provided in context
        - If you cannot find relevant information, say so clearly
        """;

    private static string BuildEmailContext(IEnumerable<Email> emails)
    {
        var sb = new StringBuilder();
        int i = 1;
        foreach (var email in emails.Take(AppConstants.MaxContextEmails))
        {
            sb.AppendLine($"--- Email {i++} ---");
            sb.AppendLine($"From: {email.SenderName} <{email.Sender}>");
            sb.AppendLine($"Subject: {email.Subject}");
            sb.AppendLine($"Date: {email.ReceivedDate:f}");
            sb.AppendLine($"Folder: {email.FolderName}");
            if (email.HasAttachments)
                sb.AppendLine("(includes attachment text when available)");
            sb.AppendLine($"Body: {email.BodyText.Truncate(AppConstants.MaxBodyLength)}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

/// <summary>
/// Embedding service using DeepSeek's embedding endpoint (OpenAI-compatible).
/// Falls back to a deterministic hash-based vector when API is unavailable.
/// </summary>
public sealed class DeepSeekEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;
    private readonly ILogger<DeepSeekEmbeddingService> _logger;
    private const string EmbeddingModel = "text-embedding-3-small"; // DeepSeek supports OpenAI-compatible embedding
    private const string BaseUrl = "https://api.deepseek.com/v1";

    public int Dimensions => AppConstants.EmbeddingDimensions;
    public string ModelName => EmbeddingModel;

    public DeepSeekEmbeddingService(HttpClient http, ILogger<DeepSeekEmbeddingService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<float[]> GenerateAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new float[Dimensions];

        // Truncate to ~8000 chars to stay within token limits
        var input = text.Length > 8000 ? text[..8000] : text;

        try
        {
            var request = new { model = EmbeddingModel, input };
            var response = await _http.PostAsJsonAsync($"{BaseUrl}/embeddings", request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var vectorArr = doc.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding");

            return vectorArr.EnumerateArray().Select(v => v.GetSingle()).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding API failed, using fallback");
            return GenerateFallbackVector(input);
        }
    }

    /// <summary>
    /// Deterministic hash-based embedding fallback (for offline/no-API-key mode).
    /// Not semantically accurate but maintains vector shape.
    /// </summary>
    private static float[] GenerateFallbackVector(string text)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var vec = new float[AppConstants.EmbeddingDimensions];
        var rand = new Random(BitConverter.ToInt32(hash, 0));
        for (int i = 0; i < vec.Length; i++)
            vec[i] = (float)(rand.NextDouble() * 2 - 1);
        // Normalize
        float norm = MathF.Sqrt(vec.Sum(v => v * v));
        for (int i = 0; i < vec.Length; i++) vec[i] /= norm;
        return vec;
    }
}

// ── Extension helpers ─────────────────────────────────────────────────────────

internal static class StringExtensions
{
    public static string Truncate(this string s, int maxLength) =>
        s.Length <= maxLength ? s : s[..maxLength] + "…";
}
