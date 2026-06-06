using MimeKit;

namespace EmailAI.Infrastructure.Services.Mail;

internal static class MimeAttachmentHelper
{
    public static IList<MimePart> ListParts(MimeEntity? entity)
    {
        var parts = new List<MimePart>();
        CollectParts(entity, parts);
        return parts;
    }

    private static void CollectParts(MimeEntity? entity, List<MimePart> parts)
    {
        if (entity is null) return;

        if (entity is MimePart part && ShouldTreatAsAttachment(part))
            parts.Add(part);

        if (entity is Multipart multipart)
        {
            foreach (var sub in multipart)
                CollectParts(sub, parts);
        }
        else if (entity is MessagePart messagePart)
        {
            CollectParts(messagePart.Message?.Body, parts);
        }
    }

    private static bool ShouldTreatAsAttachment(MimePart part)
    {
        if (part.IsAttachment) return true;
        if (part.ContentDisposition?.IsAttachment == true) return true;
        if (string.IsNullOrEmpty(part.FileName)) return false;

        var mime = part.ContentType.MimeType;
        return mime is not ("text/plain" or "text/html");
    }

    public static string ResolveAttachmentId(MimePart part, int index)
        => !string.IsNullOrEmpty(part.ContentId)
            ? part.ContentId
            : $"att:{index}";

    public static bool MatchesAttachmentId(MimePart part, int index, string attachmentId)
    {
        if (attachmentId == ResolveAttachmentId(part, index)) return true;
        if (attachmentId == $"att:{index}") return true;
        if (!string.IsNullOrEmpty(part.ContentId) && part.ContentId == attachmentId) return true;
        if (!string.IsNullOrEmpty(part.FileName) && part.FileName == attachmentId) return true;
        return false;
    }
}
