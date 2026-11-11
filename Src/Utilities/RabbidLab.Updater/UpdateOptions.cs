namespace RabbidLab.Updater;

/// <summary>How Rabbid Lab updates itself: how often it looks, how long it waits, and the key releases are signed with.</summary>
public sealed class UpdateOptions
{
    /// <summary>How often it looks at what the source last offered.</summary>
    public TimeSpan Every { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Nothing in the first moments after starting, so the source's first check-in has answered.</summary>
    public TimeSpan SettleTime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Something a signed-in person does counts as using the console for this long; the page's own background polls don't.
    /// With Automatic installation on, a newer version installs by itself once nobody has used the console for this
    /// long; until then it's offered, and installs when someone chooses Install now.
    /// </summary>
    public TimeSpan InUseFor { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>A version that didn't install isn't tried again by itself for this long.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Check for updates doesn't ask the source again if anything asked it this recently.</summary>
    public TimeSpan AskAgainAfter { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>A page opening (the sign-in page) asks the source again unless its last check-in is newer than this.</summary>
    public TimeSpan CheckWhenOpened { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// A page staying open asks the source again once its last check-in is older than this: one small request at most
    /// this often, and only while someone is looking at it.
    /// </summary>
    public TimeSpan CheckWhileOpen { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The longest a download may take in all.</summary>
    public TimeSpan DownloadLimit { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>The longest a download may go without a byte arriving: a stalled one ends with its reason.</summary>
    public TimeSpan StallAfter { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Once the installer has started, this long counts as installing: it restarts Rabbid Lab within it.</summary>
    public TimeSpan InstallWait { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>The public key every release must be signed with (Rabbid Lab's offline release key; the tests use their own).</summary>
    public string ReleasePublicKey { get; set; } = Release.ReleaseKey;
}
