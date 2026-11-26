using Mailflow.Desktop;

namespace Mailflow.Desktop.Tests;

public class ToastNotifierTests
{
    [Fact]
    public void Forwards_title_and_message()
    {
        (string, string)? shown = null;
        new ToastNotifier((t, m) => shown = (t, m)).Notify("Titre", "Message");
        Assert.Equal(("Titre", "Message"), shown);
    }

    [Fact]
    public void A_failing_display_never_throws_and_long_messages_are_cut()
    {
        new ToastNotifier((_, _) => throw new InvalidOperationException()).Notify("t", "m");
        string? shown = null;
        new ToastNotifier((_, m) => shown = m).Notify("t", new string('x', 1000));
        Assert.Equal(250, shown!.Length);
    }
}
