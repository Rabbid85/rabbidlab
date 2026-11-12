using System.ComponentModel;
using System.Diagnostics;

namespace RabbidLab.Server;

/// <summary>
/// --open &lt;address&gt;: what the Start menu and desktop shortcuts run. It checks that Rabbid Lab is answering there; if it
/// isn't, its service is started (through Setup, which asks Windows for permission), and then the console opens in the browser.
/// </summary>
public static class Opener
{
    public static async Task<int> Open(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var site) || (site.Scheme != Uri.UriSchemeHttps && site.Scheme != Uri.UriSchemeHttp))
        {
            return 1;
        }
        // Twice, a moment apart: a service that's just starting gets the chance to answer before anything is asked.
        if (!await Answers(site) && !await After(2, site))
        {
            StartService();
            for (var second = 0; second < 30 && !await After(1, site); second++)
            {
            }
        }
        try
        {
            Process.Start(new ProcessStartInfo(site.ToString()) { UseShellExecute = true })?.Dispose();
            return 0;
        }
        catch (Win32Exception)
        {
            return 1; // no browser to open it with
        }
    }

    /// <summary>Whether Rabbid Lab answers its health check there. Its own certificate is fine: nothing is sent but the question.</summary>
    public static async Task<bool> Answers(Uri site)
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var answer = await http.GetAsync(new Uri(site, "/api/health"));
            return answer.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static async Task<bool> After(int seconds, Uri site)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        return await Answers(site);
    }

    // Setup's own "start the service": it shows what it's doing, and Windows asks for permission first.
    private static void StartService()
    {
        var setup = Path.Combine(AppContext.BaseDirectory, "Setup", "RabbidLab-Setup.exe");
        if (!OperatingSystem.IsWindows() || !File.Exists(setup))
        {
            return;
        }
        try
        {
            using var start = Process.Start(new ProcessStartInfo(setup, "/service start") { UseShellExecute = true });
            start?.WaitForExit();
        }
        catch (Win32Exception)
        {
            // They said no to Windows' prompt: the browser opens anyway and says it can't connect.
        }
    }
}
