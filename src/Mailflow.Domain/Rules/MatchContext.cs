namespace Mailflow.Domain.Rules;

/// <summary>Facts about one email, evaluated by the rule engine.</summary>
public sealed record MatchContext(
    string FromAddress,
    string ToAddress,
    string Subject,
    string Body,
    IReadOnlyList<string> AttachmentNames,
    string PdfText,
    bool HasPdf)
{
    public string FromDomain => FromAddress.Contains('@') ? FromAddress[(FromAddress.LastIndexOf('@') + 1)..] : "";
}
