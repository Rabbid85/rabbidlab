using System;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace RabbidLab.Client;

/// <summary>
/// One program, run several ways: --service is the RabbidLabClient service, which joins the server and checks in every
/// minute; --check-in does one round in a console and says how it went, for trying it out (as an administrator);
/// --join saves a server's address as an administrator (the Connect window runs it); with nothing it's the tray icon
/// for whoever is signed in to Windows. Anything else is an error: no link or web page starts it.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0 || (args.Length == 1 && args[0] == "--tray"))
        {
            // Quiet (Group Policy only, the Rabbid Lab template): no icon at sign-in. Opened from the Start menu, it still shows, and so
            // does a window an update just closed (Tray.Kept).
            if (args.Length == 1 && Tray.Kept(take: false) is null && Settings.Read().Quiet)
            {
                return 0;
            }
            // One tray icon for each person signed in. Started with nothing (the Start menu), it opens its window too.
            using var one = new Mutex(true, @"Local\RabbidLab.Client.Tray", out var first);
            if (!first)
            {
                if (args.Length == 0)
                {
                    // The icon is there already: the Start menu shortcut has it open its window.
                    if (EventWaitHandle.TryOpenExisting(Tray.ShowSignal, out var show))
                    {
                        show.Set();
                        show.Dispose();
                    }
                    return 0;
                }
                // Started as the icon while another is there. That one may be on its way out (an update replaced it and it
                // started this one), so its place is waited for, for a few seconds.
                try
                {
                    if (!one.WaitOne(TimeSpan.FromSeconds(8)))
                    {
                        return 0;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // It went without letting go: the place is this one's.
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Tray(open: args.Length == 0));
            return 0;
        }
        if (args.Length == 1 && args[0] == "--service")
        {
            ServiceBase.Run(new ClientService());
            return 0;
        }
        if (args.Length == 3 && args[0] == "--join")
        {
            return new JoinLink { Server = args[1], Fingerprint = args[2] }.Run();
        }
        if (args.Length == 1 && args[0] == "--unlock")
        {
            // Uninstall, while it's locked: the uninstall code. The client's window starts this as an administrator.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UnlockWindow());
            return 0;
        }
        if (args.Length == 1 && args[0] == "--check-in")
        {
            try
            {
                if (!Folder.Secure())
                {
                    Console.Error.WriteLine($"{Folder.Data} was made by another account and cannot be moved aside. Delete or rename it, then try again.");
                    return 1;
                }
                var status = new Agent().Round(CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine(status.Describe());
                return status.State is Status.Connected or Status.Waiting ? 0 : 1;
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or ArgumentException or UriFormatException)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }
        }
        // Not one of the ways it runs: nothing is started.
        Console.Error.WriteLine("Usage: RabbidLab-Client [--tray | --service | --check-in | --unlock | --join <server> <fingerprint>]");
        return 2;
    }
}
