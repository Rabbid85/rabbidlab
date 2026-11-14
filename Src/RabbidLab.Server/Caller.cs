namespace RabbidLab.Server;

/// <summary>
/// Who an Active Directory request is for and the permission it took, for as long as that request runs. The directory
/// tools read it to keep the request inside the person's roles: the OUs their roles cover (Roles.Reach) and nobody in a
/// role above them (Roles.Outranking). None means Rabbid Lab itself is asking, and only the service account's scope counts.
/// </summary>
public sealed record Caller(Who Who, string Permission, Roles Roles)
{
    private static readonly AsyncLocal<Caller?> Now = new();

    public static Caller? Current => Now.Value;

    /// <summary>Set for the rest of this request: it flows into everything the request awaits, and is gone when it ends.</summary>
    public static void Set(Caller? caller) => Now.Value = caller;

    /// <summary>The OUs this request may touch, or null for everywhere the service account reaches.</summary>
    public IReadOnlyList<string>? Reach() => Roles.Reach(Who, Permission);

    public IReadOnlyList<Role> Outranking() => Roles.Outranking(Who);
}
