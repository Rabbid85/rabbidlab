using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>A script in the library: PowerShell or a command script, run on computers as SYSTEM.</summary>
public sealed class DeviceScript
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? About { get; set; }
    public string Language { get; set; } = DeviceScripts.PowerShell;
    public string Text { get; set; } = "";

    /// <summary>How long it may run before it's stopped.</summary>
    public int Minutes { get; set; } = DeviceScripts.UsualMinutes;

    public string? By { get; set; }
    public DateTime Changed { get; set; }

    /// <summary>One Rabbid Lab comes with (BuiltInScripts): not kept in scripts.json, and not changed or deleted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool BuiltIn { get; set; }
}

public sealed class DeviceScriptsFile
{
    public List<DeviceScript> Scripts { get; set; } = [];
}

/// <summary>The script library, kept in scripts.json in the data folder.</summary>
public sealed class DeviceScripts(DataFolder folder, TimeProvider clock)
{
    public const string PowerShell = "powershell";
    public const string Command = "cmd";
    public const int MaxScripts = 500;
    public const int MaxText = 100_000;
    public const int MaxName = 80;
    public const int UsualMinutes = 10;
    public const int MaxMinutes = 240;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<DeviceScriptsFile> file = folder.Json<DeviceScriptsFile>("scripts.json", Json);

    public IReadOnlyList<DeviceScript> All()
    {
        lock (file.Gate)
        {
            return [.. file.Read().Scripts.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase), .. BuiltInScripts.All];
        }
    }

    public DeviceScript? Find(string? id)
    {
        if (BuiltInScripts.Find(id) is { } builtIn)
        {
            return builtIn;
        }
        lock (file.Gate)
        {
            return file.Read().Scripts.FirstOrDefault(s => s.Id == id);
        }
    }

    /// <summary>Why a script can't be kept or run as it is, or null.</summary>
    public static string? Problem(string? language, string? text) =>
        language is not (PowerShell or Command) ? "Pick PowerShell or Command."
        : string.IsNullOrWhiteSpace(text) ? "The script is empty."
        : text.Length > MaxText ? $"A script is {MaxText:N0} characters at most."
        : null;

    /// <summary>A script's text the way it's run: Windows line ends, and none of the blank lines at its end.</summary>
    public static string Clean(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd().Replace("\n", "\r\n") + "\r\n";

    public static int Minutes(int? minutes) => Math.Clamp(minutes ?? UsualMinutes, 1, MaxMinutes);

    /// <summary>Adds a script, or changes the one with this id.</summary>
    public (DeviceScript? Script, string? Problem) Save(string? id, string? name, string? about, string? language, string? text, int? minutes, string by)
    {
        if (BuiltInScripts.Is(id))
        {
            return (null, "Built-in scripts can't be changed. Duplicate it to make one of your own.");
        }
        var n = name?.Trim() ?? "";
        if (n.Length is 0 or > MaxName)
        {
            return (null, n.Length == 0 ? "Give the script a name." : $"A name is {MaxName} characters at most.");
        }
        if (Problem(language, text) is { } problem)
        {
            return (null, problem);
        }
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Scripts.Any(s => s.Id != id && string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase)))
            {
                return (null, $"There's already a script called {n}.");
            }
            var script = f.Scripts.FirstOrDefault(s => s.Id == id);
            if (script is null)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    return (null, "That script isn't here any more.");
                }
                if (f.Scripts.Count >= MaxScripts)
                {
                    return (null, $"The library holds {MaxScripts} scripts at most.");
                }
                script = new DeviceScript { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant() };
                f.Scripts.Add(script);
            }
            var note = about?.Trim() ?? "";
            (script.Name, script.Language, script.Text, script.Minutes) = (n, language!, Clean(text!), Minutes(minutes));
            script.About = note.Length == 0 ? null : note.Length > 200 ? note[..200] : note;
            (script.By, script.Changed) = (by, clock.GetUtcNow().UtcDateTime);
            file.Save(f);
            return (script, null);
        }
    }

    public DeviceScript? Delete(string id)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Scripts.FirstOrDefault(s => s.Id == id) is not { } script)
            {
                return null;
            }
            f.Scripts.Remove(script);
            file.Save(f);
            return script;
        }
    }
}

/// <summary>One thing a job runs on a computer: a script's text, as it was when the job was made.</summary>
public sealed class JobStep
{
    public string Title { get; set; } = "";
    public string Language { get; set; } = DeviceScripts.PowerShell;
    public string Text { get; set; } = "";
    public int Minutes { get; set; } = DeviceScripts.UsualMinutes;
}

/// <summary>How a step went on one computer. Its output is kept in a file of its own.</summary>
public sealed class StepOutcome
{
    public int? Exit { get; set; }
    public double Seconds { get; set; }
    public bool TimedOut { get; set; }
}

/// <summary>One computer of a job.</summary>
public sealed class JobTarget
{
    public string Client { get; set; } = "";
    public string Computer { get; set; } = "";
    public string State { get; set; } = DeviceJobs.Waiting;
    public DateTime? Sent { get; set; }
    public DateTime? Finished { get; set; }
    public string? Problem { get; set; }
    public List<StepOutcome> Steps { get; set; } = [];
}

/// <summary>Something run on computers: by whom, what exactly, on which computers, and how it went on each.</summary>
public sealed class DeviceJob
{
    public string Id { get; set; } = "";
    public DateTime At { get; set; }
    public string By { get; set; } = "";
    public string Kind { get; set; } = DeviceJobs.Script;
    public string Title { get; set; } = "";

    /// <summary>The library script or the workflow it came from, when it did.</summary>
    public string? Script { get; set; }
    public string? Workflow { get; set; }

    /// <summary>A computer that hasn't taken it by then doesn't get it.</summary>
    public DateTime Expires { get; set; }

    public List<JobStep> Steps { get; set; } = [];
    public List<JobTarget> Targets { get; set; } = [];
}

/// <summary>What a client is handed: the job's id and its steps.</summary>
public sealed record TakenJob(string Id, IReadOnlyList<JobStep> Steps);

/// <summary>What a client says once it has run a job, or wouldn't.</summary>
public sealed record JobReport(string? Id, bool Refused = false, string? Problem = null, List<StepReport>? Steps = null);

public sealed record StepReport(int? Exit = null, string? Output = null, double Seconds = 0, bool TimedOut = false);

/// <summary>
/// Jobs: scripts and actions queued for computers, and how they went. A job waits for each computer until its client asks
/// (ClientCommands rings a client that's listening), is handed over once, and the client reports back with the output.
/// Kept in the data folder's Jobs folder, a file per job and one per computer's output, the newest 200.
/// </summary>
public sealed class DeviceJobs(DataFolder folder, TimeProvider clock, ClientCommands commands)
{
    public const string Script = "script";
    public const string Restart = "restart";
    public const string Updates = "updates";
    public const string Workflow = "workflow";
    public const string Printer = "printer";
    public const string Check = "check";
    public const string Lock = "lock";
    public const string SignOut = "signout";
    public const string Shutdown = "shutdown";
    public const string Message = "message";
    public const string Client = "client";
    public const string GpUpdate = "gpupdate";

    public const string Waiting = "waiting";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Refused = "refused";
    public const string NoAnswer = "noanswer";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    public const int Keep = 200;
    public const int MaxOutput = 64 * 1024;

    /// <summary>How long after a job's steps could have finished the server gives up on a computer's answer.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string dir = Path.Combine(folder.Path, "Jobs");
    private readonly Lock gate = new();
    private readonly HashSet<string> waiting = []; // the computers something waits for
    private List<DeviceJob>? jobs; // newest first

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Reads the jobs, newest first, without anything changing them meanwhile.</summary>
    public T Read<T>(Func<IReadOnlyList<DeviceJob>, T> read)
    {
        lock (gate)
        {
            return read(Loaded());
        }
    }

    /// <summary>Queues a job for these computers and rings the ones listening.</summary>
    public DeviceJob Add(string by, string kind, string title, IReadOnlyList<JobStep> steps, IEnumerable<Client> on, TimeSpan life, string? script = null, string? workflow = null)
    {
        var job = new DeviceJob
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(),
            At = Now, By = by, Kind = kind, Title = title, Script = script, Workflow = workflow, Expires = Now + life, Steps = [.. steps],
            Targets = [.. on.DistinctBy(c => c.Id).Select(c => new JobTarget { Client = c.Id, Computer = c.Computer })],
        };
        lock (gate)
        {
            var all = Loaded();
            all.Insert(0, job);
            Save(job);
            foreach (var old in all.Skip(Keep).ToList())
            {
                all.Remove(old);
                Erase(old);
            }
            Recount();
        }
        foreach (var t in job.Targets)
        {
            commands.Ring(t.Client);
        }
        return job;
    }

    /// <summary>Whether a job waits for this computer.</summary>
    public bool Waits(string client)
    {
        lock (gate)
        {
            Loaded();
            return waiting.Contains(client);
        }
    }

    /// <summary>The jobs waiting for this computer, oldest first. Each is handed over once.</summary>
    public IReadOnlyList<TakenJob> Take(string client)
    {
        lock (gate)
        {
            List<TakenJob> taken = [];
            foreach (var job in Loaded().AsEnumerable().Reverse())
            {
                if (job.Targets.FirstOrDefault(t => t.Client == client && t.State == Waiting) is not { } target)
                {
                    continue;
                }
                if (job.Expires <= Now)
                {
                    (target.State, target.Finished) = (Expired, Now);
                }
                else
                {
                    (target.State, target.Sent) = (Running, Now);
                    taken.Add(new TakenJob(job.Id, job.Steps));
                }
                Save(job);
            }
            Recount();
            return taken;
        }
    }

    /// <summary>A computer's answer for a job it was handed. False when it isn't one of its jobs.</summary>
    public bool Finish(string client, JobReport report)
    {
        lock (gate)
        {
            if (Loaded().FirstOrDefault(j => j.Id == report.Id) is not { } job || job.Targets.FirstOrDefault(t => t.Client == client && t.State == Running) is not { } target)
            {
                return false;
            }
            var steps = (report.Steps ?? []).Take(job.Steps.Count).ToList();
            target.Steps = [.. steps.Select(s => new StepOutcome { Exit = s.Exit, Seconds = Math.Max(0, s.Seconds), TimedOut = s.TimedOut })];
            target.Finished = Now;
            var bad = steps.FindIndex(s => s.TimedOut || s.Exit != 0);
            target.State = report.Refused ? Refused : bad < 0 && steps.Count == job.Steps.Count ? Done : Failed;
            target.Problem = Cut(report.Problem, 300) ?? (target.State != Failed ? null
                : bad < 0 ? "It stopped before every step had run."
                : steps[bad].TimedOut ? $"{job.Steps[bad].Title} was stopped after {job.Steps[bad].Minutes} minutes."
                : $"{job.Steps[bad].Title} ended with exit code {steps[bad].Exit?.ToString() ?? "unknown"}.");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(OutputFile(job.Id, client), JsonSerializer.SerializeToUtf8Bytes(steps.Select(s => Cut(s.Output, MaxOutput) ?? "").ToList(), Json));
            Save(job);
            return true;
        }
    }

    /// <summary>What each step wrote on one computer, or null when it has sent nothing.</summary>
    public IReadOnlyList<string>? Output(string job, string client)
    {
        if (!Plain(job) || !Plain(client))
        {
            return null;
        }
        try
        {
            var path = OutputFile(job, client);
            return File.Exists(path) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllBytes(path), Json) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Takes a job back from the computers that haven't taken it yet: how many.</summary>
    public int Cancel(string id) => Close(t => true, j => j.Id == id, Cancelled, null);

    /// <summary>A computer was removed from Devices: nothing waits for it any more.</summary>
    public void Forget(string client) => Close(t => t.Client == client, j => true, Cancelled, "The computer was removed from Devices.");

    /// <summary>Gives up on what's overdue: jobs nobody took in time, and computers that took one and never answered.</summary>
    public void Sweep()
    {
        lock (gate)
        {
            var now = Now;
            foreach (var job in Loaded())
            {
                var changed = false;
                var longest = TimeSpan.FromMinutes(job.Steps.Sum(s => s.Minutes)) + Grace;
                foreach (var t in job.Targets)
                {
                    if (t.State == Waiting && job.Expires <= now)
                    {
                        (t.State, t.Finished, changed) = (Expired, now, true);
                    }
                    else if (t.State == Running && t.Sent + longest <= now)
                    {
                        (t.State, t.Finished, t.Problem, changed) = (NoAnswer, now, "The computer took the job and never answered. It may have restarted or lost the server.", true);
                    }
                }
                if (changed)
                {
                    Save(job);
                }
            }
            Recount();
        }
    }

    private int Close(Func<JobTarget, bool> which, Func<DeviceJob, bool> where, string state, string? problem)
    {
        lock (gate)
        {
            var closed = 0;
            foreach (var job in Loaded().Where(where))
            {
                var mine = job.Targets.Where(t => t.State == Waiting && which(t)).ToList();
                foreach (var t in mine)
                {
                    (t.State, t.Finished, t.Problem) = (state, Now, problem);
                }
                if (mine.Count > 0)
                {
                    closed += mine.Count;
                    Save(job);
                }
            }
            Recount();
            return closed;
        }
    }

    private List<DeviceJob> Loaded()
    {
        if (jobs is not null)
        {
            return jobs;
        }
        List<DeviceJob> read = [];
        if (Directory.Exists(dir))
        {
            foreach (var path in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    if (JsonSerializer.Deserialize<DeviceJob>(File.ReadAllBytes(path), Json) is { Id.Length: > 0 } job)
                    {
                        read.Add(job);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
                {
                    // A job that can't be read isn't listed.
                }
            }
        }
        jobs = [.. read.OrderByDescending(j => j.At)];
        Recount();
        return jobs;
    }

    private void Recount()
    {
        waiting.Clear();
        var now = Now;
        foreach (var job in jobs ?? [])
        {
            if (job.Expires > now)
            {
                waiting.UnionWith(job.Targets.Where(t => t.State == Waiting).Select(t => t.Client));
            }
        }
    }

    private void Save(DeviceJob job)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, job.Id + ".json");
        File.WriteAllBytes(path + ".tmp", JsonSerializer.SerializeToUtf8Bytes(job, Json));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private void Erase(DeviceJob job)
    {
        try
        {
            File.Delete(Path.Combine(dir, job.Id + ".json"));
            foreach (var output in Directory.GetFiles(dir, job.Id + "-*.out"))
            {
                File.Delete(output);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left behind; nothing lists it.
        }
    }

    // Ids are the server's own (hex), so they're safe in a file name; anything else gets no file.
    private static bool Plain(string id) => id.Length is > 0 and <= 64 && id.All(char.IsAsciiLetterOrDigit);

    private string OutputFile(string job, string client) => Path.Combine(dir, $"{job}-{client}.out");

    // What a client sends is its own computer's words: the middle of a long output goes, its start and end stay.
    private static string? Cut(string? text, int most) => string.IsNullOrEmpty(text) ? null
        : text.Length <= most ? text
        : string.Concat(text.AsSpan(0, most / 4), "\r\n\r\n[... the middle was cut ...]\r\n\r\n", text.AsSpan(text.Length - (most - most / 4)));
}

/// <summary>The actions Rabbid Lab has built in. Each is a script the client runs like any other, so a client needs nothing new for one.</summary>
public static class DeviceActions
{
    private const string Warning = "Your administrator is restarting this computer in 5 minutes. Save your work.";

    public static JobStep Restart() => new()
    {
        Title = "Restart", Language = DeviceScripts.Command, Minutes = 2,
        Text = DeviceScripts.Clean($"""
            @echo off
            shutdown.exe /r /t 300 /c "{Warning}" /d p:0:0
            if errorlevel 1 exit /b %errorlevel%
            echo The computer restarts in 5 minutes.
            """),
    };

    public static JobStep RestartIfNeeded() => new()
    {
        Title = "Restart if Windows needs it", Language = DeviceScripts.PowerShell, Minutes = 2,
        Text = DeviceScripts.Clean($$"""
            $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired',
                'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending'
            if (-not ($keys | Where-Object { Test-Path $_ })) {
                'Windows is not waiting for a restart.'
                exit 0
            }
            shutdown.exe /r /t 300 /c "{{Warning}}" /d p:2:17
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            'The computer restarts in 5 minutes.'
            """),
    };

    // The sessions people are signed in to, by their Explorer (one per desktop, at the screen or over Remote Desktop),
    // and Windows' own tool from System32 even when PowerShell runs 32-bit.
    private const string Sessions = """
        $ids = @(Get-Process -Name explorer -ErrorAction SilentlyContinue | Select-Object -ExpandProperty SessionId -Unique)
        if (-not $ids) { 'Nobody is signed in.'; exit 0 }
        function Tool($name) { @("$env:windir\Sysnative\$name", "$env:windir\System32\$name") | Where-Object { Test-Path $_ } | Select-Object -First 1 }
        """;

    private const string ShutdownWarning = "Your administrator is shutting down this computer in 5 minutes. Save your work.";

    /// <summary>Everyone signed in goes back to the sign-in screen: their session is disconnected, so their apps keep running.</summary>
    public static JobStep Lock() => new()
    {
        Title = "Lock", Language = DeviceScripts.PowerShell, Minutes = 2,
        Text = DeviceScripts.Clean($$"""
            {{Sessions}}
            $tool = Tool 'tsdiscon.exe'
            if (-not $tool) { 'This edition of Windows has no tsdiscon.exe to lock with.'; exit 1 }
            $locked = 0
            foreach ($id in $ids) { & $tool $id 2>&1 | ForEach-Object { "Session ${id}: $_" }; if ($LASTEXITCODE -eq 0) { $locked++ } }
            if ($locked -eq 0) { 'No session could be locked.'; exit 1 }
            "Locked $locked session(s). Their apps keep running, and getting back in takes their password."
            """),
    };

    /// <summary>Everyone signed in is signed out now; what they hadn't saved is lost.</summary>
    public static JobStep SignOut() => new()
    {
        Title = "Sign everyone out", Language = DeviceScripts.PowerShell, Minutes = 2,
        Text = DeviceScripts.Clean($$"""
            {{Sessions}}
            $tool = Tool 'logoff.exe'
            if (-not $tool) { 'This edition of Windows has no logoff.exe.'; exit 1 }
            $out = 0
            foreach ($id in $ids) { & $tool $id 2>&1 | ForEach-Object { "Session ${id}: $_" }; if ($LASTEXITCODE -eq 0) { $out++ } }
            if ($out -eq 0) { 'Nobody could be signed out.'; exit 1 }
            "Signed out $out session(s)."
            """),
    };

    public static JobStep Shutdown() => new()
    {
        Title = "Shut down", Language = DeviceScripts.Command, Minutes = 2,
        Text = DeviceScripts.Clean($"""
            @echo off
            shutdown.exe /s /t 300 /c "{ShutdownWarning}" /d p:0:0
            if errorlevel 1 exit /b %errorlevel%
            echo The computer shuts down in 5 minutes.
            """),
    };

    /// <summary>
    /// The client installs this server's version now, not at its next try: the once-a-day wait after an install that
    /// didn't take is cleared and the client restarts, and on its first round it fetches and installs the server's
    /// version. The restart starts a few seconds after this step reports back, from outside it, so stopping the service
    /// doesn't stop the restart. Any client that takes jobs can do it.
    /// </summary>
    public static JobStep UpdateClient() => new()
    {
        Title = "Update the client", Language = DeviceScripts.PowerShell, Minutes = 2,
        Text = DeviceScripts.Clean($$"""
            $data = Join-Path $env:ProgramData 'Rabbid Lab Client'
            Remove-Item -LiteralPath (Join-Path $data 'last-update.txt') -Force -ErrorAction SilentlyContinue
            $restart = 'timeout /t 8 /nobreak >nul & net stop {{ClientServiceName}} & net start {{ClientServiceName}}'
            Start-Process -FilePath "$env:windir\System32\cmd.exe" -ArgumentList '/c', $restart -WindowStyle Hidden
            'The client restarts in a few seconds and installs the version this server has.'
            """),
    };

    private const string ClientServiceName = "RabbidLabClient";

    /// <summary>
    /// Group Policy applied again now: the computer's here, as SYSTEM, and each signed-in person's through a scheduled task
    /// that runs in their session, as Invoke-GPUpdate does it. The client reads the results again after any job.
    /// </summary>
    public static JobStep GpUpdate() => new()
    {
        Title = "Run gpupdate", Language = DeviceScripts.PowerShell, Minutes = 10,
        Text = DeviceScripts.Clean("""
            $tool = Join-Path $env:windir 'Sysnative\gpupdate.exe'
            if (-not (Test-Path $tool)) { $tool = Join-Path $env:windir 'System32\gpupdate.exe' }
            & $tool /target:computer /force /wait:300 2>&1 | Where-Object { $_ -and "$_".Trim() } | ForEach-Object { "$_".Trim() }
            $code = $LASTEXITCODE
            if (Get-Process explorer -ErrorAction SilentlyContinue) {
                $name = 'Rabbid Lab gpupdate'
                $action = New-ScheduledTaskAction -Execute $tool -Argument '/target:user /force'
                $who = New-ScheduledTaskPrincipal -GroupId 'S-1-5-4' -RunLevel Limited
                Register-ScheduledTask -TaskName $name -Action $action -Principal $who -Force | Out-Null
                Start-ScheduledTask -TaskName $name
                Start-Sleep -Seconds 45
                Unregister-ScheduledTask -TaskName $name -Confirm:$false
                'User policy refreshed for everyone signed in.'
            } else { 'Nobody is signed in, so only computer policy was refreshed.' }
            exit $code
            """),
    };

    public const int MessageMost = 250;

    /// <summary>Why it can't be the message, or null when it can.</summary>
    public static string? MessageProblem(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "Type the message." : text.Trim().Length > MessageMost ? $"Keep the message to {MessageMost} characters." : null;

    /// <summary>A message that pops up for everyone signed in (msg.exe), until they close it or an hour passes.</summary>
    public static JobStep Message(string text)
    {
        // One line, with no double quotes (Windows PowerShell passes them to msg.exe broken), as a single-quoted string.
        var line = string.Join(' ', text.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Replace('"', '\'');
        return new()
        {
            Title = "Send a message", Language = DeviceScripts.PowerShell, Minutes = 2,
            Text = DeviceScripts.Clean($$"""
                {{Sessions}}
                $tool = Tool 'msg.exe'
                if (-not $tool) { 'This edition of Windows has no msg.exe.'; exit 1 }
                & $tool * /TIME:3600 '{{line.Replace("'", "''")}}' 2>&1
                if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                "Shown to $($ids.Count) session(s)."
                """),
        };
    }

    /// <summary>
    /// The start of both update scripts: what isn't installed, from everywhere Windows can be asked, the way its own
    /// Windows Update page puts things together. The old Windows Update interface asked through one service gave a server
    /// one update where its page showed five, and asked through every registered service still missed three. So it asks
    /// the usual service, each registered one, Microsoft Update by its id, and Windows Server's own scan (the one sconfig
    /// and Windows Admin Center use), and says at the end what each answered. It leaves $session, $missing (what Windows
    /// would put in itself) and $apart (offered, not counted) from the old interface, $more and $moreApart from Windows'
    /// own scan alone, Kind for why one is apart, and $said. The client asks the same way for the inventory (Inventory.cs).
    /// </summary>
    private const string FindUpdates = """
        $ErrorActionPreference = 'Stop'
        $session = New-Object -ComObject Microsoft.Update.Session
        $session.ClientApplicationID = 'Rabbid Lab'
        $services = @((New-Object -ComObject Microsoft.Update.ServiceManager).Services)
        $seen = @{}
        $all = New-Object System.Collections.ArrayList
        $said = New-Object System.Collections.ArrayList
        $script:answered = 0
        # One source asked: each update counts once, however many sources offer it.
        function Ask($name, $id) {
            try {
                $searcher = $session.CreateUpdateSearcher()
                if ($id) { $searcher.ServerSelection = 3; $searcher.ServiceID = $id }
                $found = $searcher.Search('IsInstalled=0 and IsHidden=0').Updates
                foreach ($u in $found) {
                    $key = $u.Identity.UpdateID
                    if (-not $seen.ContainsKey($key)) { $seen[$key] = $true; [void]$all.Add($u) }
                }
                [void]$said.Add("$name $($found.Count)")
                $script:answered++
            } catch {
                $e = $_.Exception
                while ($e.InnerException) { $e = $e.InnerException }
                [void]$said.Add("$name didn't answer (0x$('{0:X8}' -f $e.HResult))")
            }
        }
        Ask 'the usual service' $null
        foreach ($s in $services) { Ask $s.Name $s.ServiceID }
        $mu = '7971f918-a847-4430-9279-4a52d1efe18d'
        if (-not ($services | Where-Object { $_.ServiceID -eq $mu })) { Ask 'Microsoft Update' $mu }
        # Windows Server's own scan, where the computer has it. What it lists that the others didn't is kept apart: it
        # comes without the details the others give.
        $own = @()
        try {
            $scan = Invoke-CimMethod -Namespace root/Microsoft/Windows/WindowsUpdate -ClassName MSFT_WUOperations -MethodName ScanForUpdates -Arguments @{ SearchCriteria = 'IsInstalled=0' } -ErrorAction Stop
            $own = @($scan.Updates | Where-Object { $_.Title })
            [void]$said.Add("Windows' own scan $($own.Count)")
            $script:answered++
        } catch {
            if ($_.Exception.Message -notmatch 'Invalid namespace|Invalid class') { [void]$said.Add("Windows' own scan didn't answer") }
        }
        if ($script:answered -eq 0) { throw "Windows Update didn't answer: $($said -join ', ')" }
        # Missing is what Windows would put in for you. Antivirus definitions come several times a day, optional drivers
        # are the ones under Optional updates, and a new version of Windows is its own decision: apart, not counted.
        function Kind($u) {
            $cats = @($u.Categories | ForEach-Object { $_.CategoryID })
            if ($cats -contains 'e0789628-ce08-4437-be74-2495b842f43b') { return 'antivirus definitions' }
            if ($cats -contains '3689bdc8-b205-4af4-8d4a-a63924c5e9d5') { return 'a new version of Windows' }
            if ($u.Type -eq 2 -and -not $u.AutoSelectOnWebSites) { return 'optional driver' }
            return ''
        }
        $missing = @($all | Where-Object { (Kind $_) -eq '' })
        $apart = @($all | Where-Object { (Kind $_) -ne '' })
        $titles = @{}
        foreach ($u in $all) { $titles[$u.Title] = $true }
        $only = @($own | Where-Object { -not $seen.ContainsKey([string]$_.UpdateID) -and -not $titles.ContainsKey($_.Title) })
        $more = @($only | Where-Object { $_.Title -notmatch 'Security Intelligence Update|Definition Update' })
        $moreApart = @($only | Where-Object { $_.Title -match 'Security Intelligence Update|Definition Update' })

        """;

    /// <summary>Asks Windows Update what's missing now and says so. After any job the client asks again and sends its inventory, so Devices shows it.</summary>
    public static JobStep CheckUpdates() => new()
    {
        Title = "Check for updates", Language = DeviceScripts.PowerShell, Minutes = 15,
        Text = DeviceScripts.Clean(FindUpdates + """
            $count = $missing.Count + $more.Count
            if ($count -eq 0) { 'Nothing is missing.' } else { "$count update(s) missing:"; foreach ($u in @($missing) + @($more)) { "  $($u.Title)" } }
            if ($apart.Count + $moreApart.Count) {
                'Also offered, and not counted:'
                foreach ($u in $apart) { "  $($u.Title) ($(Kind $u))" }
                foreach ($u in $moreApart) { "  $($u.Title) (antivirus definitions)" }
            }
            "Asked: $($said -join ', ')."
            """),
    };

    /// <summary>
    /// Every missing update Windows offers, from any of its update services, downloaded and installed, and antivirus
    /// definitions with them. Not optional drivers or a new version of Windows. It doesn't restart the computer.
    /// </summary>
    public static JobStep InstallUpdates() => new()
    {
        Title = "Install Windows updates", Language = DeviceScripts.PowerShell, Minutes = 180,
        Text = DeviceScripts.Clean(FindUpdates + """
            $found = @($missing) + @($apart | Where-Object { (Kind $_) -eq 'antivirus definitions' })
            $wanted = New-Object -ComObject Microsoft.Update.UpdateColl
            foreach ($u in $found) {
                if ($u.InstallationBehavior.CanRequestUserInput) { "Skipped, it asks questions: $($u.Title)"; continue }
                if (-not $u.EulaAccepted) { $u.AcceptEula() }
                [void]$wanted.Add($u)
            }
            # What only Windows' own scan lists goes in through that scan's own installer, after the rest.
            $rest = @($more) + @($moreApart)
            if ($wanted.Count -eq 0 -and $rest.Count -eq 0) { 'Nothing is missing.'; exit 0 }
            $failed = 0
            $restart = $false
            if ($wanted.Count) {
                "Downloading $($wanted.Count) update(s)."
                $down = $session.CreateUpdateDownloader()
                $down.Updates = $wanted
                [void]$down.Download()
                $ready = New-Object -ComObject Microsoft.Update.UpdateColl
                foreach ($u in $wanted) { if ($u.IsDownloaded) { [void]$ready.Add($u) } else { $failed++; "Not downloaded: $($u.Title)" } }
                if ($ready.Count) {
                    "Installing $($ready.Count) update(s)."
                    $installer = $session.CreateUpdateInstaller()
                    $installer.Updates = $ready
                    $result = $installer.Install()
                    for ($i = 0; $i -lt $ready.Count; $i++) {
                        $one = $result.GetUpdateResult($i)
                        if ($one.ResultCode -eq 2 -or $one.ResultCode -eq 3) { "Installed: $($ready.Item($i).Title)" }
                        else { $failed++; "Failed (0x$('{0:X8}' -f $one.HResult)): $($ready.Item($i).Title)" }
                    }
                    if ($result.RebootRequired) { $restart = $true }
                }
            }
            if ($rest.Count) {
                "Installing $($rest.Count) more through Windows' own scan."
                try {
                    $done = Invoke-CimMethod -Namespace root/Microsoft/Windows/WindowsUpdate -ClassName MSFT_WUOperations -MethodName InstallUpdates -Arguments @{ Updates = [ciminstance[]]$rest } -ErrorAction Stop
                    foreach ($u in $rest) { "Installed: $($u.Title)" }
                    if ($done.RebootRequired) { $restart = $true }
                } catch {
                    $failed += $rest.Count
                    "Failed: $($_.Exception.Message)"
                    foreach ($u in $rest) { "  $($u.Title)" }
                }
            }
            if ($restart) { 'Windows needs a restart to finish.' }
            "Asked: $($said -join ', ')."
            if ($failed) { exit 1 }
            """),
    };

    /// <summary>The SHA-256 of a job's steps, for the activity log: what ran, to the letter.</summary>
    public static string Fingerprint(IEnumerable<JobStep> steps) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n---\n", steps.Select(s => $"{s.Language}\n{s.Text}"))))).ToLowerInvariant();
}
