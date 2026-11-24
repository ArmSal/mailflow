using Mailflow.Domain.Enums;

namespace Mailflow.Domain.Entities;

public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Mailbox
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string EncryptedPassword { get; set; } = "";
    public AuthMethod AuthMethod { get; set; } = AuthMethod.Password;
    /// <summary>Encrypted OAuth token cache (never the plain refresh token); only used when AuthMethod is MicrosoftOAuth.</summary>
    public string? EncryptedOAuthCache { get; set; }
    public string InboxFolder { get; set; } = "INBOX";
    public int CheckIntervalMinutes { get; set; } = 5;
    public int LookbackDays { get; set; } = 3;
    public AfterProcessing DefaultAfterProcessing { get; set; } = AfterProcessing.Leave;
    public bool IsActive { get; set; } = true;
    public DateTime? LastCheckAt { get; set; }
    public string? LastError { get; set; }
}

public class Printer
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public string DisplayName { get; set; } = "";
    public string WindowsName { get; set; } = "";
    public PrinterKind Kind { get; set; } = PrinterKind.Local;
    public int DefaultCopies { get; set; } = 1;
    public bool Duplex { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string LocalSubfolder { get; set; } = "";
    public string? ImapFolder { get; set; }
    public string Color { get; set; } = "#6c757d";
    public int SortOrder { get; set; }
}

public class Rule
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    public MatchMode MatchMode { get; set; } = MatchMode.All;
    public bool StopProcessing { get; set; }
    public int? MailboxId { get; set; }
    public List<RuleCondition> Conditions { get; set; } = [];
    public List<RuleAction> Actions { get; set; } = [];
}

public class RuleCondition
{
    public int Id { get; set; }
    public int RuleId { get; set; }
    public ConditionField Field { get; set; }
    public ConditionOperator Operator { get; set; }
    public string Value { get; set; } = "";
    public bool CaseSensitive { get; set; }
}

public class RuleAction
{
    public int Id { get; set; }
    public int RuleId { get; set; }
    public ActionType Type { get; set; }
    public int? CategoryId { get; set; }
    public int? PrinterId { get; set; }
    public int? Copies { get; set; }
    public string? ImapFolder { get; set; }
}

public class AllowedSender
{
    public int Id { get; set; }
    /// <summary>Email address or domain.</summary>
    public string Pattern { get; set; } = "";
    public int? StoreId { get; set; }
}

public class ProcessedMessage
{
    public int Id { get; set; }
    public int MailboxId { get; set; }
    public string MessageId { get; set; } = "";
    public uint ImapUid { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string From { get; set; } = "";
    public string Subject { get; set; } = "";
    public int? MatchedRuleId { get; set; }
    public SenderVerification SenderVerification { get; set; } = SenderVerification.Unknown;
    public MessageStatus Status { get; set; }
    public DateTime ProcessedAt { get; set; }
}

public class Document
{
    public int Id { get; set; }
    public int ProcessedMessageId { get; set; }
    public string Sha256 { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string StoredPath { get; set; } = "";
    public int CategoryId { get; set; }
    public DateTime? PrintedAt { get; set; }
    public int? PrinterId { get; set; }
    public string? PrintError { get; set; }
}

public class RunLog
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public int? MailboxId { get; set; }
    public int MessagesHandled { get; set; }
    public int DocumentsSaved { get; set; }
    public int Printed { get; set; }
    public string? Error { get; set; }
}

public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
