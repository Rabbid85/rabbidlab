using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RabbidLab.Client;

[DataContract]
internal sealed class JobsAnswer
{
    [DataMember(Name = "jobs")] public List<TakenJob>? Jobs { get; set; }
}

[DataContract]
internal sealed class TakenJob
{
    [DataMember(Name = "id")] public string Id { get; set; } = "";
    [DataMember(Name = "steps")] public List<TakenStep>? Steps { get; set; }
}

[DataContract]
internal sealed class TakenStep
{
    /// <summary>"powershell" or "cmd".</summary>
    [DataMember(Name = "language")] public string? Language { get; set; }
    [DataMember(Name = "text")] public string? Text { get; set; }
    [DataMember(Name = "minutes")] public int Minutes { get; set; }
}

[DataContract]
internal sealed class JobReport
{
    [DataMember(Name = "id")] public string Id { get; set; } = "";
    [DataMember(Name = "refused")] public bool Refused { get; set; }
    [DataMember(Name = "problem", EmitDefaultValue = false)] public string? Problem { get; set; }
    [DataMember(Name = "steps")] public List<StepReport> Steps { get; set; } = new();
}

[DataContract]
internal sealed class StepReport
{
    /// <summary>The exit code; null when it never started or was stopped.</summary>
    [DataMember(Name = "exit")] public int? Exit { get; set; }
    [DataMember(Name = "output")] public string Output { get; set; } = "";
    [DataMember(Name = "seconds")] public double Seconds { get; set; }
    [DataMember(Name = "timedOut")] public bool TimedOut { get; set; }
}

/// <summary>
/// Jobs from the server's Devices page: scripts and actions, run here as SYSTEM. The server says when some wait
/// (Orders.cs), they're taken in one go and run one after another on a thread of their own, each step from a file only
/// SYSTEM and administrators can open, stopped when its time is up. What each step wrote goes back to the server.
/// Group Policy can turn all of it off (Allow scripts and actions).
/// </summary>
internal static class Jobs
{
    private const int HeadChars = 32 * 1024;
    private const int TailChars = 96 * 1024;
    private static readonly object gate = new();
    private static readonly Queue<TakenJob> queue = new();
    private static bool working;

    /// <summary>Scripts left by a crash or a power cut, deleted when the service starts: no job is running then.</summary>
    public static void Sweep()
    {
        try
        {
            foreach (var left in Directory.GetFiles(Folder.Jobs))
            {
                try
                {
                    File.Delete(left);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Still held: deleted at the next start.
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // No folder to look in.
        }
    }

    /// <summary>A job is running or waiting its turn, so the client doesn't update itself underneath it.</summary>
    public static bool Busy
    {
        get
        {
            lock (gate)
            {
                return working;
            }
        }
    }

    /// <summary>Takes what waits for this computer and starts on it. False when the server couldn't be asked.</summary>
    public static async Task<bool> Take(ServerApi api, string key, CancellationToken stop)
    {
        var (_, answer) = await api.Post<JobsAnswer>("api/clients/jobs/take", new Nothing(), key, stop).ConfigureAwait(false);
        if (answer?.Jobs is null)
        {
            return false;
        }
        lock (gate)
        {
            foreach (var job in answer.Jobs)
            {
                queue.Enqueue(job);
            }
            if (working || queue.Count == 0)
            {
                return true;
            }
            working = true;
        }
        _ = Task.Run(() => Work(stop));
        return true;
    }

    private static async Task Work(CancellationToken stop)
    {
        while (true)
        {
            TakenJob job;
            lock (gate)
            {
                if (queue.Count == 0 || stop.IsCancellationRequested)
                {
                    working = false;
                    return;
                }
                job = queue.Dequeue();
            }
            JobReport report;
            var took = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                report = Run(job, stop);
            }
            catch (Exception e)
            {
                report = new JobReport { Id = job.Id, Problem = $"The client on {Machine.Name} could not run the job: {e.Message.Trim().TrimEnd('.')}." };
            }
            // The window's Activity and today's count: that a job ran and how it went, never what it was.
            Logbook.Job(report.Problem is null && !report.Refused && report.Steps.TrueForAll(s => s.Exit is null or 0), took.Elapsed.TotalSeconds);
            // What it changed shows on the Devices page within a minute or two, not at the next half hour.
            Inventory.Soon();
            await Report(report, stop).ConfigureAwait(false);
        }
    }

    private static JobReport Run(TakenJob job, CancellationToken stop)
    {
        var report = new JobReport { Id = job.Id };
        if (!Settings.Read().Scripts)
        {
            report.Refused = true;
            report.Problem = $"Scripts and actions are disabled by Group Policy on {Machine.Name}.";
            return report;
        }
        var n = 0;
        foreach (var step in job.Steps ?? new List<TakenStep>())
        {
            var result = RunStep($"{job.Id}-{++n}", step, stop);
            report.Steps.Add(result);
            // The steps are in order: the next one doesn't run after one that failed.
            if (result.TimedOut || result.Exit != 0)
            {
                break;
            }
        }
        return report;
    }

    private static StepReport RunStep(string name, TakenStep step, CancellationToken stop)
    {
        var powershell = step.Language == "powershell";
        if (!powershell && step.Language != "cmd")
        {
            return new StepReport { Output = "This client version does not support this script type. Update the client." };
        }
        // 64-bit Windows' own PowerShell and cmd, also if this process were ever 32-bit.
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = Path.Combine(windows, Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32");
        var path = Path.Combine(Folder.Jobs, name + (powershell ? ".ps1" : ".cmd"));
        var watch = Stopwatch.StartNew();
        var kept = new Kept();
        try
        {
            Directory.CreateDirectory(Folder.Jobs);
            // Windows PowerShell reads a script as UTF-8 only when it starts with the mark.
            File.WriteAllText(path, step.Text ?? "", powershell ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true) : Encoding.Default);
            var oem = Encoding.GetEncoding((int) GetOEMCP());
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(system, powershell ? @"WindowsPowerShell\v1.0\powershell.exe" : "cmd.exe"),
                Arguments = powershell ? $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{path}\"" : $"/d /c \"\"{path}\"\"",
                WorkingDirectory = system,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = oem,
                StandardErrorEncoding = oem,
            };
            using var process = new Process { StartInfo = start };
            using var outDone = new ManualResetEventSlim();
            using var errDone = new ManualResetEventSlim();
            process.OutputDataReceived += (_, e) => Line(e.Data, kept, outDone);
            process.ErrorDataReceived += (_, e) => Line(e.Data, kept, errDone);
            process.Start();
            process.StandardInput.Close(); // nothing answers a script that asks
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var limit = TimeSpan.FromMinutes(Math.Min(Math.Max(step.Minutes, 1), 240));
            var timedOut = false;
            while (!process.WaitForExit(500))
            {
                if (stop.IsCancellationRequested || watch.Elapsed > limit)
                {
                    timedOut = true;
                    Kill(process, system);
                    process.WaitForExit(10000);
                    break;
                }
            }
            // The last of what it wrote. Not waited for long: something it started may still hold the pipe open.
            outDone.Wait(5000);
            errDone.Wait(5000);
            process.CancelOutputRead();
            process.CancelErrorRead();
            if (timedOut)
            {
                kept.Add(stop.IsCancellationRequested ? "[Stopped: the Rabbid Lab Client service is stopping.]" : $"[Stopped: the script exceeded the {limit.TotalMinutes:0}-minute time limit.]");
            }
            return new StepReport { Exit = timedOut || !process.HasExited ? null : process.ExitCode, Output = kept.Text(), Seconds = Math.Round(watch.Elapsed.TotalSeconds, 1), TimedOut = timedOut };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            kept.Add($"[Could not start the script on {Machine.Name}: {e.Message.Trim()}]");
            return new StepReport { Output = kept.Text(), Seconds = Math.Round(watch.Elapsed.TotalSeconds, 1) };
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Still held by what was stopped: the folder is only SYSTEM's and administrators'.
            }
        }
    }

    private static void Line(string? line, Kept kept, ManualResetEventSlim done)
    {
        if (line is null)
        {
            done.Set(); // the end of that stream
        }
        else
        {
            kept.Add(line);
        }
    }

    // The script and everything it started.
    private static void Kill(Process process, string system)
    {
        try
        {
            using var kill = Process.Start(new ProcessStartInfo(Path.Combine(system, "taskkill.exe"), $"/PID {process.Id} /T /F") { UseShellExecute = false, CreateNoWindow = true });
            kill?.WaitForExit(10000);
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // It ended by itself meanwhile.
        }
    }

    /// <summary>Sends how a job went, for ten minutes if the server is away (updating, say).</summary>
    private static async Task Report(JobReport report, CancellationToken stop)
    {
        for (var attempt = 1; attempt <= 20 && !stop.IsCancellationRequested; attempt++)
        {
            try
            {
                var settings = Settings.Read();
                var me = Identity.Load();
                if (settings.Server is null || me is null)
                {
                    return;
                }
                using var api = new ServerApi(settings.Server, settings.Fingerprint, TimeSpan.FromSeconds(60));
                var (code, _) = await api.Post<Nothing>("api/clients/jobs/done", report, me.Key, stop).ConfigureAwait(false);
                if ((int) code < 500)
                {
                    return;
                }
            }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !stop.IsCancellationRequested))
            {
                // Tried again below.
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stop).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    /// <summary>What a step wrote: the start and the end of it, when there's more than the server keeps.</summary>
    private sealed class Kept
    {
        private readonly StringBuilder head = new();
        private readonly Queue<string> tail = new();
        private int tailChars;
        private bool cut;

        public void Add(string line)
        {
            lock (head)
            {
                if (tail.Count == 0 && head.Length + line.Length <= HeadChars)
                {
                    head.AppendLine(line);
                    return;
                }
                if (line.Length > TailChars)
                {
                    line = line.Substring(line.Length - TailChars);
                    cut = true;
                }
                tail.Enqueue(line);
                tailChars += line.Length;
                while (tailChars > TailChars)
                {
                    tailChars -= tail.Dequeue().Length;
                    cut = true;
                }
            }
        }

        public string Text()
        {
            lock (head)
            {
                var all = new StringBuilder(head.ToString());
                if (cut)
                {
                    all.AppendLine().AppendLine("[... output truncated ...]").AppendLine();
                }
                foreach (var line in tail)
                {
                    all.AppendLine(line);
                }
                return all.ToString().TrimEnd();
            }
        }
    }
}
