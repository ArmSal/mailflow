namespace Mailflow.Desktop;

/// <summary>Error dialog behind a replaceable function (tests and automated runs can swap it).</summary>
internal static class Dialogs
{
    public static Action<string> ShowError { get; set; } = message =>
        MessageBox.Show(message, "Mailflow", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
