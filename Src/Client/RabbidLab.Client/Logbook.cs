using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace RabbidLab.Client;

/// <summary>One thing the client did on this computer, for the window's Activity: never a script's or an admin's name.</summary>
[DataContract]
internal sealed class Act
{
    /// <summary>check, monitor, download, code, shield, alert, clock, message, plug.</summary>
    [DataMember(Name = "icon")] public string Icon { get; set; } = "check";

    /// <summary>ok, warn or bad: the colour of its icon.</summary>
    [DataMember(Name = "tone")] public string Tone { get; set; } = "ok";
    [DataMember(Name = "title")] public string Title { get; set; } = "";
    [DataMember(Name = "detail")] public string Detail { get; set; } = "";
    [DataMember(Name = "at")] public DateTime At { get; set; }
}

/// <summary>The lab's own look, from the server (Settings, Appearance): its name, accent, and its icon's hash.</summary>
[DataContract]
internal sealed class LabLook
{
    [DataMember(Name = "name")] public string? Name { get; set; }
    [DataMember(Name = "accent")] public string? Accent { get; set; }
    [DataMember(Name = "icon")] public string? Icon { get; set; }
}

/// <summary>Ask IT for help, from the server (Settings, Help requests): on or off, how to reach IT, pictures of the screens.</summary>
[DataContract]
internal sealed class HelpLook
{
    [DataMember(Name = "on")] public bool On { get; set; }
    [DataMember(Name = "text")] public string? Text { get; set; }

    /// <summary>all (every screen, any can be left out), ask (only when ticked), never.</summary>
    [DataMember(Name = "pictures")] public string? Pictures { get; set; }
}

/// <summary>
/// What the client remembers for its window, in logbook.json (only the service writes it): what it did lately, every
/// check-in of the last day and every round that couldn't reach the server, today's jobs and problems, the lab's look,
/// Ask IT for help, and this computer's make, model and serial number from its inventory. Copied into status.json each
/// time that's written, for the tray icon to show.
/// </summary>
[DataContract]
internal sealed class Logbook
{
    public const int MostActs = 40;
    private static readonly object Gate = new();
    private static Logbook? loaded;

    [DataMember(Name = "acts")] public List<Act> Acts { get; set; } = new();

    /// <summary>Minutes (since 1 January 2000) with a check-in, and with a round that couldn't reach the server, in the last day.</summary>
    [DataMember(Name = "ok")] public List<int> Ok { get; set; } = new();
    [DataMember(Name = "failed")] public List<int> Failed { get; set; } = new();

    [DataMember(Name = "state")] public string? State { get; set; }
    [DataMember(Name = "version")] public string? Version { get; set; }
    [DataMember(Name = "waitingSince")] public DateTime? WaitingSince { get; set; }
    [DataMember(Name = "lostSince")] public DateTime? LostSince { get; set; }
    [DataMember(Name = "lab")] public LabLook? Lab { get; set; }
    [DataMember(Name = "help")] public HelpLook? Help { get; set; }
    [DataMember(Name = "maker")] public string? Maker { get; set; }
    [DataMember(Name = "model")] public string? Model { get; set; }
    [DataMember(Name = "serial")] public string? Serial { get; set; }

    /// <summary>The hash of the icon kept in lab-icon.png, set once it's saved; null when there is none.</summary>
    [DataMember(Name = "iconHash")] public string? IconHash { get; set; }

    private static string Path => Folder.File("logbook.json");

    /// <summary>Where the lab's own icon is kept, for the tray icon to read.</summary>
    public static string IconFile => Folder.File("lab-icon.png");

    private static readonly DateTime Epoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static int Minute(DateTime utc) => (int) ((utc - Epoch).TotalMinutes);

    // Changed under the lock, then written.
    private static void Change(Action<Logbook> what)
    {
        try
        {
            lock (Gate)
            {
                var j = Load();
                what(j);
                j.Write();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SerializationException)
        {
            // Kept in memory; written next time.
        }
    }

    private static Logbook Load()
    {
        if (loaded is not null)
        {
            return loaded;
        }
        try
        {
            using var file = File.OpenRead(Path);
            loaded = (Logbook?) new DataContractJsonSerializer(typeof(Logbook)).ReadObject(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SerializationException)
        {
            // None yet, or unreadable: started again.
        }
        loaded ??= new Logbook();
        loaded.Acts ??= new List<Act>();
        loaded.Ok ??= new List<int>();
        loaded.Failed ??= new List<int>();
        return loaded;
    }

    private void Write()
    {
        var since = Minute(DateTime.UtcNow) - 24 * 60;
        Ok.RemoveAll(m => m < since);
        Failed.RemoveAll(m => m < since);
        if (Acts.Count > MostActs)
        {
            Acts.RemoveRange(MostActs, Acts.Count - MostActs);
        }
        var temp = Path + ".tmp";
        using (var file = File.Create(temp))
        {
            new DataContractJsonSerializer(typeof(Logbook)).WriteObject(file, this);
        }
        if (File.Exists(Path))
        {
            File.Replace(temp, Path, null);
        }
        else
        {
            File.Move(temp, Path);
        }
    }

    /// <summary>Something it did, newest first.</summary>
    public static void Add(string icon, string tone, string title, string detail) =>
        Change(j => j.Acts.Insert(0, new Act { Icon = icon, Tone = tone, Title = title, Detail = detail, At = DateTime.UtcNow }));

    /// <summary>The service started: a new version since last time was an update (or the first install).</summary>
    public static void Started(string version) => Change(j =>
    {
        if (j.Version == version)
        {
            return;
        }
        j.Acts.Insert(0, j.Version is null
            ? new Act { Icon = "download", Tone = "ok", Title = "Installed the client", Detail = $"Version {version}.", At = DateTime.UtcNow }
            : new Act { Icon = "download", Tone = "ok", Title = $"Updated the client to {version}", Detail = $"From {j.Version}, by itself.", At = DateTime.UtcNow });
        j.Version = version;
    });

    /// <summary>A round with the server: it checked in, or it couldn't reach it.</summary>
    public static void Round(bool reached) => Change(j =>
    {
        var m = Minute(DateTime.UtcNow);
        var list = reached ? j.Ok : j.Failed;
        if (list.Count == 0 || list[list.Count - 1] != m)
        {
            list.Add(m);
        }
    });

    /// <summary>A job from Rabbid Lab ran here: said without what it was.</summary>
    public static void Job(bool ok, double seconds) => Change(j =>
    {
        var took = seconds < 1.5 ? "in a second" : seconds < 90 ? $"in {Math.Round(seconds):0} seconds" : $"in {Math.Round(seconds / 60):0} minutes";
        j.Acts.Insert(0, ok
            ? new Act { Icon = "code", Tone = "ok", Title = "Ran a job", Detail = $"Sent from Rabbid Lab. Finished {took}.", At = DateTime.UtcNow }
            : new Act { Icon = "code", Tone = "warn", Title = "A job didn't finish", Detail = "Sent from Rabbid Lab. It stopped with an error.", At = DateTime.UtcNow });
    });

    /// <summary>The lab's look and Ask IT for help, as the server said at the last check-in.</summary>
    public static void From(LabLook? lab, HelpLook? help) => Change(j =>
    {
        j.Lab = lab;
        j.Help = help;
    });

    /// <summary>This computer's make, model and serial number, from its inventory.</summary>
    public static void Hardware(string? maker, string? model, string? serial) => Change(j => (j.Maker, j.Model, j.Serial) = (maker, model, serial));

    /// <summary>The icon kept in lab-icon.png now (null: none).</summary>
    public static void IconSaved(string? hash) => Change(j => j.IconHash = hash);

    /// <summary>Whether the icon the server has is the one kept here.</summary>
    public static bool HasIcon(string? hash)
    {
        lock (Gate)
        {
            return Load().IconHash == hash; // an SVG one is fetched once and not kept (the window draws its own mark)
        }
    }

    /// <summary>
    /// The state as this round left it: what changed is written down (asked to join, approved, lost touch, back in touch),
    /// then everything the window shows goes into the status.
    /// </summary>
    public static void Fill(Status s)
    {
        Change(j =>
        {
            var was = j.State;
            var now = s.State;
            if (was != now && now is Status.Waiting or Status.Connected or Status.Unreachable or Status.NoServer)
            {
                var lab = j.Lab?.Name ?? "Rabbid Lab";
                if (now == Status.Waiting && was is null or Status.NoServer or Status.Removed or Status.Starting)
                {
                    j.WaitingSince = DateTime.UtcNow;
                    j.Acts.Insert(0, new Act { Icon = "clock", Tone = "warn", Title = $"Asked to join {lab}", Detail = "An admin needs to approve it.", At = DateTime.UtcNow });
                }
                else if (now == Status.Connected && was == Status.Waiting)
                {
                    j.Acts.Insert(0, new Act { Icon = "check", Tone = "ok", Title = "Approved", Detail = $"An admin let it into {lab}.", At = DateTime.UtcNow });
                }
                else if (now == Status.Unreachable && was is Status.Connected or Status.Waiting)
                {
                    j.LostSince = DateTime.UtcNow;
                    j.Acts.Insert(0, new Act { Icon = "alert", Tone = "bad", Title = "Lost touch with the server", Detail = "Check that this computer is on the network and the server is running.", At = DateTime.UtcNow });
                }
                else if (now == Status.Connected && was == Status.Unreachable)
                {
                    j.Acts.Insert(0, new Act { Icon = "check", Tone = "ok", Title = "Back in touch", Detail = j.LostSince is { } lost ? $"After {Gone(DateTime.UtcNow - lost)}." : "It checks in every minute again.", At = DateTime.UtcNow });
                }
                j.State = now;
            }
            s.Acts = j.Acts.Take(12).ToList();
            s.Day = j.HalfHours();
            s.OkDay = j.Ok.Count;
            s.FailedDay = j.Failed.Count;
            s.LastOk = j.Ok.Count > 0 ? Epoch.AddMinutes(j.Ok[j.Ok.Count - 1]) : null;
            s.WaitingSince = now == Status.Waiting ? j.WaitingSince : null;
            s.Lab = j.Lab;
            s.Help = j.Help;
            (s.Maker, s.Model, s.Serial) = (j.Maker, j.Model, j.Serial);
        });
    }

    // The last day in 48 half hours, oldest first: 2 every check-in came, 1 some were missed, 0 none came while it tried,
    // -1 it wasn't on (asleep, switched off, or not installed yet).
    private List<int> HalfHours()
    {
        var end = Minute(DateTime.UtcNow) + 1;
        var day = new List<int>(48);
        for (var i = 0; i < 48; i++)
        {
            var from = end - (48 - i) * 30;
            var to = from + 30;
            var ok = Ok.Count(m => m >= from && m < to);
            var failed = Failed.Count(m => m >= from && m < to);
            day.Add(ok > 0 && failed == 0 ? 2 : ok > 0 ? 1 : failed > 0 ? 0 : -1);
        }
        return day;
    }

    private static string Gone(TimeSpan t) => t.TotalMinutes < 90 ? $"{Math.Max(1, (int) t.TotalMinutes)} minutes" : t.TotalHours < 48 ? $"{(int) t.TotalHours} hours" : $"{(int) t.TotalDays} days";
}
