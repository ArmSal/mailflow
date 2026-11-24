namespace Mailflow.Domain.Enums;

public enum ConditionField { FromAddress, FromDomain, ToAddress, Subject, Body, AttachmentName, PdfText, HasPdf }

public enum ConditionOperator { Contains, NotContains, Equals, StartsWith, EndsWith, Regex }

public enum ActionType { SaveToCategory, Print, MarkAsRead, MoveToFolder, LeaveUnread, Skip }

public enum AfterProcessing { MarkRead, Move, Leave }

public enum MatchMode { All, Any }

public enum PrinterKind { Local, PrintNode }

public enum MessageStatus { Processed, Failed, Skipped }
