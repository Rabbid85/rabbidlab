using System;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace RabbidLab.Client;

/// <summary>
/// One program, run several ways: --service is the RabbidLabClient service, which joins the server and checks in every
/// minute; --check-in does one round in a console and says how it went, for trying it out (as an administrator); a
/// rabbidlab://join link (from the server's page) opens Connect this computer, which saves with --join as an
/// administrator; with nothing it's the tray icon for whoever is signed in to Windows. Anything else is an error.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0 || (args.Length == 1 && args[0] == "--tray"))
        {
            // One tray icon for each person signed in. Started with nothing (the Start menu), it opens its window too.
            using var one = new Mutex(true, @"Local\RabbidLab.Client.Tray", out var first);
            if (!first)
            {
                // The icon is there already: the Start menu shortcut has it open its window.
                if (args.Length == 0 && EventWaitHandle.TryOpenExisting(Tray.ShowSignal, out var show))
                {
                    show.Set();
                    show.Dispose();
                }
                return 0;
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
            return new JoinLink { Server = args[1], Fingerprint = args[2] }.Save();
        }
        if (args.Length == 1 && args[0].StartsWith("rabbidlab:", StringComparison.OrdinalIgnoreCase))
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ConnectWindow(JoinLink.Parse(args[0])));
            return 0;
        }
        if (args.Length == 1 && args[0] == "--check-in")
        {
            try
            {
                Folder.Secure();
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
        Console.Error.WriteLine("Usage: RabbidLab-Client [--tray | --service | --check-in | --join <server> <fingerprint> | rabbidlab://join?server=...]");
        return 2;
    }
}
