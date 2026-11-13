using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;
using RabbidLab.Server;
using RabbidLab.Updater;
using RabbidLab.Shared;
using Microsoft.AspNetCore.Http;

namespace RabbidLab.Tests;

/// <summary>
/// Rabbid Lab started in a test, with a data folder of the test's own and a stand-in for GitHub (FakeUpdateSource) so no
/// test asks the real one. The client it makes carries the X-Rabbid-Lab header, as the page's changes do.
/// </summary>
public sealed class TestApp(string data) : WebApplicationFactory<Program>
{
    public FakeUpdateSource Updates { get; } = new();

    /// <summary>Stands in for Active Directory, so no test needs a domain.</summary>
    public FakeDirectory Directory { get; } = new();

    /// <summary>Stands in for DNS: amw.com with dc01 answering and dc02 not.</summary>
    public FakeDomainFinder Finder { get; } = new();

    /// <summary>Stands in for this computer's Windows accounts: localadmin, an administrator, password "windows pass".</summary>
    public FakeWindowsAccounts Windows { get; } = new();

    /// <summary>The time the server sees, moved on by hand.</summary>
    public FakeClock Clock { get; } = new();

    /// <summary>Stands in for Rabbid Lab's Active Directory account, so no test changes a real domain.</summary>
    public FakeDirectoryTools Tools { get; } = new();

    /// <summary>Stands in for the network: what each sensor's check finds is whatever the test says.</summary>
    public FakeProbe Probe { get; } = new();

    /// <summary>Stands in for the mail server: keeps what would have been sent.</summary>
    public FakeMail Mail { get; } = new();

    /// <summary>Stands in for Discord, Teams, Slack, ntfy and webhooks: keeps what would have been posted.</summary>
    public FakeChannelSender Hooks { get; } = new();

    /// <summary>Stands in for the domain's DNS and the certificate store, so no test changes either.</summary>
    public FakeConsoleDns Dns { get; } = new();

    public FakeNamesCertificate Named { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DataPath", data);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDirectoryTools>();
            services.AddSingleton<IDirectoryTools>(Tools);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IUpdateSource>();
            services.AddSingleton<IUpdateSource>(Updates);
            services.RemoveAll<IDirectory>();
            services.AddSingleton<IDirectory>(Directory);
            services.RemoveAll<IDomainFinder>();
            services.AddSingleton<IDomainFinder>(Finder);
            services.RemoveAll<IWindowsAccounts>();
            services.AddSingleton<IWindowsAccounts>(Windows);
            services.RemoveAll<ISensorProbe>();
            services.AddSingleton<ISensorProbe>(Probe);
            services.RemoveAll<IMailSender>();
            services.AddSingleton<IMailSender>(Mail);
            services.RemoveAll<IChannelSender>();
            services.AddSingleton<IChannelSender>(Hooks);
            services.RemoveAll<IConsoleDns>();
            services.AddSingleton<IConsoleDns>(Dns);
            services.RemoveAll<INamesCertificate>();
            services.AddSingleton<INamesCertificate>(Named);
            // The tests run each round of checks themselves.
            foreach (var worker in services.Where(d => d.ImplementationType == typeof(SensorWorker)).ToList())
            {
                services.Remove(worker);
            }
        });
    }

    /// <summary>A browser on Rabbid Lab's own page: its changes carry the header. It keeps cookies and doesn't follow redirects.</summary>
    public HttpClient Page()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Rabbid-Lab", "1");
        return client;
    }

    /// <summary>A page signed in as a local administrator, setting Rabbid Lab up first when it isn't yet.</summary>
    public async Task<HttpClient> SignedIn(string username = "admin", string password = "correct horse")
    {
        var client = Page();
        var setup = await client.PostAsJsonAsync("/api/setup/local", new { username, password });
        if (!setup.IsSuccessStatusCode)
        {
            (await client.PostAsJsonAsync("/api/signin", new { username, password })).EnsureSuccessStatusCode();
        }
        return client;
    }

    /// <summary>Adds a notification and gives back its id: an email one through Gmail, or a webhook kind at an address.</summary>
    public static async Task<string> Notification(HttpClient admin, string name = "Me", string kind = "email", string? url = null)
    {
        var made = await admin.PostAsJsonAsync("/api/channels", kind == "email"
            ? new { kind, name, url, mail = new { provider = "gmail", username = "lab@gmail.com", password = "abcd efgh ijkl mnop", to = new[] { "me@example.com" } } }
            : (object) new { kind, name, url });
        made.EnsureSuccessStatusCode();
        return (await made.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetString()!;
    }

    /// <summary>The alert type new sensors and watched links start with alerts this many minutes in, to these notifications.</summary>
    public static async Task Alerting(HttpClient admin, int after, params string[] to)
    {
        var start = (await admin.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/alerts")).GetProperty("now").GetProperty("startType").GetString();
        (await admin.PostAsJsonAsync($"/api/alerts/types/{start}", new { name = "Default", after, again = 0, back = true, to })).EnsureSuccessStatusCode();
    }

    /// <summary>Adds an alert type and gives back its id.</summary>
    public static async Task<string> AlertType(HttpClient admin, string name, int after, int again = 0, bool back = true, params string[] to)
    {
        var made = await admin.PostAsJsonAsync("/api/alerts/types", new { name, after, again, back, to });
        made.EnsureSuccessStatusCode();
        return (await made.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetString()!;
    }
}

/// <summary>A clock that starts at the real time and moves only when told.</summary>
public sealed class FakeClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

/// <summary>A domain with two people: awiles in Domain Admins, and jreyes in Domain Users.</summary>
public sealed class FakeDirectory : IDirectory
{
    public string? Problem { get; set; }

    /// <summary>Like Active Directory's default: groups are listed only after a sign-in.</summary>
    public bool ListsAnonymously { get; set; }

    /// <summary>The domain controller the last call went to.</summary>
    public string? LastHost { get; private set; }

    /// <summary>The domain the last sign-in went to.</summary>
    public string? LastDomain { get; private set; }

    /// <summary>amw.com trusts lab.amw.com, and nothing else.</summary>
    public Task<IReadOnlyList<string>> Domains(string domain, CancellationToken stop) => Task.FromResult<IReadOnlyList<string>>([domain, "lab.amw.com"]);

    public Task<DirectoryList?> Search(string domain, string host, string? search, CancellationToken stop)
    {
        LastHost = host;
        if (Problem is not null)
        {
            throw new DirectoryProblem(Problem);
        }
        List<DirectoryEntry> all = [new("group", "Domain Admins", null, "Designated administrators of the domain"), new("group", "Domain Users", null, null),
            new("group", "Lab Admins", null, "Runs the lab"), new("user", "awiles", "Anthony Wiles", null), new("user", "jreyes", "Jordan Reyes", null)];
        var found = all.Where(e => string.IsNullOrEmpty(search) ? e.Kind == "group"
            : e.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || (e.Display?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        return Task.FromResult(ListsAnonymously ? new DirectoryList(found, false) : null);
    }

    /// <summary>awiles (password "domain pass") is in Domain Admins and Domain Users; jreyes ("user pass") only in Domain Users.</summary>
    public Task<DirectoryUser?> SignIn(string domain, string host, IReadOnlyCollection<string> groups, string username, string password, CancellationToken stop)
    {
        LastHost = host;
        LastDomain = domain;
        if (Problem is not null)
        {
            throw new DirectoryProblem(Problem);
        }
        var name = username.Trim();
        name = name[(name.LastIndexOf('\\') + 1)..];
        (string Display, string[] Groups)? who = name == "awiles" && password == "domain pass" ? ("Anthony Wiles", ["Domain Admins", "Domain Users"])
            : name == "jreyes" && password == "user pass" ? ("Jordan Reyes", ["Domain Users"]) : null;
        if (who is not { } w)
        {
            return Task.FromResult<DirectoryUser?>(null);
        }
        return Task.FromResult<DirectoryUser?>(new DirectoryUser(name, w.Display, [.. groups.Where(g => w.Groups.Contains(g, StringComparer.OrdinalIgnoreCase))]));
    }
}

/// <summary>This computer's Windows accounts as the tests see them: localadmin is an administrator, Administrator is off.</summary>
public sealed class FakeWindowsAccounts : IWindowsAccounts
{
    public List<WindowsAccount> Accounts { get; } =
    [
        new() { Name = "Administrator", Enabled = false, BuiltIn = true, Groups = ["Administrators"] },
        new() { Name = "localadmin", Enabled = true, Groups = ["Administrators", "Users"] },
    ];

    /// <summary>Every order it was given, passwords and all.</summary>
    public List<AccountOrder> Orders { get; } = [];

    public Task<string?> Administrator(string name, string password, CancellationToken stop) =>
        Task.FromResult(name == "localadmin" && password == "windows pass" ? "localadmin" : null);

    public Task<AccountAnswer> Do(AccountOrder order, CancellationToken stop)
    {
        Orders.Add(order);
        var a = Accounts.FirstOrDefault(x => string.Equals(x.Name, order.Name, StringComparison.OrdinalIgnoreCase));
        AccountAnswer ok = new() { Id = order.Id, Ok = true };
        switch (order.Action)
        {
            case AccountOrder.List:
                return Task.FromResult(new AccountAnswer { Id = order.Id, Ok = true, Accounts = Accounts, Groups = [new() { Name = "Administrators", Sid = "S-1-5-32-544" }, new() { Name = "Users" }] });
            case AccountOrder.Create when a is not null:
                return Task.FromResult(AccountAnswer.Failed(order.Id, $"There's already an account called {order.Name}."));
            case AccountOrder.Create:
                Accounts.Add(new WindowsAccount { Name = order.Name!, FullName = order.FullName, Enabled = true, Groups = order.Add ?? [] });
                return Task.FromResult(ok);
        }
        if (a is null)
        {
            return Task.FromResult(AccountAnswer.Failed(order.Id, $"There's no account called {order.Name}."));
        }
        switch (order.Action)
        {
            case AccountOrder.Enable or AccountOrder.Disable:
                a.Enabled = order.Action == AccountOrder.Enable;
                break;
            case AccountOrder.Groups:
                a.Groups = [.. (a.Groups ?? []).Concat(order.Add ?? []).Except(order.Remove ?? []).Distinct()];
                break;
            case AccountOrder.Delete:
                Accounts.Remove(a);
                break;
        }
        return Task.FromResult(ok);
    }
}

/// <summary>
/// Rabbid Lab's Active Directory account as the tests see it: set up with awiles ("domain pass"), with jreyes in the
/// domain to begin with. Nothing is kept outside the test.
/// </summary>
public sealed class FakeDirectoryTools : IDirectoryTools
{
    private DirectoryToolsState? state;

    public List<AdUser> People { get; } = [Person("jreyes", "Jordan Reyes")];

    /// <summary>The passwords set, by username.</summary>
    public Dictionary<string, string> Passwords { get; } = [];

    public string? Domain => "amw.com";

    /// <summary>Whether lists say Active Directory had more than it sent.</summary>
    public bool More { get; set; }

    public DirectoryToolsState? State() => state;

    public Task<SetupLook> Look(string? domain, string username, string password, CancellationToken stop) =>
        password == "domain pass" ? Task.FromResult(new SetupLook("amw.com", "DC=amw,DC=com", [new("OU=Lab,DC=amw,DC=com", "Lab", "Lab")], "CN=Users,DC=amw,DC=com", "svc-rabbidlab"))
        : throw new ToolProblem("That username or password is wrong.");

    public Task<string?> Connect(SetupForm form, string by, CancellationToken stop)
    {
        if (form.Password != "domain pass")
        {
            throw new ToolProblem("That username or password is wrong.");
        }
        state = new DirectoryToolsState("amw.com", form.Existing ? form.Username! : form.Account ?? "svc-rabbidlab", "DC=amw,DC=com", "all of amw.com",
            "CN=Users,DC=amw,DC=com", "Users", !form.Existing, by, DateTime.UtcNow);
        return Task.FromResult<string?>(null);
    }

    public void Disconnect() => state = null;

    public Task SetNewUsers(string dn, CancellationToken stop) => Task.CompletedTask;

    public Task<List<AdOu>> Places(CancellationToken stop) => Task.FromResult<List<AdOu>>([new("CN=Users,DC=amw,DC=com", "Users", "Users")]);

    public Task<(List<AdUser> Users, bool More)> Users(CancellationToken stop) => Task.FromResult((Ready(), false));

    public Task<AdUser> User(string username, CancellationToken stop) => Task.FromResult(Get(username));

    public Task<(List<AdGroup> Groups, bool More)> Groups(string? search, CancellationToken stop) =>
        Task.FromResult<(List<AdGroup>, bool)>(([new("CN=Lab Admins,DC=amw,DC=com", "Lab Admins", null, "")], More));

    public Task<(AdUser User, string? Warning)> Create(NewAdUser form, string password, CancellationToken stop)
    {
        if (Ready().Any(u => u.Username == form.Username))
        {
            throw new ToolProblem($"There's already an account called {form.Username} in amw.com.");
        }
        var made = Person(form.Username!, form.Display ?? $"{form.First} {form.Last}");
        People.Add(made);
        Passwords[made.Username] = password;
        return Task.FromResult<(AdUser, string?)>((made, null));
    }

    public Task Password(string username, string password, bool mustChange, CancellationToken stop)
    {
        Get(username);
        Passwords[username] = password;
        return Task.CompletedTask;
    }

    public Task Unlock(string username, CancellationToken stop) => Task.FromResult(Get(username));

    public Task Enable(string username, bool on, CancellationToken stop)
    {
        var u = Get(username);
        People[People.IndexOf(u)] = u with { Enabled = on };
        return Task.CompletedTask;
    }

    public Task<string?> ChangeGroups(string username, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken stop) => Task.FromResult<string?>(null);

    public Task Delete(string username, CancellationToken stop)
    {
        People.Remove(Get(username));
        return Task.CompletedTask;
    }

    /// <summary>Lab Admins and who's in it, by DN.</summary>
    public Dictionary<string, List<string>> GroupMembers { get; } = new() { ["CN=Lab Admins,DC=amw,DC=com"] = [] };

    public Task<(List<AdMember> Members, bool More)> Members(string group, CancellationToken stop)
    {
        Ready();
        return Task.FromResult(GroupMembers.TryGetValue(group, out var m) ? (m.Select(dn => new AdMember(dn, dn.Split(',')[0][3..], "user", null, "Users")).ToList(), More)
            : throw new ToolProblem("That group isn't in Active Directory any more.", StatusCodes.Status404NotFound));
    }

    public Task<AdGroup> CreateGroup(NewAdGroup form, CancellationToken stop)
    {
        Ready();
        var dn = $"CN={form.Name},CN=Users,DC=amw,DC=com";
        GroupMembers[dn] = [];
        return Task.FromResult(new AdGroup(dn, form.Name!, form.Description, "Users", form.Scope ?? "Global", form.Security));
    }

    public Task<string?> ChangeMembers(string group, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken stop)
    {
        Ready();
        var members = GroupMembers[group];
        members.AddRange(add.Except(members));
        members.RemoveAll(remove.Contains);
        return Task.FromResult<string?>(null);
    }

    public Task DeleteGroup(string group, CancellationToken stop)
    {
        Ready();
        GroupMembers.Remove(group);
        return Task.CompletedTask;
    }

    public List<AdComputer> Machines { get; } = [new("AMW-LT-022", "CN=AMW-LT-022,CN=Computers,DC=amw,DC=com", "Computers", "amw-lt-022.amw.com", "Windows 11 Pro", null, null, true, false, null, null)];

    public Task<(List<AdComputer> Computers, bool More)> Computers(CancellationToken stop)
    {
        Ready();
        return Task.FromResult((Machines, false));
    }

    public Task EnableComputer(string name, bool on, CancellationToken stop)
    {
        Ready();
        var i = Machines.FindIndex(c => c.Name == name);
        Machines[i] = Machines[i] with { Enabled = on };
        return Task.CompletedTask;
    }

    public Task DeleteComputer(string name, CancellationToken stop)
    {
        Ready();
        Machines.RemoveAll(c => c.Name == name);
        return Task.CompletedTask;
    }

    public Task<List<AdMember>> Find(string? search, CancellationToken stop) =>
        Task.FromResult(Ready().Where(u => u.Username.Contains(search ?? "", StringComparison.OrdinalIgnoreCase)).Select(u => new AdMember(u.Dn, u.Name ?? u.Username, "user", u.Username, u.Where)).ToList());

    private List<AdUser> Ready() => state is null ? throw new ToolProblem("Set it up first.", StatusCodes.Status409Conflict) : People;

    private AdUser Get(string username) =>
        Ready().FirstOrDefault(u => u.Username == username) ?? throw new ToolProblem($"There's no account called {username} any more.", StatusCodes.Status404NotFound);

    private static AdUser Person(string username, string name) =>
        new(username, name, $"{username}@amw.com", null, null, null, $"CN={name},CN=Users,DC=amw,DC=com", "Users", true, false, false, false, false, false, false, null, null, null, []);
}

/// <summary>DNS as the tests see it: amw.com, with dc01 answering in 4 ms and dc02 not answering, and lab.amw.com with dc1.</summary>
public sealed class FakeDomainFinder : IDomainFinder
{
    public Task<IReadOnlyList<string>> Domains(CancellationToken stop) => Task.FromResult<IReadOnlyList<string>>(["amw.com"]);

    public Task<IReadOnlyList<FoundController>> Controllers(string domain, CancellationToken stop) =>
        Task.FromResult<IReadOnlyList<FoundController>>(domain == "amw.com" ? [new("dc01.amw.com", true, 4), new("dc02.amw.com", false, null)]
            : domain == "lab.amw.com" ? [new("dc1.lab.amw.com", true, 6)] : []);
}

/// <summary>A source of releases that offers whatever the test says, and counts how often it's asked.</summary>
public sealed class FakeUpdateSource : IUpdateSource
{
    public UpdateOffer? Offer { get; set; }

    public DateTime? CheckedAt { get; private set; }

    public string? Problem { get; set; }

    public int Checks { get; private set; }

    public Task Check(CancellationToken cancel)
    {
        Checks++;
        CheckedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task Fetch(UpdateOffer offer, Func<Stream, CancellationToken, Task> read, CancellationToken cancel) =>
        throw new InvalidOperationException("the stand-in source has nothing to hand over");
}

/// <summary>An installer runner that only notes what it was asked to run, or refuses as Windows might.</summary>
public sealed class FakeRunner : IInstallerRunner
{
    public List<(string Installer, string Log)> Runs { get; } = [];

    /// <summary>What Run throws instead of running, when set.</summary>
    public Exception? Refuse { get; set; }

    public void Run(string installer, string log)
    {
        if (Refuse is not null)
        {
            throw Refuse;
        }
        Runs.Add((installer, log));
    }
}

/// <summary>Checks that find what the test set for each address; anything not set answers in 5 ms.</summary>
public sealed class FakeProbe : ISensorProbe
{
    public Dictionary<string, Probe> Finds { get; } = [];

    public Task<Probe> Check(Sensor sensor, CancellationToken stop) => Task.FromResult(Finds.GetValueOrDefault(sensor.Target) ?? new Probe(true, 5));
}

/// <summary>A channel service that keeps every alert it was asked to post.</summary>
public sealed class FakeChannelSender : IChannelSender
{
    public List<(string Kind, string Url, MailNote Note)> Sent { get; } = [];

    public Task<string?> Send(string kind, string url, MailNote note, CancellationToken stop)
    {
        Sent.Add((kind, url, note));
        return Task.FromResult<string?>(null);
    }
}

/// <summary>A mail server that keeps every email it was asked to send.</summary>
public sealed class FakeMail : IMailSender
{
    /// <summary>Why every send fails, or null for them to go.</summary>
    public string? Fails { get; set; }

    public List<MailNote> Sent { get; } = [];

    /// <summary>The settings each email went with.</summary>
    public List<MailSettings> With { get; } = [];

    public Task<string?> Send(MailSettings settings, MailNote note, CancellationToken stop)
    {
        if (Fails is not null)
        {
            return Task.FromResult<string?>(Fails);
        }
        Sent.Add(note);
        With.Add(settings);
        return Task.FromResult<string?>(null);
    }
}

/// <summary>A domain, amw.com, whose DNS is whatever the test says: records by name, and the aliases added and taken out.</summary>
public sealed class FakeConsoleDns : IConsoleDns
{
    public string? Domain { get; set; } = "amw.com";

    public string Target => "server1.amw.com";

    public Dictionary<string, List<RabbidLab.Setup.DnsRecordInfo>> Records { get; } = [];

    public List<string> Added { get; } = [];

    public List<string> Removed { get; } = [];

    public List<DnsLogin> Logins { get; } = [];

    public (NameLeads Leads, string Where) Lookup(string name) => Added.Contains(name) ? (NameLeads.Here, Target) : (NameLeads.Nowhere, "");

    public RabbidLab.Setup.DnsFacts Read(string name, DnsLogin login)
    {
        Logins.Add(login);
        return login.Password == "wrong" ? throw new DnsProblem("The username or password is wrong.") : new RabbidLab.Setup.DnsFacts
        {
            Name = name,
            Server = "dc01.amw.com",
            Target = Target,
            Zones = [new() { Name = "amw.com", Primary = true }],
            Records = Records.GetValueOrDefault(name) ?? [],
            TargetAddresses = ["192.0.2.10"],
            MyAddresses = ["192.0.2.10"],
        };
    }

    public void Add(RabbidLab.Setup.DnsFacts facts, string zone, DnsLogin login) => Added.Add(facts.Name);

    public string Remove(string name, string zone, string server, string target, DnsLogin login)
    {
        Logins.Add(login);
        Removed.Add($"{name} in {zone} on {server} to {target}");
        Added.Remove(name);
        return $"Took the alias {name} out of DNS on {server}.";
    }
}

/// <summary>A certificate store that keeps which names each certificate was made for.</summary>
public sealed class FakeNamesCertificate : INamesCertificate
{
    public bool Https { get; set; } = true;

    public X509Certificate2? Current { get; private set; }

    public List<List<string>> Made { get; } = [];

    public X509Certificate2? Make(IReadOnlyList<string> names)
    {
        Made.Add([.. names]);
        if (names.Count == 0)
        {
            return Current = null;
        }
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Console addresses", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        return Current = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));
    }
}
