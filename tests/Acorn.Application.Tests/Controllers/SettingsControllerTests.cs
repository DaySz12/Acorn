using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Acorn.Application.Tests.Controllers;

public class SettingsControllerTests
{
    [Fact]
    public async Task Open_builds_the_form_from_vault_settings()
    {
        using var h = new ControllerHarness();
        await h.UnlockAsync();

        h.Settings.Open();

        var form = h.State.Settings!;
        Assert.Equal(Screen.Settings, h.State.Screen);
        Assert.Equal(30, form.ClipboardClearSeconds);
        Assert.Equal("dev, staging, prod", form.EnvironmentsText);
        Assert.Equal("C:\\data\\Acorn", form.StorageLocation);
        Assert.False(form.KdfBelowRecommended);
    }

    [Fact]
    public async Task Out_of_range_values_are_rejected_without_saving()
    {
        using var h = new ControllerHarness();
        await h.UnlockAsync();
        h.Settings.Open();
        h.State.Settings!.AutoLockMinutes = 0;
        h.State.Settings!.EnvironmentsText = "dev, Pro d!";

        var result = await h.Settings.SaveAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(2, h.State.Settings!.Errors.Count);
        h.Session.DidNotReceive().Save();
        Assert.Equal(5, h.Data.Settings.AutoLockMinutes);
    }

    [Fact]
    public async Task Saving_applies_timers_screen_protection_and_theme()
    {
        using var h = new ControllerHarness();
        await h.UnlockAsync();
        h.Settings.Open();
        var form = h.State.Settings!;
        form.AutoLockMinutes = 15;
        form.ScreenCaptureProtection = false;
        form.Theme = "light";
        form.EnvironmentsText = "dev qa prod";

        var result = await h.Settings.SaveAsync();

        Assert.True(result.Succeeded);
        h.Session.Received(1).Save();
        h.AutoLock.Received().Start(TimeSpan.FromMinutes(15));
        h.ScreenProtection.Received().SetEnabled(false);
        Assert.Equal("light", h.State.Theme);
        Assert.Equal(["dev", "qa", "prod"], h.Data.Settings.Environments);
        Assert.Contains(h.State.Sidebar.Environments, e => e.Name == "qa");
    }

    [Fact]
    public async Task Failed_save_restores_previous_settings()
    {
        using var h = new ControllerHarness();
        await h.UnlockAsync();
        h.Session.Save().Throws(new IOException("read-only"));

        var result = await h.Settings.SetThemeAsync("dark");

        Assert.Equal(Messages.StorageError, result.Error);
        Assert.Equal("system", h.Data.Settings.Theme);
    }
}
