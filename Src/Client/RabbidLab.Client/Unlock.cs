using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RabbidLab.Client;

/// <summary>
/// Uninstall, while it's locked: this computer's uninstall code, checked here without the server (Removal.UnlockWith).
/// The right code allows uninstalling for an hour, and moving the computer to another server. Run as an administrator
/// (--unlock), which the client's window asks Windows for.
/// </summary>
internal sealed class UnlockWindow : Form
{
    private readonly TextBox code = new() { MaxLength = 40, CharacterCasing = CharacterCasing.Upper };
    private readonly Label said = new() { AutoSize = false, Height = 62, Dock = DockStyle.Top, Padding = new Padding(0, 8, 0, 0) };
    private readonly Button go = Look.Button("Unlock", primary: true);
    private readonly Button cancel = Look.Button("Cancel", primary: false);
    private bool unlocked;

    public UnlockWindow()
    {
        var now = Status.Read() ?? new Status();
        Text = "Rabbid Lab Client";
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

        var has = Removal.CodeCheck is not null;
        var title = new Label { Text = "Enter the uninstall code", AutoSize = false, Height = 36, Dock = DockStyle.Top, Font = new Font("Segoe UI", 15f, FontStyle.Bold) };
        var lead = new Label
        {
            Text = has ? $"Uninstalling is locked by {now.Host}. An admin can find this computer's uninstall code in Rabbid Lab: go to Devices, click Uninstall next to {Machine.Name}, then click Show."
                : "This computer doesn't have an uninstall code yet. It gets one the next time it checks in with the server. Until then, an admin has to allow uninstalling in Rabbid Lab.",
            AutoSize = false, Height = 78, Dock = DockStyle.Top, ForeColor = Look.Muted,
        };
        code.Font = new Font("Consolas", 11f);
        var field = Look.Box(code);
        var caption = new Label { Text = "Uninstall code", AutoSize = false, Height = 26, Dock = DockStyle.Top, ForeColor = Look.Muted, Padding = new Padding(0, 6, 0, 0) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(0, 12, 0, 0), FlowDirection = FlowDirection.RightToLeft };
        cancel.Click += (_, _) => Close();
        go.Click += (_, _) => Go();
        buttons.Controls.Add(go);
        buttons.Controls.Add(cancel);
        AcceptButton = go;
        CancelButton = cancel;

        // Top to bottom (docked controls stack in reverse).
        foreach (var c in new Control[] { said, field, caption, lead, title })
        {
            Controls.Add(c);
        }
        Controls.Add(buttons);
        ClientSize = new Size(ClientSize.Width, Padding.Vertical + Controls.Cast<Control>().Sum(c => c.Height));
        if (Removal.Allowed)
        {
            Done(Removal.CodeUntil ?? Removal.Until);
        }
        else if (!has)
        {
            code.ReadOnly = true;
            go.Enabled = false;
        }
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

    private void Go()
    {
        if (unlocked)
        {
            if (!Removal.Uninstall(quiet: false))
            {
                Say("The uninstall didn't start. Use Settings > Apps > Installed apps instead.", Look.Red);
                return;
            }
            Close();
            return;
        }
        try
        {
            if (Removal.UnlockWith(code.Text) is not { } until)
            {
                Say("That code isn't right. Check it and try again.", Look.Red);
                code.SelectAll();
                code.Focus();
                return;
            }
            Journal.Write($"The uninstall code was entered on this computer by {WindowsIdentity.GetCurrent().Name}. Uninstalling the Rabbid Lab client is allowed until {until.ToLocalTime():g}.");
            // The service checks in again straight away, so the server hears the code was used before the client is gone.
            Task.Run(Journal.CheckInNow);
            Done(until);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            Say("This needs administrator permission. Close this window and click Uninstall again, then choose Yes.", Look.Red);
        }
    }

    // Unlocked: uninstall now, or close and do what else the lock was in the way of.
    private void Done(DateTime? until)
    {
        unlocked = true;
        code.ReadOnly = true;
        code.ForeColor = Look.Muted;
        Say(until is { } at ? $"Unlocked until {at.ToLocalTime():t}. You can uninstall the client now, or connect this computer to a different server."
            : "Unlocked. You can uninstall the client now, or connect this computer to a different server.", Look.Green);
        go.Text = "Uninstall";
        cancel.Text = "Close";
    }

    private void Say(string text, Color colour)
    {
        said.Text = text;
        said.ForeColor = colour;
    }
}

/// <summary>What the client writes to Windows' Application event log, and a nudge for the service.</summary>
internal static class Journal
{
    private const string Source = "Rabbid Lab Client";

    /// <summary>A warning in the Application log. Needs an administrator the first time, to register the source.</summary>
    public static void Write(string message)
    {
        try
        {
            if (!EventLog.SourceExists(Source))
            {
                EventLog.CreateEventSource(Source, "Application");
            }
            EventLog.WriteEntry(Source, message, EventLogEntryType.Warning, 4100);
        }
        catch (Exception e) when (e is System.Security.SecurityException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            // Not written: unlocking still worked, and the server is told at the next check-in.
        }
    }

    /// <summary>Starts the service again, which checks in as it starts.</summary>
    public static void CheckInNow()
    {
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
        catch (Exception e) when (e is InvalidOperationException or System.ServiceProcess.TimeoutException or System.ComponentModel.Win32Exception)
        {
            // Not installed as a service, or slow to stop: it checks in within a minute anyway.
        }
    }
}
