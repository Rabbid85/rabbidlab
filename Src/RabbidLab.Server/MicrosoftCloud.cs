using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;

namespace RabbidLab.Server;

/// <summary>The connection to a Microsoft 365 tenant, as kept in cloud.json. The certificate's private key is encrypted.</summary>
public sealed class CloudFile
{
    public string? Tenant { get; set; }
    public string? TenantName { get; set; }
    public string? Domain { get; set; }

    /// <summary>The tenant's onmicrosoft.com domain, which Exchange Online's REST calls are anchored to.</summary>
    public string? Initial { get; set; }

    /// <summary>Rabbid Lab's own app registration in the tenant: its client id, its object id, and its enterprise app's id.</summary>
    public string? AppId { get; set; }
    public string? AppObjectId { get; set; }
    public string? ServicePrincipalId { get; set; }

    /// <summary>The app's certificate as a PFX, encrypted with this server's data protection keys, and its thumbprint.</summary>
    public string? Certificate { get; set; }
    public string? Thumbprint { get; set; }
    public DateTime? Connected { get; set; }
    public string? ConnectedBy { get; set; }
    public bool Exchange { get; set; }
    public List<string> Subscriptions { get; set; } = [];

    /// <summary>What connecting couldn't do, in words, such as Azure without a subscription to give access to.</summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>Connecting under way: the code to enter at Microsoft, then each step as it's done.</summary>
public sealed class CloudConnecting
{
    public string Code { get; init; } = "";
    public string Url { get; init; } = "";
    public DateTime Expires { get; init; }
    public string By { get; init; } = "";
    public string Step { get; set; } = "Waiting for you to sign in at Microsoft";
    public List<string> Done { get; } = [];
    public string? Problem { get; set; }
    public bool Finished { get; set; }
}

/// <summary>The HttpClient Microsoft is called with; tests put their own handler in.</summary>
public sealed class CloudHttp(HttpMessageHandler? handler = null)
{
    public HttpClient Client { get; } = handler is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(60) } : new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
}

public sealed class CloudProblem(string message) : Exception(message);

/// <summary>
/// Microsoft 365, Entra ID, Exchange and Azure, through Rabbid Lab's own app registration in the tenant. Connecting: a
/// Global Administrator signs in once with Microsoft's device sign-in, and that sign-in is used only to create the app
/// registration, consent to its permissions, give it a certificate, the Exchange Administrator role and Reader on the
/// Azure subscriptions it can; it is never kept. Afterwards Rabbid Lab signs in as the app with the certificate.
/// Disconnecting deletes the app registration (the app owns itself, so it may).
/// </summary>
public sealed class MicrosoftCloud(DataFolder folder, IDataProtectionProvider protection, CloudHttp http, TimeProvider clock)
{
    // Azure CLI's public client: Microsoft's own, so one device sign-in gives tokens for Graph and for Azure.
    public const string SignInClient = "04b07795-8ddb-461a-bbee-02f9e1bf7b46";
    private const string Login = "https://login.microsoftonline.com";
    private const string Graph = "https://graph.microsoft.com/v1.0";
    private const string GraphApp = "00000003-0000-0000-c000-000000000000";
    private const string ExchangeApp = "00000002-0000-0ff1-ce00-000000000000";
    private const string ExchangeAdministrator = "29232cdf-9323-42fd-ade2-1d097af3e4de";
    private const string AzureReader = "acdd72a7-3385-48ef-bd42-f606fba81ae7";
    private const string Arm = "https://management.azure.com";

    /// <summary>The Graph application permissions the app is given, agreed 2026-11-22. Never Directory.ReadWrite.All,
    /// AppRoleAssignment.ReadWrite.All or RoleManagement.ReadWrite.Directory, which would let it make itself an admin.</summary>
    public static readonly IReadOnlyList<string> GraphPermissions =
    [
        "User.ReadWrite.All", "User-PasswordProfile.ReadWrite.All", "User.RevokeSessions.All", "LicenseAssignment.ReadWrite.All", "Group.ReadWrite.All",
        "Device.ReadWrite.All", "Organization.Read.All", "Reports.Read.All", "AuditLog.Read.All", "Policy.Read.All", "RoleManagement.Read.Directory",
        "IdentityRiskyUser.Read.All", "Application.Read.All", "Application.ReadWrite.OwnedBy",
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<CloudFile> file = folder.Json<CloudFile>("cloud.json", Json);
    private readonly IDataProtector protector = protection.CreateProtector("RabbidLab.MicrosoftCloud");
    private readonly Lock gate = new();
    private readonly Dictionary<string, (string Token, DateTime Until)> tokens = [];
    private CloudConnecting? connecting;
    private X509Certificate2? certificate;

    /// <summary>How long between asking Microsoft whether the sign-in is done; null waits as long as Microsoft says. Tests set it.</summary>
    public TimeSpan? Every { get; set; }

    public CloudFile Now()
    {
        lock (gate)
        {
            return file.Read();
        }
    }

    public bool Connected => Now() is { AppId: not null, Tenant: not null, Certificate: not null };

    public CloudConnecting? Connecting
    {
        get
        {
            lock (gate)
            {
                return connecting;
            }
        }
    }

    // --- Connecting -----------------------------------------------------------------------------------------------------

    /// <summary>Starts Microsoft's device sign-in; the person enters the code at Microsoft, and the rest happens here.</summary>
    public async Task<CloudConnecting> Connect(string by, CancellationToken stop)
    {
        lock (gate)
        {
            if (connecting is { Finished: false } busy && busy.Expires > clock.GetUtcNow().UtcDateTime)
            {
                return busy;
            }
        }
        var answer = await Post($"{Login}/organizations/oauth2/v2.0/devicecode", new()
        {
            ["client_id"] = SignInClient,
            ["scope"] = "https://graph.microsoft.com/.default offline_access",
        }, stop);
        var c = new CloudConnecting
        {
            Code = Text(answer, "user_code") ?? throw new CloudProblem("Microsoft did not give a sign-in code."),
            Url = Text(answer, "verification_uri") ?? "https://microsoft.com/devicelogin",
            Expires = clock.GetUtcNow().UtcDateTime.AddSeconds(answer?["expires_in"]?.GetValue<int>() ?? 900),
            By = by,
        };
        lock (gate)
        {
            connecting = c;
        }
        var device = Text(answer, "device_code")!;
        var every = Math.Max(answer?["interval"]?.GetValue<int>() ?? 5, 3);
        _ = Task.Run(() => Finish(c, device, every));
        return c;
    }

    // Waits for the sign-in, then sets the tenant up. Every problem lands on the connecting card.
    private async Task Finish(CloudConnecting c, string device, int every)
    {
        try
        {
            JsonNode? signedIn = null;
            while (signedIn is null)
            {
                if (clock.GetUtcNow().UtcDateTime > c.Expires)
                {
                    throw new CloudProblem("The sign-in code expired. Select Connect to get a new one.");
                }
                await Task.Delay(Every ?? TimeSpan.FromSeconds(every));
                using var r = await http.Client.PostAsync($"{Login}/organizations/oauth2/v2.0/token", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = SignInClient,
                    ["device_code"] = device,
                }));
                var j = JsonNode.Parse(await r.Content.ReadAsStringAsync());
                if (r.IsSuccessStatusCode)
                {
                    signedIn = j;
                }
                else if (Text(j, "error") is "authorization_declined")
                {
                    throw new CloudProblem("The sign-in was declined at Microsoft.");
                }
                else if (Text(j, "error") is not ("authorization_pending" or "slow_down"))
                {
                    throw new CloudProblem($"Microsoft refused the sign-in: {Text(j, "error_description")?.Split('\r', '\n')[0] ?? Text(j, "error")}");
                }
            }
            await SetUp(c, Text(signedIn, "access_token")!, Text(signedIn, "refresh_token"));
        }
        catch (Exception e) when (e is CloudProblem or HttpRequestException or TaskCanceledException or JsonException)
        {
            c.Problem = e is CloudProblem ? e.Message : $"Could not reach Microsoft: {e.Message}";
            c.Finished = true;
        }
    }

    // The tenant, the app registration with its certificate, consent, the Exchange role, then Azure. The admin's tokens go with this method.
    private async Task SetUp(CloudConnecting c, string token, string? refresh)
    {
        Step(c, "Reading your tenant");
        var org = (await Call(HttpMethod.Get, $"{Graph}/organization?$select=id,displayName,verifiedDomains", token))?["value"]?[0];
        var tenant = Text(org, "id") ?? throw new CloudProblem("Could not read the tenant.");
        var domain = org?["verifiedDomains"]?.AsArray().FirstOrDefault(d => d?["isDefault"]?.GetValue<bool>() == true)?["name"]?.GetValue<string>();
        var initial = org?["verifiedDomains"]?.AsArray().FirstOrDefault(d => d?["isInitial"]?.GetValue<bool>() == true)?["name"]?.GetValue<string>();
        Done(c, $"Tenant {Text(org, "displayName")} ({domain})");

        Step(c, "Creating the app registration");
        var graphSp = await ServicePrincipal(GraphApp, token);
        var exchangeSp = await ServicePrincipal(ExchangeApp, token);
        var roles = graphSp?["appRoles"]?.AsArray().Where(r => GraphPermissions.Contains(Text(r, "value"))).Select(r => Text(r, "id")!).ToList() ?? [];
        var manageAsApp = exchangeSp?["appRoles"]?.AsArray().FirstOrDefault(r => Text(r, "value") == "Exchange.ManageAsApp") is { } m ? Text(m, "id") : null;
        using var rsa = RSA.Create(2048);
        var name = $"{Product.Title} ({Environment.MachineName})";
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        var access = new JsonArray { new JsonObject { ["resourceAppId"] = GraphApp, ["resourceAccess"] = new JsonArray([.. roles.Select(id => (JsonNode) new JsonObject { ["id"] = id, ["type"] = "Role" })]) } };
        if (manageAsApp is not null)
        {
            access.Add(new JsonObject { ["resourceAppId"] = ExchangeApp, ["resourceAccess"] = new JsonArray(new JsonObject { ["id"] = manageAsApp, ["type"] = "Role" }) });
        }
        var app = await Call(HttpMethod.Post, $"{Graph}/applications", token, new JsonObject
        {
            ["displayName"] = name,
            ["signInAudience"] = "AzureADMyOrg",
            ["notes"] = $"Made by {Product.Title} on {Environment.MachineName} when {c.By} connected it. Disconnecting in {Product.Title} deletes it.",
            ["requiredResourceAccess"] = access,
            ["keyCredentials"] = new JsonArray(new JsonObject { ["type"] = "AsymmetricX509Cert", ["usage"] = "Verify", ["key"] = Convert.ToBase64String(made.RawData), ["displayName"] = name }),
        });
        var appId = Text(app, "appId") ?? throw new CloudProblem("Microsoft did not create the app registration.");
        var appObject = Text(app, "id")!;
        var sp = await Call(HttpMethod.Post, $"{Graph}/servicePrincipals", token, new JsonObject { ["appId"] = appId });
        var spId = Text(sp, "id")!;
        // It owns itself, so Application.ReadWrite.OwnedBy lets Disconnect delete it and nothing else.
        await Call(HttpMethod.Post, $"{Graph}/applications/{appObject}/owners/$ref", token, new JsonObject { ["@odata.id"] = $"{Graph}/directoryObjects/{spId}" });
        Done(c, $"App registration {name}");

        Step(c, "Granting its permissions");
        foreach (var role in roles)
        {
            await Call(HttpMethod.Post, $"{Graph}/servicePrincipals/{Text(graphSp, "id")}/appRoleAssignedTo", token,
                new JsonObject { ["principalId"] = spId, ["resourceId"] = Text(graphSp, "id"), ["appRoleId"] = role });
        }
        if (manageAsApp is not null)
        {
            await Call(HttpMethod.Post, $"{Graph}/servicePrincipals/{Text(exchangeSp, "id")}/appRoleAssignedTo", token,
                new JsonObject { ["principalId"] = spId, ["resourceId"] = Text(exchangeSp, "id"), ["appRoleId"] = manageAsApp });
        }
        Done(c, $"{roles.Count} Microsoft Graph permissions{(manageAsApp is null ? "" : " and Exchange")}");

        var notes = new List<string>();
        Step(c, "Giving it the Exchange Administrator role");
        var exchange = false;
        try
        {
            await Call(HttpMethod.Post, $"{Graph}/roleManagement/directory/roleAssignments", token,
                new JsonObject { ["principalId"] = spId, ["roleDefinitionId"] = ExchangeAdministrator, ["directoryScopeId"] = "/" });
            exchange = true;
            Done(c, "Exchange Administrator role");
        }
        catch (CloudProblem e)
        {
            notes.Add($"Exchange: the Exchange Administrator role could not be given ({e.Message}). Exchange pages stay empty until it is.");
        }

        var subscriptions = new List<string>();
        Step(c, "Giving it Reader on your Azure subscriptions");
        try
        {
            var arm = refresh is null ? null : await Refresh(refresh, $"{Arm}/.default offline_access");
            var subs = arm is null ? [] : (await Call(HttpMethod.Get, $"{Arm}/subscriptions?api-version=2022-12-01", arm))?["value"]?.AsArray() ?? [];
            foreach (var s in subs)
            {
                var id = Text(s, "subscriptionId")!;
                // A new enterprise app takes a moment to reach Azure: tried a few times.
                for (var tries = 0; ; tries++)
                {
                    try
                    {
                        await Call(HttpMethod.Put, $"{Arm}/subscriptions/{id}/providers/Microsoft.Authorization/roleAssignments/{Guid.NewGuid()}?api-version=2022-04-01", arm!, new JsonObject
                        {
                            ["properties"] = new JsonObject
                            {
                                ["roleDefinitionId"] = $"/subscriptions/{id}/providers/Microsoft.Authorization/roleDefinitions/{AzureReader}",
                                ["principalId"] = spId,
                                ["principalType"] = "ServicePrincipal",
                            },
                        });
                        subscriptions.Add(id);
                        break;
                    }
                    catch (CloudProblem e) when (tries < 5 && e.Message.Contains("PrincipalNotFound", StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(6));
                    }
                    catch (CloudProblem e)
                    {
                        notes.Add($"Azure: no access to {Text(s, "displayName")} ({e.Message}).");
                        break;
                    }
                }
            }
            if (subs.Count == 0)
            {
                notes.Add("Azure: the account you signed in with has no Azure subscriptions it can give access to.");
            }
            Done(c, subscriptions.Count == 0 ? "No Azure subscriptions" : $"Reader on {subscriptions.Count} Azure subscription{(subscriptions.Count == 1 ? "" : "s")}");
        }
        catch (CloudProblem e)
        {
            notes.Add($"Azure: {e.Message}");
        }

        lock (gate)
        {
            file.Save(new CloudFile
            {
                Tenant = tenant, TenantName = Text(org, "displayName"), Domain = domain, Initial = initial, AppId = appId, AppObjectId = appObject, ServicePrincipalId = spId,
                Certificate = Convert.ToBase64String(protector.Protect(made.Export(X509ContentType.Pfx))), Thumbprint = made.Thumbprint,
                Connected = clock.GetUtcNow().UtcDateTime, ConnectedBy = c.By, Exchange = exchange, Subscriptions = subscriptions, Notes = notes,
            });
            certificate = null;
            tokens.Clear();
        }
        Step(c, "Connected");
        c.Finished = true;
    }

    private static void Step(CloudConnecting c, string step) => c.Step = step;

    private static void Done(CloudConnecting c, string done)
    {
        lock (c.Done)
        {
            c.Done.Add(done);
        }
    }

    private async Task<JsonNode?> ServicePrincipal(string appId, string token) =>
        (await Call(HttpMethod.Get, $"{Graph}/servicePrincipals?$filter=appId eq '{appId}'&$select=id,appRoles", token))?["value"]?[0];

    private async Task<string?> Refresh(string refresh, string scope)
    {
        var answer = await Post($"{Login}/organizations/oauth2/v2.0/token", new()
        {
            ["grant_type"] = "refresh_token", ["client_id"] = SignInClient, ["refresh_token"] = refresh, ["scope"] = scope,
        }, CancellationToken.None);
        return Text(answer, "access_token");
    }

    /// <summary>Forgets the connection and deletes the app registration in the tenant, which takes its permissions and role with it.</summary>
    public async Task<string?> Disconnect()
    {
        string? problem = null;
        var f = Now();
        if (f.AppObjectId is { } id)
        {
            try
            {
                await Call(HttpMethod.Delete, $"{Graph}/applications/{id}", await Token("https://graph.microsoft.com/.default"));
            }
            catch (CloudProblem e)
            {
                problem = $"The app registration {f.AppId} could not be deleted ({e.Message}). Delete it in the Microsoft Entra admin center, under App registrations.";
            }
        }
        lock (gate)
        {
            file.Save(new CloudFile());
            certificate = null;
            tokens.Clear();
            connecting = null;
        }
        return problem;
    }

    // --- Signing in as the app, and calling Microsoft ---------------------------------------------------------------------

    /// <summary>A token for the app (client credentials with a certificate assertion), kept until five minutes before it runs out.</summary>
    public async Task<string> Token(string scope)
    {
        X509Certificate2 cert;
        CloudFile f;
        lock (gate)
        {
            f = file.Read();
            if (f.Tenant is null || f.AppId is null || f.Certificate is null)
            {
                throw new CloudProblem("Microsoft cloud is not connected.");
            }
            if (tokens.TryGetValue(scope, out var kept) && kept.Until > clock.GetUtcNow().UtcDateTime)
            {
                return kept.Token;
            }
            cert = certificate ??= X509CertificateLoader.LoadPkcs12(protector.Unprotect(Convert.FromBase64String(f.Certificate)), null);
        }
        var audience = $"{Login}/{f.Tenant}/oauth2/v2.0/token";
        var answer = await Post(audience, new()
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = f.AppId,
            ["scope"] = scope,
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = Assertion(cert, f.AppId, audience),
        }, CancellationToken.None);
        var token = Text(answer, "access_token") ?? throw new CloudProblem("Microsoft did not give the app a token.");
        lock (gate)
        {
            tokens[scope] = (token, clock.GetUtcNow().UtcDateTime.AddSeconds((answer?["expires_in"]?.GetValue<int>() ?? 3600) - 300));
        }
        return token;
    }

    // The signed statement the app signs in with: who it is, for which token endpoint, for the next ten minutes.
    public string Assertion(X509Certificate2 cert, string appId, string audience)
    {
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var head = B64(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["alg"] = "RS256", ["typ"] = "JWT", ["x5t"] = B64(cert.GetCertHash()) }));
        var body = B64(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["aud"] = audience, ["iss"] = appId, ["sub"] = appId, ["jti"] = Guid.NewGuid().ToString(), ["nbf"] = now - 60, ["exp"] = now + 600,
        }));
        using var key = cert.GetRSAPrivateKey() ?? throw new CloudProblem("The app's certificate has no private key.");
        var sig = key.SignData(Encoding.ASCII.GetBytes($"{head}.{body}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{head}.{body}.{B64(sig)}";
    }

    /// <summary>Every item of a Graph list, page by page, up to a limit.</summary>
    public async Task<List<JsonNode>> GraphList(string path, int most = 5000)
    {
        var token = await Token("https://graph.microsoft.com/.default");
        var all = new List<JsonNode>();
        for (string? next = $"{Graph}/{path}"; next is not null && all.Count < most;)
        {
            var page = await Call(HttpMethod.Get, next, token);
            all.AddRange(page?["value"]?.AsArray().OfType<JsonNode>() ?? []);
            next = Text(page, "@odata.nextLink");
        }
        return all;
    }

    /// <summary>An Exchange Online cmdlet through its REST endpoint, as the Exchange module runs it with an app's token.</summary>
    public async Task<List<JsonNode>> Exchange(string cmdlet, JsonObject? parameters = null, int most = 5000)
    {
        var f = Now();
        var token = await Token("https://outlook.office365.com/.default");
        var all = new List<JsonNode>();
        string? next = $"https://outlook.office365.com/adminapi/beta/{f.Tenant}/InvokeCommand";
        var body = new JsonObject { ["CmdletInput"] = new JsonObject { ["CmdletName"] = cmdlet, ["Parameters"] = parameters ?? new JsonObject() } };
        while (next is not null && all.Count < most)
        {
            var page = await Call(HttpMethod.Post, next, token, (JsonNode) body.DeepClone(), anchor: $"UPN:SystemMailbox{{bb558c35-97f1-4cb9-8ff7-d53741dc928c}}@{f.Initial ?? f.Domain}");
            all.AddRange(page?["value"]?.AsArray().OfType<JsonNode>() ?? []);
            next = Text(page, "@odata.nextLink");
        }
        return all;
    }

    /// <summary>An Azure Resource Manager list, page by page.</summary>
    public async Task<List<JsonNode>> AzureList(string path)
    {
        var token = await Token($"{Arm}/.default");
        var all = new List<JsonNode>();
        for (string? next = $"{Arm}/{path}"; next is not null && all.Count < 5000;)
        {
            var page = await Call(HttpMethod.Get, next, token);
            all.AddRange(page?["value"]?.AsArray().OfType<JsonNode>() ?? []);
            next = Text(page, "nextLink");
        }
        return all;
    }

    private async Task<JsonNode?> Call(HttpMethod method, string url, string token, JsonNode? body = null, string? anchor = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (anchor is not null)
        {
            request.Headers.TryAddWithoutValidation("X-AnchorMailbox", anchor);
        }
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        using var response = await http.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new CloudProblem(Said(response.StatusCode, text));
        }
        return text.Length == 0 ? null : JsonNode.Parse(text);
    }

    private async Task<JsonNode?> Post(string url, Dictionary<string, string> form, CancellationToken stop)
    {
        using var response = await http.Client.PostAsync(url, new FormUrlEncodedContent(form), stop);
        var text = await response.Content.ReadAsStringAsync(stop);
        if (!response.IsSuccessStatusCode)
        {
            var j = text.StartsWith('{') ? JsonNode.Parse(text) : null;
            var said = Text(j, "error_description")?.Split('\r', '\n')[0] ?? Text(j, "error") ?? $"HTTP {(int) response.StatusCode}";
            // A new app's certificate takes a minute or so to reach every sign-in server.
            throw new CloudProblem(said.Contains("AADSTS700016") || said.Contains("AADSTS7000215") || said.Contains("AADSTS700027")
                ? "Microsoft is still setting up Rabbid Lab's app. Try again in a minute." : said);
        }
        return JsonNode.Parse(text);
    }

    // Microsoft's error in a line: its message when it sent one.
    private static string Said(HttpStatusCode code, string text)
    {
        try
        {
            var j = JsonNode.Parse(text);
            var e = j?["error"];
            var message = e is JsonObject ? Text(e, "message") : Text(j, "message") ?? Text(j, "error_description");
            var what = e is JsonObject ? Text(e, "code") : null;
            return $"{message ?? $"HTTP {(int) code}"}{(what is null || message?.Contains(what) == true ? "" : $" ({what})")}";
        }
        catch (JsonException)
        {
            return code == HttpStatusCode.Forbidden ? "Not allowed." : $"HTTP {(int) code}";
        }
    }

    public static string? Text(JsonNode? node, string name) =>
        node is JsonObject o && o.TryGetPropertyValue(name, out var v) && v is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}
