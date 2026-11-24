namespace Mailflow.Web.Worker;

public sealed class WorkerOptions
{
    public const string Section = "Worker";

    /// <summary>Delay between scheduled runs.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}
