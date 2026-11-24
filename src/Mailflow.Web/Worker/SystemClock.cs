using Mailflow.Application.Abstractions;

namespace Mailflow.Web.Worker;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
