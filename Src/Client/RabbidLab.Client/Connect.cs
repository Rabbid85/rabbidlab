using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RabbidLab.Client;

/// <summary>Where to connect: from a rabbidlab://join link the server's page opens, or typed in.</summary>
internal sealed class JoinLink
{
    public string Server { get; set; } = "";
    public string Fingerprint { get; set; } = "";

    /// <summary>rabbidlab://join?server=...&amp;fingerprint=...; anything else gives empty fields.</summary>
    public static JoinLink Parse(string? link)
    {
        var j = new JoinLink();
        if (link is null || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != "rabbidlab")
        {
            return j;
        }
        foreach (var pair in uri.Query.TrimStart('?').Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }
            var value = Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' ')).Trim();
            switch (pair.Substring(0, eq))
            {
                case "server": j.Server = value.TrimEnd('/'); break;
                case "fingerprint": j.Fingerprint = Settings.CleanFingerprint(value) ?? ""; break;
            }
        }
        return j;
    }

    /// <summary>What's wrong with it, or null when it's good to save.</summary>
    public string? Problem()
    {
        // It goes on a command line (--join), so nothing that could split or end an argument.
        if (Server.Any(c => char.IsWhiteSpace(c) || c == '"' || c == '\\'))
        {
            return "The server's address can't have spaces, quotes or backslashes in it.";
        }
        if (!Uri.TryCreate(Server, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http") || u.Host.Length == 0)
        {
            return "The server's address starts with https:// (or http://), such as https://rabbidlab.example.com:8420.";
        }
        if (Fingerprint.Length > 0 && !Regex.IsMatch(Fingerprint, "^[0-9A-F]{64}$"))
        {
            return "The fingerprint is 64 letters and numbers (0-9, A-F).";
        }
        return null;
    }

    /// <summary>
    /// Saved where the installer keeps its settings, then the service restarted so it joins straight away. Run as an
    /// administrator (--join); the window asks Windows for that.
    /// </summary>
    public int Save()
    {
        if (Problem() is { } problem)
        {
            Console.Error.WriteLine(problem);
            return 2;
        }
        using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var key = machine.CreateSubKey(Settings.InstalledKey))
        {
            key.SetValue("Server", Server);
            key.SetValue("CertificateFingerprint", Fingerprint);
            key.DeleteValue("JoinKey", false); // from 0.3, when joining took a key
        }
        try
        {
            using var service = new ServiceController(ClientService.Name);
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
            }
            service.Start();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            // Not installed as a service (tried from a copy), or slow to stop: it's saved, and read at the next round.
        }
        return 0;
    }
}

/// <summary>
/// Connect this computer: the server (filled in from the link the server's page opened), and Connect,
/// which asks Windows for an administrator because it changes the whole computer. Then how joining goes.
/// </summary>
internal sealed class ConnectWindow : Form
{
    private readonly TextBox server = new();
    private readonly Label note = new() { AutoSize = false, Height = 54 };
    private readonly Label said = new() { AutoSize = false, Height = 44 };
    private readonly Button go = new() { Text = "Connect", Height = 36, Width = 130, FlatStyle = FlatStyle.System };
    private readonly string fingerprint;
    private bool done;

    public ConnectWindow(JoinLink link)
    {
        fingerprint = link.Fingerprint;
        Text = "Rabbid Lab";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Look.Night;
        ForeColor = Look.Ink;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(460, 300);
        Padding = new Padding(22);

        var title = new Label { Text = "Connect this computer", AutoSize = false, Height = 36, Dock = DockStyle.Top, Font = new Font("Segoe UI", 15f, FontStyle.Bold) };
        var lead = new Label
        {
            Text = $"{Machine.Name} becomes one of the server's seats: it shows up in Rabbid Lab, and the people who run it can make and change its Windows accounts.",
            AutoSize = false, Height = 58, Dock = DockStyle.Top, ForeColor = Look.Muted,
        };
        server.Text = link.Server;
        foreach (var box in new[] { server })
        {
            box.BackColor = Look.Field;
            box.ForeColor = Look.Ink;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Dock = DockStyle.Top;
        }
        note.Dock = DockStyle.Top;
        said.Dock = DockStyle.Top;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", Height = 36, Width = 100, FlatStyle = FlatStyle.System };
        cancel.Click += (_, _) => Close();
        go.Click += async (_, _) => await Connect();
        buttons.Controls.Add(go);
        buttons.Controls.Add(cancel);
        AcceptButton = go;
        CancelButton = cancel;

        // Top to bottom (docked controls stack in reverse).
        foreach (var c in new Control[] { said, note, server, Caption("Server address"), lead, title })
        {
            Controls.Add(c);
        }
        Controls.Add(buttons);
        Shield(go);

        var now = Settings.Read();
        if (now.PolicyServer is not null)
        {
            Say(note, $"Group Policy picks this computer's server ({now.PolicyServer}), so it can't be changed here.", Look.Amber);
            server.Enabled = go.Enabled = false;
        }
        else if (now.Server is not null && !string.Equals(now.Server, link.Server, StringComparison.OrdinalIgnoreCase) && link.Server.Length > 0)
        {
            Say(note, $"This computer is connected to {now.Server}. Connecting moves it to {link.Server}.", Look.Amber);
        }
        else if (link.Server.Length > 0)
        {
            Say(note, "Check the address is your Rabbid Lab, then Connect.", Look.Muted);
        }
        server.TextChanged += (_, _) =>
        {
            if (go.Enabled && !done)
            {
                WarnPlain();
            }
        };
        WarnPlain();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Look.Fit(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Look.DarkTitle(Handle);
    }

    /// <summary>http:// works (Rabbid Lab in Docker serves it), but it isn't encrypted: said before connecting.</summary>
    private void WarnPlain()
    {
        var plain = server.Text.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        Say(said, plain ? "This address uses http://, so the connection to the server is not encrypted. Use https:// if the server has it." : "", Look.Amber);
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = false, Height = 26, Dock = DockStyle.Top, ForeColor = Look.Muted, Padding = new Padding(0, 6, 0, 0) };

    private static void Say(Label label, string text, Color colour)
    {
        label.Text = text;
        label.ForeColor = colour;
    }

    private async Task Connect()
    {
        if (done)
        {
            Close();
            return;
        }
        var link = new JoinLink { Server = server.Text.Trim().TrimEnd('/'), Fingerprint = fingerprint };
        if (link.Problem() is { } problem)
        {
            Say(said, problem, Look.Red);
            return;
        }
        go.Enabled = false;
        Say(said, "Connecting…", Look.Muted);
        try
        {
            var args = $"--join \"{link.Server}\" \"{link.Fingerprint}\"";
            using var admin = Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { Verb = "runas", UseShellExecute = true });
            await Task.Run(() => admin?.WaitForExit());
            if (admin?.ExitCode is not 0 and not null)
            {
                Say(said, "That didn't save. Try again, or install with the server's address.", Look.Red);
                go.Enabled = true;
                return;
            }
        }
        catch (Win32Exception)
        {
            Say(said, "Windows needs an administrator's OK to connect this computer.", Look.Red);
            go.Enabled = true;
            return;
        }
        // The service joins as soon as it starts again: how it went, from status.json.
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            var s = Status.Read();
            if (s?.Server == link.Server && s.State is Status.Connected or Status.Waiting)
            {
                Say(said, s.State == Status.Connected ? $"Connected. {Machine.Name} shows up in Rabbid Lab's Seats." : $"Joined. {Machine.Name} waits for someone to let it in under Seats.", Look.Green);
                go.Text = "Done";
                go.Enabled = true;
                done = true;
                return;
            }
            if (s?.Server == link.Server && s.State is Status.Refused or Status.Unreachable or Status.IdentityUnreadable)
            {
                Say(said, s.Describe(), Look.Red);
                go.Enabled = true;
                return;
            }
        }
        Say(said, "Saved. It's still joining; the tray icon says when it's connected.", Look.Muted);
        go.Enabled = true;
    }

    /// <summary>The administrator shield on a button, as Windows draws it.</summary>
    private static void Shield(Button button) => SendMessage(button.Handle, 0x160C, IntPtr.Zero, new IntPtr(1));

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr w, IntPtr l);
}
