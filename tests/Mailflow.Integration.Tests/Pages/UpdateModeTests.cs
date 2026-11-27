using Mailflow.Web.Updates;

namespace Mailflow.Integration.Tests.Pages;

public sealed class UpdateModeTests
{
    private static readonly ReleaseInfo Rel = new("v1.4.0", "https://github.com/x/y/releases/tag/v1.4.0");

    [Theory]
    [InlineData(null, UpdateMode.Notify)]
    [InlineData("", UpdateMode.Notify)]
    [InlineData("junk", UpdateMode.Notify)]
    [InlineData("99", UpdateMode.Notify)]
    [InlineData("Notify", UpdateMode.Notify)]
    [InlineData("manual", UpdateMode.Manual)]
    [InlineData(" Auto ", UpdateMode.Auto)]
    public void ParseMode_defaults_to_notify(string? value, UpdateMode expected)
        => Assert.Equal(expected, UpdateChecker.ParseMode(value));

    [Theory]
    [InlineData(UpdateMode.Notify, false)]
    [InlineData(UpdateMode.Manual, true)]
    [InlineData(UpdateMode.Auto, true)]
    public void Install_button_is_offered_unless_notify(UpdateMode mode, bool expected)
        => Assert.Equal(expected, new UpdateChecker(null!) { Mode = mode }.InstallEnabled);

    [Fact]
    public void Auto_installs_a_newer_release_on_a_setup_install()
        => Assert.Equal(Rel, UpdateChecker.AutoCandidate(UpdateMode.Auto, Rel, true, null));

    [Theory]
    [InlineData(UpdateMode.Notify)]
    [InlineData(UpdateMode.Manual)]
    public void Only_auto_mode_installs_by_itself(UpdateMode mode)
        => Assert.Null(UpdateChecker.AutoCandidate(mode, Rel, true, null));

    [Fact]
    public void Auto_never_retries_a_version_that_was_already_attempted()
        => Assert.Null(UpdateChecker.AutoCandidate(UpdateMode.Auto, Rel, true, "v1.4.0"));

    [Fact]
    public void Auto_tries_a_newer_version_after_a_failed_one()
        => Assert.Equal(Rel, UpdateChecker.AutoCandidate(UpdateMode.Auto, Rel, true, "v1.3.9"));

    [Fact]
    public void Auto_needs_a_setup_install_and_an_available_release()
    {
        Assert.Null(UpdateChecker.AutoCandidate(UpdateMode.Auto, Rel, false, null));
        Assert.Null(UpdateChecker.AutoCandidate(UpdateMode.Auto, null, true, null));
    }
}
