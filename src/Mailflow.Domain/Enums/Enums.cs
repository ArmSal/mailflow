namespace Mailflow.Domain.Enums;

public enum ConditionField { FromAddress, FromDomain, ToAddress, Subject, Body, AttachmentName, PdfText, HasPdf }

public enum ConditionOperator { Contains, NotContains, Equals, StartsWith, EndsWith, Regex }

public enum ActionType { SaveToCategory, Print, MarkAsRead, MoveToFolder, LeaveUnread, Skip }

public enum AfterProcessing { MarkRead, Move, Leave }

public enum MatchMode { All, Any }

public enum PrinterKind { Local, PrintNode }

/// <summary>
/// Whether the sender of an email is checked before automatic printing (Setting 'PrintSenderCheck', default Warn).
/// Off: never checked. Warn: prints as before but History flags unverified senders. Strict: prints only verified senders.
/// </summary>
public enum SenderCheckMode { Off, Warn, Strict }

/// <summary>Result of the receiving mail server's own sender authentication (DKIM / DMARC) for one message.</summary>
public enum SenderVerification { Unknown, Pass, Fail }

/// <summary>How a mailbox signs in to its IMAP server.</summary>

public enum MessageStatus { Processed, Failed, Skipped }
