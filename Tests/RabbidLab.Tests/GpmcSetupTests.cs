using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>The Group Policy Management install, run for real through Windows PowerShell with a stand-in command.</summary>
public class GpmcSetupTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task A_warning_and_a_restart_are_still_an_install_that_worked_and_its_percentages_are_heard()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var heard = new List<int>();
        var ended = await GpmcSetup.Install(
            "foreach ($p in 10, 45, 100) { Write-Progress -Activity 'Installing' -PercentComplete $p; Start-Sleep -Milliseconds 900 }; "
            + "Write-Warning 'You must restart this server to finish the installation process.'; [pscustomobject]@{ Success = $true; RestartNeeded = 'Yes' }",
            p => { lock (heard) { heard.Add(p); } }, Limit);

        Assert.True(ended.Ok, ended.Text);
        Assert.True(ended.Restart);
        Assert.Contains("restart this server", ended.Text);
        Assert.NotEmpty(heard);
        Assert.Equal(heard.OrderBy(p => p), heard); // only ever forward
        Assert.DoesNotContain("PCT", ended.Text);
        Assert.DoesNotContain("RESULT", ended.Text);
    }

    [Fact]
    public async Task No_restart_asked_for_is_a_plain_install()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        // The capability's answer on Windows 10 and 11: no Success, and RestartNeeded as true or false.
        var ended = await GpmcSetup.Install("[pscustomobject]@{ Online = $true; RestartNeeded = $false }", _ => { }, Limit);

        Assert.True(ended.Ok, ended.Text);
        Assert.False(ended.Restart);
    }

    [Fact]
    public async Task An_error_or_an_answer_that_says_it_failed_is_a_failure_with_what_Windows_said()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var thrown = await GpmcSetup.Install("throw 'The source files could not be found.'", _ => { }, Limit);
        var written = await GpmcSetup.Install("Write-Error 'Access is denied.'", _ => { }, Limit);
        var said = await GpmcSetup.Install("[pscustomobject]@{ Success = $false; RestartNeeded = 'No' }", _ => { }, Limit);

        Assert.False(thrown.Ok);
        Assert.Contains("source files", thrown.Text);
        Assert.False(written.Ok);
        Assert.Contains("Access is denied", written.Text);
        Assert.False(said.Ok);
    }
}
