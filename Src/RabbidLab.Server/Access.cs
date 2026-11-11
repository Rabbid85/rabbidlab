namespace RabbidLab.Server;

/// <summary>
/// A change must come from Rabbid Lab's own page: it carries the X-Rabbid-Lab header, which another site's page can't add.
/// What someone may see and do comes from their roles (Roles.cs). Signing in is SignIn.cs.
/// </summary>
public static class Access
{
    public const string Header = "X-Rabbid-Lab";

    /// <summary>Why a change isn't allowed (the answer to send), or null when it is.</summary>
    public static IResult? Refuse(HttpContext context) =>
        context.Request.Headers.ContainsKey(Header) ? null
            : Results.Json(new { error = $"This change must come from {Product.Name}'s own page (the {Header} header is missing)." }, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>Whether the person asking has a role that allows this.</summary>
    public static bool Can(HttpContext context, string permission) => context.RequestServices.GetRequiredService<Roles>().Can(context.User, permission);

    /// <summary>Like Refuse, and also refused when no role of theirs allows it.</summary>
    public static IResult? RefuseUnless(HttpContext context, string permission) => Refuse(context) ?? (Can(context, permission) ? null : NotAllowed());

    public static IResult NotAllowed() => Results.Json(new { error = "Your roles don't let you do that." }, statusCode: StatusCodes.Status403Forbidden);
}
