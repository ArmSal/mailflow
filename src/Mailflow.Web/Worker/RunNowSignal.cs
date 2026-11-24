using System.Threading.Channels;

namespace Mailflow.Web.Worker;

/// <summary>Coalescing "run now" request: several requests before the worker wakes up count as one.</summary>
public sealed class RunNowSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Request() => _channel.Writer.TryWrite(true);

    public async Task WaitAsync(CancellationToken ct)
    {
        await _channel.Reader.ReadAsync(ct);
    }
}
