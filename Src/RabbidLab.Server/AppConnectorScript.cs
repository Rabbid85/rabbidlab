using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>
/// Any app, through PowerShell scripts of your own, for apps with no connector yet (an ERP, a database, a line-of-business
/// app). The scripts live in a folder under Connectors in the data folder, which only administrators of this server can
/// write to: the console can pick a folder there by name, never upload or change a script. They run as the service.
///   Read.ps1                                      prints { "accounts": [{ id, name, email, login, active }], "groups": [{ id, name, type, members: [account id] }] }
///   New-Account.ps1 -Username -Name -Email -First -Last   prints the new account's id
///   Add-Member.ps1 / Remove-Member.ps1 -Group -Account
///   Disable-Account.ps1 -Account
/// The secret, when one is set, is in $env:RABBIDLAB_SECRET. A script that fails exits with a code other than 0.
/// </summary>
public sealed partial class ScriptConnector(DataFolder folder) : IAppConnector
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(2);

    public string Kind => "script";
    public string Name => "PowerShell scripts";
    public string About => "Any other app, such as an ERP or a database: your own scripts read its accounts and groups and make the changes.";
    public string Account => "account";
    public string Group => "group";
    public bool CanCreate => true;

    public IReadOnlyList<ConnectorField> Fields { get; } =
    [
        new("folder", "Scripts folder", "A folder name under Connectors in the data folder, such as Syspro"),
        new("secret", "Secret for the scripts (optional)", "Given to the scripts as $env:RABBIDLAB_SECRET", Secret: true),
    ];

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 _-]{0,39}$")]
    private static partial Regex FolderName();

    /// <summary>Where connectors' scripts go.</summary>
    public string Root => Path.Combine(folder.Path, "Connectors");

    private string Where(AppLink link) => Path.Combine(Root, link["folder"]);

    public string? Bad(AppLink link) =>
        !OperatingSystem.IsWindows() ? "Script connectors run Windows PowerShell, which this server does not have."
        : !FolderName().IsMatch(link["folder"]) ? "Enter the folder's name: letters, numbers, spaces, - and _ only."
        : !File.Exists(Path.Combine(Where(link), "Read.ps1")) ? $"There is no Read.ps1 in {Where(link)}. An administrator of this server puts the scripts there."
        : null;

    private async Task<string> Run(AppLink link, string script, CancellationToken stop, params (string Name, string Value)[] arguments)
    {
        var path = Path.Combine(Where(link), script);
        if (Bad(link) is { } bad)
        {
            throw new AppProblem(bad);
        }
        if (!File.Exists(path))
        {
            throw new AppProblem($"The connector has no {script}.");
        }
        var start = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Where(link) };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path })
        {
            start.ArgumentList.Add(a);
        }
        // Each value its own argument, so nothing in a name can become part of the command.
        foreach (var (name, value) in arguments)
        {
            start.ArgumentList.Add($"-{name}");
            start.ArgumentList.Add(value);
        }
        if (link.Secret is { Length: > 0 } secret)
        {
            start.Environment["RABBIDLAB_SECRET"] = secret;
        }
        using var process = Process.Start(start) ?? throw new AppProblem("PowerShell could not be started.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(Limit);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var error = process.StandardError.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            if (process.ExitCode != 0)
            {
                var said = (await error).Trim() is { Length: > 0 } e ? e : (await output).Trim();
                throw new AppProblem($"{script} failed (exit code {process.ExitCode}){(said.Length > 0 ? $": {said[..Math.Min(said.Length, 300)]}" : ".")}");
            }
            return (await output).Trim();
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new AppProblem($"{script} did not finish in {Limit.TotalMinutes:0} minutes and was stopped.");
        }
    }

    public async Task<AppSnapshot> Read(AppLink link, CancellationToken stop)
    {
        var text = await Run(link, "Read.ps1", stop);
        try
        {
            var j = JsonNode.Parse(text) ?? throw new JsonException();
            static string S(JsonNode? n) => n?.ToString() ?? "";
            var accounts = (j["accounts"]?.AsArray() ?? []).OfType<JsonNode>().Where(a => S(a["id"]).Length > 0).Select(a => new AppAccount
            {
                Id = S(a["id"]), Name = S(a["name"]) is { Length: > 0 } n ? n : S(a["id"]), Email = S(a["email"]), Login = S(a["login"]) is { Length: > 0 } l ? l : S(a["id"]),
                Active = a["active"] is null || a["active"]!.GetValueKind() != JsonValueKind.False,
            }).ToList();
            var groups = (j["groups"]?.AsArray() ?? []).OfType<JsonNode>().Where(g => S(g["id"]).Length > 0).Select(g => new AppGroup
            {
                Id = S(g["id"]), Name = S(g["name"]) is { Length: > 0 } n ? n : S(g["id"]), Type = S(g["type"]) is { Length: > 0 } t ? t : "Group",
                Members = [.. (g["members"]?.AsArray() ?? []).Select(S).Where(m => m.Length > 0)],
            }).ToList();
            return new AppSnapshot(accounts, groups);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new AppProblem("Read.ps1 did not print the JSON expected: an object with accounts and groups.");
        }
    }

    public async Task<string> Create(AppLink link, AppPerson who, CancellationToken stop) =>
        await Run(link, "New-Account.ps1", stop, ("Username", who.Username), ("Name", who.Name), ("Email", who.Email), ("First", who.First), ("Last", who.Last)) is { Length: > 0 } id
            ? id.Split('\n')[^1].Trim() : throw new AppProblem("New-Account.ps1 did not print the new account's id.");

    public Task AddMember(AppLink link, AppGroup group, AppAccount account, CancellationToken stop) => Run(link, "Add-Member.ps1", stop, ("Group", group.Id), ("Account", account.Id));

    public Task RemoveMember(AppLink link, AppGroup group, AppAccount account, CancellationToken stop) => Run(link, "Remove-Member.ps1", stop, ("Group", group.Id), ("Account", account.Id));

    public Task Disable(AppLink link, AppAccount account, CancellationToken stop) => Run(link, "Disable-Account.ps1", stop, ("Account", account.Id));
}
