using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RabbidLab.Server;

/// <summary>The HTTP the connectors share, so tests can answer for an app.</summary>
public sealed class AppHttp(HttpMessageHandler? handler = null)
{
    public HttpClient Client { get; } = handler is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Sends JSON and gives the answer as JSON (null for none). An answer that isn't a success is an AppProblem with what the app said.</summary>
    public async Task<JsonNode?> Send(HttpMethod method, string url, string token, JsonNode? body, CancellationToken stop, string media = "application/json")
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json, application/scim+json");
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, media);
        }
        using var response = await Client.SendAsync(request, stop);
        var text = await response.Content.ReadAsStringAsync(stop);
        if (!response.IsSuccessStatusCode)
        {
            throw new AppProblem($"{new Uri(url).Host} answered {(int) response.StatusCode}{(Said(text) is { } said ? $": {said}" : "")}");
        }
        try
        {
            return text.Length == 0 ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            throw new AppProblem($"{new Uri(url).Host} did not answer with JSON.");
        }
    }

    // What went wrong, as SCIM (detail) and Google (error.message) say it.
    private static string? Said(string text)
    {
        try
        {
            var j = JsonNode.Parse(text);
            return (j?["detail"] ?? j?["error"]?["message"] ?? j?["error_description"])?.ToString() is { Length: > 0 } said ? said[..Math.Min(said.Length, 300)] : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Any app with a SCIM 2.0 endpoint (RFC 7644): users and groups, read a page at a time, group members changed with
/// PATCH, users made with POST and turned off with active false. Signs in with a bearer token.
/// </summary>
public sealed class ScimConnector(AppHttp http) : IAppConnector
{
    private const string PatchOp = "urn:ietf:params:scim:api:messages:2.0:PatchOp";
    private const string UserSchema = "urn:ietf:params:scim:schemas:core:2.0:User";
    private const int Page = 100;

    public string Kind => "scim";
    public string Name => "SCIM 2.0";
    public string About => "Any app with a SCIM 2.0 endpoint: users and groups.";
    public string Account => "user";
    public string Group => "group";
    public bool CanCreate => true;

    public IReadOnlyList<ConnectorField> Fields { get; } =
    [
        new("url", "Base URL", "https://app.example.com/scim/v2"),
        new("token", "Bearer token", Secret: true),
    ];

    public string? Bad(AppLink link) =>
        !Uri.TryCreate(link["url"], UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ? "Enter the SCIM base URL, starting with https://."
        : string.IsNullOrEmpty(link.Secret) ? "Enter the bearer token."
        : null;

    private static string Base(AppLink link) => link["url"].TrimEnd('/');

    // Every resource of a kind: SCIM pages from 1, and says how many there are.
    private async Task<List<JsonNode>> All(AppLink link, string what, CancellationToken stop)
    {
        var all = new List<JsonNode>();
        for (var start = 1; start < 50_000; start += Page)
        {
            var page = await http.Send(HttpMethod.Get, $"{Base(link)}/{what}?startIndex={start}&count={Page}", link.Secret!, null, stop);
            var got = page?["Resources"]?.AsArray().OfType<JsonNode>().ToList() ?? [];
            all.AddRange(got);
            if (got.Count == 0 || all.Count >= (page?["totalResults"]?.GetValue<int>() ?? 0))
            {
                break;
            }
        }
        return all;
    }

    public async Task<AppSnapshot> Read(AppLink link, CancellationToken stop)
    {
        var users = (await All(link, "Users", stop)).Select(u => new AppAccount
        {
            Id = u["id"]!.ToString(),
            Login = u["userName"]?.ToString() ?? "",
            Name = u["displayName"]?.ToString() ?? u["name"]?["formatted"]?.ToString() ?? u["userName"]?.ToString() ?? "",
            Email = (u["emails"]?.AsArray().OfType<JsonNode>().OrderByDescending(e => e["primary"]?.GetValue<bool>() == true).FirstOrDefault()?["value"]?.ToString()) ?? "",
            Active = u["active"]?.GetValue<bool>() ?? true,
        }).ToList();
        var groups = (await All(link, "Groups", stop)).Select(g => new AppGroup
        {
            Id = g["id"]!.ToString(),
            Name = g["displayName"]?.ToString() ?? g["id"]!.ToString(),
            Members = [.. g["members"]?.AsArray().OfType<JsonNode>().Select(m => m["value"]?.ToString()).OfType<string>() ?? []],
        }).ToList();
        return new AppSnapshot(users, groups);
    }

    public async Task<string> Create(AppLink link, AppPerson who, CancellationToken stop)
    {
        if (who.Email.Length == 0)
        {
            throw new AppProblem($"{who.Name} has no email address in Active Directory to make the account with.");
        }
        var made = await http.Send(HttpMethod.Post, $"{Base(link)}/Users", link.Secret!, new JsonObject
        {
            ["schemas"] = new JsonArray(UserSchema),
            ["userName"] = who.Email,
            ["displayName"] = who.Name,
            ["name"] = new JsonObject { ["givenName"] = who.First, ["familyName"] = who.Last, ["formatted"] = who.Name },
            ["emails"] = new JsonArray(new JsonObject { ["value"] = who.Email, ["primary"] = true, ["type"] = "work" }),
            ["active"] = true,
        }, stop, "application/scim+json");
        return made?["id"]?.ToString() ?? throw new AppProblem("The app made the user but did not say its id.");
    }

    private Task Patch(AppLink link, string path, JsonObject operation, CancellationToken stop) =>
        http.Send(HttpMethod.Patch, $"{Base(link)}/{path}", link.Secret!, new JsonObject { ["schemas"] = new JsonArray(PatchOp), ["Operations"] = new JsonArray(operation) }, stop, "application/scim+json");

    public Task AddMember(AppLink link, AppGroup group, AppAccount account, CancellationToken stop) =>
        Patch(link, $"Groups/{Uri.EscapeDataString(group.Id)}", new JsonObject { ["op"] = "add", ["path"] = "members", ["value"] = new JsonArray(new JsonObject { ["value"] = account.Id }) }, stop);

    public Task RemoveMember(AppLink link, AppGroup group, AppAccount account, CancellationToken stop) =>
        Patch(link, $"Groups/{Uri.EscapeDataString(group.Id)}", new JsonObject { ["op"] = "remove", ["path"] = $"members[value eq \"{account.Id.Replace("\"", "")}\"]" }, stop);

    public Task Disable(AppLink link, AppAccount account, CancellationToken stop) =>
        Patch(link, $"Users/{Uri.EscapeDataString(account.Id)}", new JsonObject { ["op"] = "replace", ["path"] = "active", ["value"] = false }, stop);
}
