using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RabbidLab.Server;

/// <summary>
/// Google Workspace through the Admin SDK Directory API: users, groups and their members. It signs in as a Google Cloud
/// service account with domain-wide delegation (its JSON key is the secret), acting as an admin of the domain.
/// </summary>
public sealed class GoogleConnector(AppHttp http, TimeProvider clock) : IAppConnector
{
    private const string Api = "https://admin.googleapis.com/admin/directory/v1";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string Scopes = "https://www.googleapis.com/auth/admin.directory.user https://www.googleapis.com/auth/admin.directory.group";

    private readonly Dictionary<string, (string Token, DateTime Until)> tokens = [];

    public string Kind => "google";
    public string Name => "Google Workspace";
    public string About => "Users and groups, through the Admin SDK. Needs a service account key with domain-wide delegation.";
    public string Account => "user";
    public string Group => "group";
    public bool CanCreate => true;

    public IReadOnlyList<ConnectorField> Fields { get; } =
    [
        new("domain", "Domain", "example.com"),
        new("admin", "Act as this admin", "admin@example.com"),
        new("key", "Service account key (JSON)", "Paste the contents of the key file", Secret: true, Long: true),
    ];

    public string? Bad(AppLink link)
    {
        if (link["domain"].Length == 0 || !link["admin"].Contains('@'))
        {
            return "Enter the domain and the email address of an admin to act as.";
        }
        try
        {
            var key = JsonNode.Parse(link.Secret ?? "");
            return key?["client_email"] is null || key["private_key"] is null ? "Paste the service account key: the JSON file from Google Cloud, with client_email and private_key." : null;
        }
        catch (JsonException)
        {
            return "The service account key is not JSON. Paste the contents of the key file.";
        }
    }

    // An access token for the admin, from a JWT signed with the service account's key; kept until shortly before it ends.
    private async Task<string> Token(AppLink link, CancellationToken stop)
    {
        var now = clock.GetUtcNow();
        var who = $"{link["admin"]}|{link.Secret!.GetHashCode()}";
        lock (tokens)
        {
            if (tokens.TryGetValue(who, out var kept) && kept.Until > now.UtcDateTime)
            {
                return kept.Token;
            }
        }
        var key = JsonNode.Parse(link.Secret!)!;
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var head = B64(Encoding.UTF8.GetBytes("""{"alg":"RS256","typ":"JWT"}"""));
        var claims = B64(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = key["client_email"]!.ToString(), ["sub"] = link["admin"], ["scope"] = Scopes, ["aud"] = TokenUrl,
            ["iat"] = now.ToUnixTimeSeconds(), ["exp"] = now.AddMinutes(55).ToUnixTimeSeconds(),
        }));
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(key["private_key"]!.ToString());
        }
        catch (ArgumentException)
        {
            throw new AppProblem("The private key in the service account key could not be read.");
        }
        var jwt = $"{head}.{claims}.{B64(rsa.SignData(Encoding.ASCII.GetBytes($"{head}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))}";
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = jwt }),
        };
        using var response = await http.Client.SendAsync(request, stop);
        var answer = JsonNode.Parse(await response.Content.ReadAsStringAsync(stop));
        if (!response.IsSuccessStatusCode || answer?["access_token"]?.ToString() is not { Length: > 0 } token)
        {
            throw new AppProblem($"Google refused the sign-in: {answer?["error_description"] ?? answer?["error"] ?? response.ReasonPhrase}. Check domain-wide delegation for the service account, and its scopes.");
        }
        lock (tokens)
        {
            tokens[who] = (token, now.UtcDateTime.AddMinutes(50));
        }
        return token;
    }

    private async Task<List<JsonNode>> All(AppLink link, string url, string list, CancellationToken stop)
    {
        var all = new List<JsonNode>();
        string? next = null;
        do
        {
            var page = await http.Send(HttpMethod.Get, $"{url}{(url.Contains('?') ? '&' : '?')}maxResults=200{(next is null ? "" : $"&pageToken={Uri.EscapeDataString(next)}")}", await Token(link, stop), null, stop);
            all.AddRange(page?[list]?.AsArray().OfType<JsonNode>() ?? []);
            next = page?["nextPageToken"]?.ToString();
        }
        while (next is { Length: > 0 } && all.Count < 50_000);
        return all;
    }

    public async Task<AppSnapshot> Read(AppLink link, CancellationToken stop)
    {
        var domain = Uri.EscapeDataString(link["domain"]);
        var users = (await All(link, $"{Api}/users?domain={domain}&orderBy=email", "users", stop)).Select(u => new AppAccount
        {
            Id = u["id"]!.ToString(),
            Email = u["primaryEmail"]?.ToString() ?? "",
            Login = (u["primaryEmail"]?.ToString() ?? "").Split('@')[0],
            Name = u["name"]?["fullName"]?.ToString() ?? u["primaryEmail"]?.ToString() ?? "",
            Active = u["suspended"]?.GetValue<bool>() != true,
            LastSignIn = DateTime.TryParse(u["lastLoginTime"]?.ToString(), out var at) && at.Year > 1971 ? at.ToUniversalTime() : null,
        }).ToList();
        var groups = new List<AppGroup>();
        foreach (var g in await All(link, $"{Api}/groups?domain={domain}", "groups", stop))
        {
            var id = g["id"]!.ToString();
            var members = await All(link, $"{Api}/groups/{Uri.EscapeDataString(id)}/members", "members", stop);
            groups.Add(new AppGroup { Id = id, Name = g["email"]?.ToString() ?? g["name"]?.ToString() ?? id, Members = [.. members.Where(m => m["type"]?.ToString() == "USER").Select(m => m["id"]!.ToString())] });
        }
        return new AppSnapshot(users, groups);
    }

    public async Task<string> Create(AppLink link, AppPerson who, CancellationToken stop)
    {
        if (who.Email.Length == 0 || !who.Email.EndsWith("@" + link["domain"], StringComparison.OrdinalIgnoreCase))
        {
            throw new AppProblem($"{who.Name} has no email address in {link["domain"]} to make the user with.");
        }
        // A password nobody is told: they set their own through Google's account recovery, or sign in with SSO.
        var made = await http.Send(HttpMethod.Post, $"{Api}/users", await Token(link, stop), new JsonObject
        {
            ["primaryEmail"] = who.Email,
            ["name"] = new JsonObject { ["givenName"] = who.First.Length > 0 ? who.First : who.Name, ["familyName"] = who.Last.Length > 0 ? who.Last : who.Name },
            ["password"] = Passwords.Make(32),
            ["changePasswordAtNextLogin"] = true,
        }, stop);
        return made?["id"]?.ToString() ?? throw new AppProblem("Google made the user but did not say its id.");
    }

    public async Task AddMember(AppLink link, AppGroup group, AppAccount account, CancellationToken stop) =>
        await http.Send(HttpMethod.Post, $"{Api}/groups/{Uri.EscapeDataString(group.Id)}/members", await Token(link, stop), new JsonObject { ["id"] = account.Id, ["role"] = "MEMBER" }, stop);

    public async Task RemoveMember(AppLink link, AppGroup group, AppAccount account, CancellationToken stop) =>
        await http.Send(HttpMethod.Delete, $"{Api}/groups/{Uri.EscapeDataString(group.Id)}/members/{Uri.EscapeDataString(account.Id)}", await Token(link, stop), null, stop);

    public async Task Disable(AppLink link, AppAccount account, CancellationToken stop) =>
        await http.Send(HttpMethod.Put, $"{Api}/users/{Uri.EscapeDataString(account.Id)}", await Token(link, stop), new JsonObject { ["suspended"] = true }, stop);
}
