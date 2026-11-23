using System.Text.Json.Nodes;

namespace RabbidLab.Server;

/// <summary>
/// The Microsoft cloud pages: the connection (MicrosoftCloud.cs), then read-only lists for Entra ID, Exchange, Microsoft
/// 365 and Azure, each shaped for the page's grid. Seeing them takes View Microsoft cloud; connecting takes its own permission.
/// </summary>
public static class CloudApi
{
    public static void MapCloudApi(this WebApplication app)
    {
        app.MapGet("/api/cloud", (HttpContext context, MicrosoftCloud cloud) =>
        {
            if (!Access.Can(context, Permissions.CloudView) && !Access.Can(context, Permissions.CloudConnect))
            {
                return Access.NotAllowed();
            }
            var f = cloud.Now();
            var c = cloud.Connecting;
            return Results.Ok(new
            {
                connected = cloud.Connected,
                tenant = f.TenantName, domain = f.Domain, tenantId = f.Tenant, app = f.AppId, since = f.Connected, by = f.ConnectedBy,
                exchange = f.Exchange, subscriptions = f.Subscriptions.Count, notes = f.Notes,
                permissions = MicrosoftCloud.GraphPermissions,
                connecting = c is null ? null : new { code = c.Code, url = c.Url, expires = c.Expires, step = c.Step, done = c.Done.ToList(), problem = c.Problem, finished = c.Finished },
            });
        });

        // Starts Microsoft's device sign-in. The person who connects signs in at Microsoft with a Global Administrator account.
        app.MapPost("/api/cloud/connect", async (HttpContext context, MicrosoftCloud cloud, Confirmations confirm, ActivityLog log) =>
        {
            if ((Access.RefuseUnless(context, Permissions.CloudConnect) ?? confirm.Refuse(context)) is { } refused)
            {
                return refused;
            }
            if (cloud.Connected)
            {
                return Results.BadRequest(new { error = "Microsoft cloud is already connected. Disconnect it first." });
            }
            try
            {
                var c = await cloud.Connect(ActivityLog.Who(context), context.RequestAborted);
                log.Record(context, "cloud", "cloud.connect", "Started connecting Microsoft cloud");
                return Results.Ok(new { code = c.Code, url = c.Url, expires = c.Expires });
            }
            catch (Exception e) when (e is CloudProblem or HttpRequestException)
            {
                return Results.BadRequest(new { error = e.Message });
            }
        });

        app.MapPost("/api/cloud/disconnect", async (HttpContext context, MicrosoftCloud cloud, Confirmations confirm, ActivityLog log) =>
        {
            if ((Access.RefuseUnless(context, Permissions.CloudConnect) ?? confirm.Refuse(context)) is { } refused)
            {
                return refused;
            }
            var tenant = cloud.Now().TenantName;
            var problem = await cloud.Disconnect();
            log.Record(context, "cloud", "cloud.disconnect", $"Disconnected Microsoft cloud ({tenant})", problem is null ? "ok" : "warn");
            return Results.Ok(new { problem });
        });

        // The lists, each read when the page asks.
        Map(app, "/api/cloud/entra/users", async c => Users(await c.GraphList("users?$select=id,displayName,userPrincipalName,mail,accountEnabled,jobTitle,department,onPremisesSyncEnabled,assignedLicenses,createdDateTime,userType&$top=999")));
        Map(app, "/api/cloud/entra/groups", async c => (await c.GraphList("groups?$select=id,displayName,mail,groupTypes,securityEnabled,mailEnabled,membershipRule,onPremisesSyncEnabled,description,createdDateTime&$top=999")).Select(g => new
        {
            id = S(g, "id"), name = S(g, "displayName"), mail = S(g, "mail"), description = S(g, "description"), created = S(g, "createdDateTime"),
            kind = GroupKind(g), dynamic = S(g, "membershipRule") is not null, synced = B(g, "onPremisesSyncEnabled"),
        }));
        Map(app, "/api/cloud/entra/devices", async c => (await c.GraphList("devices?$select=id,displayName,operatingSystem,operatingSystemVersion,trustType,isCompliant,isManaged,approximateLastSignInDateTime,accountEnabled,registrationDateTime&$top=999")).Select(d => new
        {
            id = S(d, "id"), name = S(d, "displayName"), os = $"{S(d, "operatingSystem")} {S(d, "operatingSystemVersion")}".Trim(), join = Join(S(d, "trustType")),
            compliant = d["isCompliant"]?.GetValue<bool?>(), managed = B(d, "isManaged"), enabled = B(d, "accountEnabled"), seen = S(d, "approximateLastSignInDateTime"), registered = S(d, "registrationDateTime"),
        }));
        Map(app, "/api/cloud/entra/roles", async c => (await c.GraphList("directoryRoles?$expand=members($select=id,displayName,userPrincipalName)")).Select(r => new
        {
            id = S(r, "id"), name = S(r, "displayName"), description = S(r, "description"),
            members = r["members"]?.AsArray().Select(m => new { id = S(m, "id"), name = S(m, "displayName"), upn = S(m, "userPrincipalName"), kind = (S(m, "@odata.type") ?? "").Replace("#microsoft.graph.", "") }),
        }));
        Map(app, "/api/cloud/m365/licenses", async c =>
        {
            var skus = await c.GraphList("subscribedSkus");
            return skus.Select(s => new
            {
                id = S(s, "skuId"), name = Sku(S(s, "skuPartNumber")), part = S(s, "skuPartNumber"), state = S(s, "capabilityStatus"),
                total = s["prepaidUnits"]?["enabled"]?.GetValue<int>() ?? 0, used = s["consumedUnits"]?.GetValue<int>() ?? 0,
            });
        });
        Map(app, "/api/cloud/exchange/mailboxes", async c => (await c.Exchange("Get-Mailbox", new JsonObject { ["ResultSize"] = "Unlimited" })).Select(m => new
        {
            id = S(m, "ExternalDirectoryObjectId") ?? S(m, "Guid"), name = S(m, "DisplayName"), address = S(m, "PrimarySmtpAddress"), alias = S(m, "Alias"),
            kind = Mailbox(S(m, "RecipientTypeDetails")), hidden = B(m, "HiddenFromAddressListsEnabled"), forwarding = S(m, "ForwardingSmtpAddress") ?? S(m, "ForwardingAddress"),
            archive = S(m, "ArchiveStatus"), litigation = B(m, "LitigationHoldEnabled"), created = S(m, "WhenCreated"),
            addresses = m["EmailAddresses"]?.AsArray().Select(a => a?.GetValue<string>()).OfType<string>().Where(a => a.StartsWith("smtp:", StringComparison.OrdinalIgnoreCase)).Select(a => a[5..]),
        }));
        Map(app, "/api/cloud/exchange/groups", async c => (await c.Exchange("Get-DistributionGroup", new JsonObject { ["ResultSize"] = "Unlimited" })).Select(g => new
        {
            id = S(g, "ExternalDirectoryObjectId") ?? S(g, "Guid"), name = S(g, "DisplayName"), address = S(g, "PrimarySmtpAddress"),
            kind = S(g, "RecipientTypeDetails") == "MailUniversalSecurityGroup" ? "Mail-enabled security" : "Distribution list",
            outside = !(g["RequireSenderAuthenticationEnabled"]?.GetValue<bool>() ?? true), owners = g["ManagedBy"]?.AsArray().Select(o => o?.GetValue<string>()),
        }));
        Map(app, "/api/cloud/exchange/contacts", async c => (await c.Exchange("Get-MailContact", new JsonObject { ["ResultSize"] = "Unlimited" })).Select(x => new
        {
            id = S(x, "Guid"), name = S(x, "DisplayName"), address = S(x, "ExternalEmailAddress")?.Replace("SMTP:", "", StringComparison.OrdinalIgnoreCase), hidden = B(x, "HiddenFromAddressListsEnabled"),
        }));
        Map(app, "/api/cloud/exchange/rules", async c => (await c.Exchange("Get-TransportRule")).Select(r => new
        {
            id = S(r, "Guid"), name = S(r, "Name"), state = S(r, "State"), priority = r["Priority"]?.GetValue<int>(), mode = S(r, "Mode"), description = S(r, "Description"),
        }));
        Map(app, "/api/cloud/exchange/quarantine", async c => (await c.Exchange("Get-QuarantineMessage", new JsonObject { ["PageSize"] = 500 }, most: 500)).Select(q => new
        {
            id = S(q, "Identity"), subject = S(q, "Subject"), sender = S(q, "SenderAddress"), to = q["RecipientAddress"]?.AsArray().Select(a => a?.GetValue<string>()),
            why = S(q, "QuarantineTypes") ?? S(q, "Type"), received = S(q, "ReceivedTime"), expires = S(q, "Expires"), released = B(q, "Released"),
        }));
        Map(app, "/api/cloud/azure/subscriptions", async c => (await c.AzureList("subscriptions?api-version=2022-12-01")).Select(s => new
        {
            id = S(s, "subscriptionId"), name = S(s, "displayName"), state = S(s, "state"),
        }));
        Map(app, "/api/cloud/azure/machines", async c =>
        {
            var list = new List<object>();
            foreach (var s in await c.AzureList("subscriptions?api-version=2022-12-01"))
            {
                var sub = S(s, "subscriptionId");
                foreach (var vm in await c.AzureList($"subscriptions/{sub}/providers/Microsoft.Compute/virtualMachines?api-version=2024-07-01&statusOnly=true"))
                {
                    var id = S(vm, "id") ?? "";
                    var power = vm["properties"]?["instanceView"]?["statuses"]?.AsArray().Select(x => S(x, "code")).FirstOrDefault(x => x?.StartsWith("PowerState/") == true)?[11..];
                    list.Add(new
                    {
                        id, name = S(vm, "name"), subscription = S(s, "displayName"), group = id.Split('/').SkipWhile(p => !p.Equals("resourceGroups", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(),
                        location = S(vm, "location"), size = vm["properties"]?["hardwareProfile"]?["vmSize"]?.GetValue<string>(),
                        os = vm["properties"]?["storageProfile"]?["osDisk"]?["osType"]?.GetValue<string>(), power = Power(power),
                    });
                }
            }
            return list;
        });
    }

    // A list: View Microsoft cloud, connected, and Microsoft's own words when it refuses.
    private static void Map(WebApplication app, string path, Func<MicrosoftCloud, Task<object>> read) =>
        app.MapGet(path, async (HttpContext context, MicrosoftCloud cloud) =>
        {
            if (!Access.Can(context, Permissions.CloudView))
            {
                return Access.NotAllowed();
            }
            if (!cloud.Connected)
            {
                return Results.BadRequest(new { error = "Microsoft cloud is not connected." });
            }
            try
            {
                return Results.Ok(await read(cloud));
            }
            catch (Exception e) when (e is CloudProblem or HttpRequestException or TaskCanceledException)
            {
                return Results.BadRequest(new { error = e is CloudProblem ? e.Message : $"Could not reach Microsoft: {e.Message}" });
            }
        });

    private static object Users(List<JsonNode> users) => users.Select(u => new
    {
        id = S(u, "id"), name = S(u, "displayName"), upn = S(u, "userPrincipalName"), mail = S(u, "mail"), enabled = B(u, "accountEnabled"),
        title = S(u, "jobTitle"), department = S(u, "department"), synced = B(u, "onPremisesSyncEnabled"), licenses = u["assignedLicenses"]?.AsArray().Count ?? 0,
        created = S(u, "createdDateTime"), guest = S(u, "userType") == "Guest",
    });

    private static string? S(JsonNode? n, string name) => MicrosoftCloud.Text(n, name);
    private static bool B(JsonNode? n, string name) => n?[name] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static string GroupKind(JsonNode g)
    {
        var unified = g["groupTypes"]?.AsArray().Any(t => t?.GetValue<string>() == "Unified") == true;
        return unified ? "Microsoft 365" : B(g, "securityEnabled") && B(g, "mailEnabled") ? "Mail-enabled security" : B(g, "securityEnabled") ? "Security" : "Distribution list";
    }

    private static string Join(string? trust) => trust switch
    {
        "AzureAd" => "Microsoft Entra joined",
        "ServerAd" => "Hybrid joined",
        "Workplace" => "Registered",
        _ => trust ?? "",
    };

    private static string Mailbox(string? kind) => kind switch
    {
        "UserMailbox" => "User",
        "SharedMailbox" => "Shared",
        "RoomMailbox" => "Room",
        "EquipmentMailbox" => "Equipment",
        "SchedulingMailbox" => "Scheduling",
        _ => kind ?? "",
    };

    private static string Power(string? state) => state switch
    {
        "running" => "Running",
        "deallocated" => "Stopped",
        "stopped" => "Stopped, still billed",
        "starting" => "Starting",
        "stopping" or "deallocating" => "Stopping",
        null => "Unknown",
        _ => state,
    };

    // The licence names people know, for the common SKUs; anything else shows as its part number.
    private static string Sku(string? part) => part switch
    {
        "O365_BUSINESS_ESSENTIALS" => "Microsoft 365 Business Basic",
        "O365_BUSINESS_PREMIUM" => "Microsoft 365 Business Standard",
        "SPB" => "Microsoft 365 Business Premium",
        "SPE_E3" => "Microsoft 365 E3",
        "SPE_E5" => "Microsoft 365 E5",
        "ENTERPRISEPACK" => "Office 365 E3",
        "ENTERPRISEPREMIUM" => "Office 365 E5",
        "STANDARDPACK" => "Office 365 E1",
        "EXCHANGESTANDARD" => "Exchange Online (Plan 1)",
        "EXCHANGEENTERPRISE" => "Exchange Online (Plan 2)",
        "AAD_PREMIUM" => "Microsoft Entra ID P1",
        "AAD_PREMIUM_P2" => "Microsoft Entra ID P2",
        "EMS" => "Enterprise Mobility + Security E3",
        "INTUNE_A" => "Microsoft Intune Plan 1",
        "FLOW_FREE" => "Power Automate Free",
        "POWER_BI_STANDARD" => "Power BI (free)",
        "TEAMS_EXPLORATORY" => "Teams Exploratory",
        "DEVELOPERPACK_E5" => "Microsoft 365 E5 Developer",
        _ => part ?? "",
    };
}
