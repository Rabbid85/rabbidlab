using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Connecting Microsoft cloud with a device sign-in, the lists read as Rabbid Lab's own app, and disconnecting.</summary>
public sealed class MicrosoftCloudTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-cloud-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public MicrosoftCloudTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
        app.Services.GetRequiredService<MicrosoftCloud>().Every = TimeSpan.FromMilliseconds(10);
    }

    public void Dispose()
    {
        app.Dispose();
        try
        {
            Directory.Delete(data, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temp folder's own cleanup.
        }
    }

    private static async Task<JsonNode> Json(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, text);
        return JsonNode.Parse(text)!;
    }

    // Connect, sign in at the pretend Microsoft, and wait for the server to finish setting up.
    private async Task<HttpClient> Connected()
    {
        var admin = await app.SignedIn();
        var started = await Json(await admin.PostAsJsonAsync("/api/cloud/connect", new { }));
        Assert.Equal("HQ7K-M2PX", started["code"]!.GetValue<string>());
        var waiting = await Json(await admin.GetAsync("/api/cloud"));
        Assert.False(waiting["connected"]!.GetValue<bool>());
        Assert.Equal("Waiting for you to sign in at Microsoft", waiting["connecting"]!["step"]!.GetValue<string>());
        app.Microsoft.SignedIn = true;
        for (var i = 0; i < 300; i++)
        {
            var now = await Json(await admin.GetAsync("/api/cloud"));
            Assert.Null(now["connecting"]?["problem"]?.GetValue<string>());
            if (now["connected"]!.GetValue<bool>())
            {
                return admin;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("Microsoft cloud never connected.");
    }

    [Fact]
    public async Task Connecting_makes_an_app_registration_with_a_certificate_and_keeps_nothing_of_the_admins_sign_in()
    {
        var admin = await Connected();
        var status = await Json(await admin.GetAsync("/api/cloud"));
        Assert.Equal("Northwind Labs", status["tenant"]!.GetValue<string>());
        Assert.Equal("northwindlabs.com", status["domain"]!.GetValue<string>());
        Assert.True(status["exchange"]!.GetValue<bool>());
        Assert.Equal(1, status["subscriptions"]!.GetValue<int>());

        // The app registration: a certificate to sign in with, only the permissions agreed, and Exchange as an app.
        var made = JsonNode.Parse(app.Microsoft.SentTo("POST", "https://graph.microsoft.com/v1.0/applications").Single())!;
        Assert.Equal("AsymmetricX509Cert", made["keyCredentials"]![0]!["type"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(made["keyCredentials"]![0]!["key"]!.GetValue<string>()));
        var asked = made["requiredResourceAccess"]!.AsArray().SelectMany(r => r!["resourceAccess"]!.AsArray()).Select(a => a!["id"]!.GetValue<string>()).ToList();
        Assert.Equal(["user-rw", "group-rw", "exo-app"], asked); // never Directory.ReadWrite.All, which the tenant also offers
        Assert.Equal(3, app.Microsoft.Sent("POST", "appRoleAssignedTo").Count);
        Assert.Contains("29232cdf-9323-42fd-ade2-1d097af3e4de", app.Microsoft.Sent("POST", "roleManagement/directory/roleAssignments").Single()); // Exchange Administrator
        Assert.Contains("acdd72a7-3385-48ef-bd42-f606fba81ae7", app.Microsoft.Sent("PUT", "subscriptions/sub-1/providers/Microsoft.Authorization/roleAssignments").Single()); // Reader

        // Kept: the app and its certificate, encrypted. Not kept: the admin's tokens.
        var file = File.ReadAllText(Path.Combine(data, "cloud.json"));
        Assert.Contains("app-id", file);
        Assert.DoesNotContain(FakeMicrosoft.AdminToken, file);
        Assert.DoesNotContain(FakeMicrosoft.AdminRefresh, file);
        Assert.DoesNotContain("MII", JsonNode.Parse(file)!["certificate"]!.GetValue<string>()[..8]); // not a bare PFX
    }

    [Fact]
    public async Task The_lists_are_read_as_the_app_with_its_certificate_page_by_page()
    {
        var admin = await Connected();
        var users = await Json(await admin.GetAsync("/api/cloud/entra/users"));
        Assert.Equal(["Avery Brooks", "Jordan Lee", "Sam Patel"], users.AsArray().Select(u => u!["name"]!.GetValue<string>()));
        Assert.Equal(2, users[0]!["licenses"]!.GetValue<int>());
        Assert.True(users[1]!["synced"]!.GetValue<bool>());

        // The app signed in with a signed statement carrying its certificate's thumbprint, never a secret.
        var signIn = app.Microsoft.Sent("POST", "/tenant-1/oauth2/v2.0/token").First();
        Assert.Contains("grant_type=client_credentials", signIn);
        Assert.Contains("client_id=app-id", signIn);
        var assertion = WebUtility.UrlDecode(signIn.Split('&').Single(p => p.StartsWith("client_assertion=", StringComparison.Ordinal))["client_assertion=".Length..]);
        var head = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Pad(assertion.Split('.')[0]))))!;
        Assert.Equal("RS256", head["alg"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(head["x5t"]!.GetValue<string>()));
        Assert.All(app.Microsoft.Bearers("graph.microsoft.com/v1.0/users"), t => Assert.Equal(FakeMicrosoft.AppToken, t));

        // Exchange Online's cmdlets, anchored to the tenant's onmicrosoft.com domain.
        var boxes = await Json(await admin.GetAsync("/api/cloud/exchange/mailboxes"));
        Assert.Equal("Shared", boxes[0]!["kind"]!.GetValue<string>());
        Assert.Equal(["info@northwindlabs.com", "info2@northwindlabs.com"], boxes[0]!["addresses"]!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.Contains("Get-Mailbox", app.Microsoft.Sent("POST", "adminapi/beta/tenant-1/InvokeCommand").Single());
        Assert.Equal("UPN:SystemMailbox{bb558c35-97f1-4cb9-8ff7-d53741dc928c}@northwindlabs.onmicrosoft.com", app.Microsoft.Anchor);
    }

    [Fact]
    public async Task Connecting_takes_Confirm_its_you_and_the_lists_wait_for_a_connection()
    {
        var admin = await app.SignedIn(confirmed: false);
        var refused = await admin.PostAsJsonAsync("/api/cloud/connect", new { });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.NotNull((await refused.Content.ReadFromJsonAsync<JsonNode>())!["confirm"]);
        Assert.Empty(app.Microsoft.Sent("POST", "devicecode"));

        var users = await admin.GetAsync("/api/cloud/entra/users");
        Assert.Equal(HttpStatusCode.BadRequest, users.StatusCode);
        Assert.Contains("not connected", await users.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Disconnecting_deletes_the_app_registration_and_forgets_the_tenant()
    {
        var admin = await Connected();
        var answer = await Json(await admin.PostAsJsonAsync("/api/cloud/disconnect", new { }));
        Assert.Null(answer["problem"]?.GetValue<string>());
        Assert.Single(app.Microsoft.Sent("DELETE", "graph.microsoft.com/v1.0/applications/app-object"));
        var status = await Json(await admin.GetAsync("/api/cloud"));
        Assert.False(status["connected"]!.GetValue<bool>());
        Assert.Null(status["tenant"]?.GetValue<string>());
        Assert.DoesNotContain("app-id", File.ReadAllText(Path.Combine(data, "cloud.json")));
    }

    private static string Pad(string b64) => b64.Replace('-', '+').Replace('_', '/') + new string('=', (4 - b64.Length % 4) % 4);
}

/// <summary>
/// A pretend Microsoft: device sign-in (pending until SignedIn), the tenant's Graph, an app's token, Exchange Online's
/// cmdlets and Azure. Keeps every request so a test can look at what was sent.
/// </summary>
public sealed class FakeMicrosoft : HttpMessageHandler
{
    public const string AdminToken = "admin-access-token-ABC";
    public const string AdminRefresh = "admin-refresh-token-XYZ";
    public const string AppToken = "app-access-token";
    private readonly List<(string Method, string Url, string Body, string? Bearer)> sent = [];

    public bool SignedIn { get; set; }
    public string? Anchor { get; private set; }

    /// <summary>The bodies sent with this method to an address containing this text.</summary>
    public List<string> Sent(string method, string url)
    {
        lock (sent)
        {
            return [.. sent.Where(s => s.Method == method && s.Url.Contains(url, StringComparison.Ordinal)).Select(s => s.Body)];
        }
    }

    /// <summary>The bodies sent with this method to exactly this address.</summary>
    public List<string> SentTo(string method, string url)
    {
        lock (sent)
        {
            return [.. sent.Where(s => s.Method == method && s.Url == url).Select(s => s.Body)];
        }
    }

    public List<string?> Bearers(string url)
    {
        lock (sent)
        {
            return [.. sent.Where(s => s.Url.Contains(url, StringComparison.Ordinal)).Select(s => s.Bearer)];
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var method = request.Method.Method;
        lock (sent)
        {
            sent.Add((method, url, body, request.Headers.Authorization?.Parameter));
        }
        if (request.Headers.TryGetValues("X-AnchorMailbox", out var anchor))
        {
            Anchor = anchor.Single();
        }
        return Answer(method, url, body);
    }

    private static HttpResponseMessage Ok(object json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(json), Encoding.UTF8, "application/json") };

    private HttpResponseMessage Answer(string method, string url, string body)
    {
        const string graph = "https://graph.microsoft.com/v1.0/";
        if (url.EndsWith("/organizations/oauth2/v2.0/devicecode", StringComparison.Ordinal))
        {
            return Ok(new { user_code = "HQ7K-M2PX", device_code = "device-1", verification_uri = "https://microsoft.com/devicelogin", expires_in = 900, interval = 5 });
        }
        if (url.EndsWith("/organizations/oauth2/v2.0/token", StringComparison.Ordinal))
        {
            if (body.Contains("grant_type=refresh_token", StringComparison.Ordinal))
            {
                return Ok(new { access_token = "admin-arm-token" });
            }
            return SignedIn ? Ok(new { access_token = AdminToken, refresh_token = AdminRefresh }) : Ok(new { error = "authorization_pending" }, HttpStatusCode.BadRequest);
        }
        if (url.EndsWith("/tenant-1/oauth2/v2.0/token", StringComparison.Ordinal))
        {
            return body.Contains("client_assertion=", StringComparison.Ordinal) ? Ok(new { access_token = AppToken, expires_in = 3600 }) : Ok(new { error = "invalid_client" }, HttpStatusCode.Unauthorized);
        }
        if (url.StartsWith(graph + "organization", StringComparison.Ordinal))
        {
            return Ok(new { value = new[] { new { id = "tenant-1", displayName = "Northwind Labs", verifiedDomains = new object[]
                { new { name = "northwindlabs.com", isDefault = true, isInitial = false }, new { name = "northwindlabs.onmicrosoft.com", isDefault = false, isInitial = true } } } } });
        }
        if (url.StartsWith(graph + "servicePrincipals?", StringComparison.Ordinal))
        {
            return url.Contains("00000003-0000-0000-c000-000000000000", StringComparison.Ordinal)
                ? Ok(new { value = new[] { new { id = "graph-sp", appRoles = new[] { new { id = "user-rw", value = "User.ReadWrite.All" }, new { id = "group-rw", value = "Group.ReadWrite.All" }, new { id = "dir-rw", value = "Directory.ReadWrite.All" } } } } })
                : Ok(new { value = new[] { new { id = "exo-sp", appRoles = new[] { new { id = "exo-app", value = "Exchange.ManageAsApp" } } } } });
        }
        if (method == "POST" && url == graph + "applications")
        {
            return Ok(new { id = "app-object", appId = "app-id" }, HttpStatusCode.Created);
        }
        if (method == "POST" && url == graph + "servicePrincipals")
        {
            return Ok(new { id = "app-sp" }, HttpStatusCode.Created);
        }
        if (method == "DELETE" || url.EndsWith("/owners/$ref", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
        if (url.Contains("appRoleAssignedTo", StringComparison.Ordinal) || url.Contains("roleManagement/directory/roleAssignments", StringComparison.Ordinal) || url.Contains("Microsoft.Authorization/roleAssignments", StringComparison.Ordinal))
        {
            return Ok(new { id = "assigned" }, HttpStatusCode.Created);
        }
        if (url.StartsWith("https://management.azure.com/subscriptions?", StringComparison.Ordinal))
        {
            return Ok(new { value = new[] { new { subscriptionId = "sub-1", displayName = "Production", state = "Enabled" } } });
        }
        if (url.StartsWith(graph + "users?", StringComparison.Ordinal))
        {
            return Ok(new { value = new object[]
            {
                new { id = "u1", displayName = "Avery Brooks", userPrincipalName = "avery@northwindlabs.com", accountEnabled = true, onPremisesSyncEnabled = (bool?) null, assignedLicenses = new object[] { new { }, new { } }, userType = "Member" },
                new { id = "u2", displayName = "Jordan Lee", userPrincipalName = "jordan@northwindlabs.com", accountEnabled = true, onPremisesSyncEnabled = (bool?) true, assignedLicenses = Array.Empty<object>(), userType = "Member" },
            }, odataNextLink = graph + "users-page-2" }.Renamed());
        }
        if (url == graph + "users-page-2")
        {
            return Ok(new { value = new[] { new { id = "u3", displayName = "Sam Patel", userPrincipalName = "sam@northwindlabs.com", accountEnabled = false, userType = "Member" } } });
        }
        if (url.Contains("/adminapi/beta/tenant-1/InvokeCommand", StringComparison.Ordinal))
        {
            return Ok(new { value = new[] { new { ExternalDirectoryObjectId = "m1", DisplayName = "Info", PrimarySmtpAddress = "info@northwindlabs.com", RecipientTypeDetails = "SharedMailbox",
                EmailAddresses = new[] { "SMTP:info@northwindlabs.com", "smtp:info2@northwindlabs.com", "X500:/o=ExchangeLabs" } } } });
        }
        return Ok(new { error = new { code = "NotFound", message = $"The fake has no answer for {method} {url}" } }, HttpStatusCode.NotFound);
    }
}

internal static class FakeJson
{
    // Graph's paging link is "@odata.nextLink", which an anonymous object can't be named.
    public static JsonNode Renamed(this object o)
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(o)!.AsObject();
        if (node.Remove("odataNextLink", out var next))
        {
            node["@odata.nextLink"] = next;
        }
        return node;
    }
}
