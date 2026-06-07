using EmailAI.Core;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;

namespace EmailAI.Infrastructure.Services;

public sealed class TextChunker : ITextChunker
{
    public IEnumerable<(int Index, string Source, string Content)> BuildChunks(Email email, string? attachmentText)
    {
        int index = 0;

        var header = $"Subject: {email.Subject}\nFrom: {email.SenderName} <{email.Sender}>\nDate: {email.ReceivedDate:f}";
        if (!string.IsNullOrWhiteSpace(header))
            yield return (index++, "header", header);

        foreach (var part in SplitText(email.BodyText, AppConstants.ChunkSizeChars, AppConstants.ChunkOverlapChars))
            yield return (index++, "body", part);

        if (!string.IsNullOrWhiteSpace(attachmentText))
        {
            foreach (var part in SplitText(attachmentText, AppConstants.ChunkSizeChars, AppConstants.ChunkOverlapChars))
                yield return (index++, "attachment", part);
        }
    }

    internal static IEnumerable<string> SplitText(string text, int chunkSize, int overlap)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var normalized = text.Replace("\r\n", "\n").Trim();
        if (normalized.Length <= chunkSize)
        {
            yield return normalized;
            yield break;
        }

        int start = 0;
        while (start < normalized.Length)
        {
            var length = Math.Min(chunkSize, normalized.Length - start);
            var slice = normalized.Substring(start, length).Trim();
            if (slice.Length > 0)
                yield return slice;

            if (start + length >= normalized.Length) break;
            start += Math.Max(1, chunkSize - overlap);
        }
    }
}
