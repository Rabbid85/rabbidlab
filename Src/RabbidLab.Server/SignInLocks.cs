using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>One name's wrong passwords and whether it's locked, as kept in signin-locks.json.</summary>
public sealed class LockEntry
{
    /// <summary>The name's key (SignInLocks.Key); for a name that isn't an account here, a keyed hash of it.</summary>
    public string Id { get; set; } = "";

    /// <summary>The account's name, from its key. Empty for a name that isn't an account here: it may be a password in the wrong box.</summary>
    public string Name { get; set; } = "";

    public int Wrong { get; set; }

    /// <summary>When the last wrong one came, and from where.</summary>
    public DateTime Last { get; set; }

    public string? From { get; set; }

    /// <summary>No try before this.</summary>
    public DateTime? Wait { get; set; }

    /// <summary>When it locked; null while it isn't.</summary>
    public DateTime? Locked { get; set; }

    /// <summary>Tries to unlock it with the code that had the code right and the password wrong.</summary>
    public int Unlocks { get; set; }
}

/// <summary>A browser that has signed in as an account, by a keyed hash of the token in its cookie.</summary>
public sealed class KnownBrowser
{
    public string Id { get; set; } = "";

    /// <summary>The account it signed in as (SignInLocks.Key).</summary>
    public string Key { get; set; } = "";

    public DateTime At { get; set; }
}

public sealed class LocksFile
{
    /// <summary>The key that names and browser tokens are hashed with, encrypted like the MFA secrets.</summary>
    public string? Secret { get; set; }

    public List<LockEntry> Names { get; set; } = [];

    public List<KnownBrowser> Browsers { get; set; } = [];
}

public enum LockState
{
    Open,
    Wait,
    Locked,
}

/// <summary>Where a name stands: open, waiting this many seconds, or locked. Just: this wrong password is the one that locked it.</summary>
public readonly record struct LockAnswer(LockState State, int Seconds = 0, bool Just = false);

/// <summary>
/// Who is trying, as far as counting goes: anyone (the default), the server itself, or a browser that has signed in as
/// this name before (Browser: its id). The last two have counts of their own, so wrong passwords from elsewhere never
/// keep them out.
/// </summary>
public readonly record struct Asker(string? Browser = null, bool Here = false);

/// <summary>
/// Wrong passwords, counted by the name they were typed for, whoever sent them and whether or not the name is an account,
/// so the answer never says which names exist. Each one doubles the wait before the next try, up to five minutes, and
/// enough of them lock the name: an account here until an administrator unlocks it, any other name for a day. A count
/// starts over after a sign-in that went through, or a day without a wrong one. One try per name is checked at a time, so
/// tries sent together can't all get in before the first is counted.
/// Two kinds of try are counted apart, so nobody can be kept out of their own lab by someone hammering their name: tries
/// from the server itself, which never lock, and tries from a browser that has signed in as that name before (it keeps a
/// token in a cookie), which gets ten of its own and is then a stranger again.
/// Kept in signin-locks.json, so a restart or an update forgets nothing.
/// </summary>
public sealed class SignInLocks : IDisposable
{
    public const int Most = 10;
    public const int LongestWait = 300;
    public const int BrowserDays = 180;
    private const int MostOthers = 5000; // names that aren't accounts here: made-up ones can't fill the memory
    private const int MostBrowsers = 10; // remembered for one account; the oldest go
    private const int MostUnlocks = 5; // tries to unlock with a right code and a wrong password, before it's a day between them
    private const string FileName = "signin-locks.json";
    private static readonly TimeSpan Forget = TimeSpan.FromHours(24);
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly DataFolder folder;
    private readonly SettingsStore secrets;
    private readonly TimeProvider clock;
    private readonly Lock gate = new();
    private readonly Dictionary<string, LockEntry> names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KnownBrowser> browsers = new(StringComparer.Ordinal);
    private readonly HashSet<string> trying = new(StringComparer.Ordinal); // names with a try being checked right now
    private readonly string path;
    private JsonFile<LocksFile> file = null!;
    private byte[] secret = [];
    private string kept = ""; // the secret as the file keeps it
    private DateTime stamp; // the file's write time when it was read or saved here
    private DateTime saved;
    private DateTime swept;
    private bool unsaved;
    private ITimer? flush; // writes what's unsaved two seconds on, when nothing else did

    public SignInLocks(DataFolder folder, SettingsStore secrets, TimeProvider clock)
    {
        this.folder = folder;
        this.secrets = secrets;
        this.clock = clock;
        path = Path.Combine(folder.Path, FileName);
        Load();
    }

    /// <summary>The key a name is counted under: AMW\awiles, awiles@amw.com, Awiles and awilés are one name.</summary>
    public static string Key(string source, string name) => $"{(source == SignIn.Directory ? "ad" : "local")}:{Fold(Ldap.Account(name.Trim()))}";

    /// <summary>How long the next try waits after this many wrong ones: 2 seconds, doubling, five minutes at the most.</summary>
    public static int WaitAfter(int wrong) => (int) Math.Min(LongestWait, Math.Pow(2, Math.Clamp(wrong, 1, 9)));

    /// <summary>
    /// Unlocks accounts from outside the running server (--unlock), which sees the file change. The file is changed as it
    /// is, without its secret: only the service's own account can open that. So names that aren't accounts can't be found
    /// here; their locks end by themselves.
    /// </summary>
    public static int UnlockInFile(DataFolder folder, IReadOnlyCollection<string> keys)
    {
        var file = folder.Json<LocksFile>(FileName, Json);
        var f = file.Read();
        var gone = f.Names.RemoveAll(e => e.Locked is not null && keys.Contains(e.Id));
        if (gone > 0)
        {
            file.Save(f);
        }
        return gone;
    }

    /// <summary>Where a name stands now. Begin is the one to ask before a password is checked.</summary>
    public LockAnswer Check(string key, Asker asker = default)
    {
        lock (gate)
        {
            Sync();
            var now = Now;
            if (Find(key, asker) is not { } e || Ended(e, now))
            {
                return default;
            }
            // Never longer than a wait can be, whatever the clock was doing when it was set.
            var left = e.Wait > now ? (int) Math.Min(Math.Ceiling((e.Wait.Value - now).TotalSeconds), e.Locked is null ? LongestWait : Forget.TotalSeconds) : 0;
            return e.Locked is not null ? new(LockState.Locked, left) : left > 0 ? new(LockState.Wait, left) : default;
        }
    }

    /// <summary>
    /// Where a name stands, asked before any password is looked at, and its turn when a try may go ahead: one try for a
    /// name is checked at a time, and the others sent meanwhile are told to wait. Dispose the turn when the try is over.
    /// Null when there's no turn to take: the name is waiting, or another try for it is being checked.
    /// </summary>
    public IDisposable? Begin(string key, Asker asker, out LockAnswer standing)
    {
        lock (gate)
        {
            standing = Check(key, asker);
            if (standing.Seconds > 0)
            {
                return null;
            }
            var id = Mark(Plain(key, asker));
            if (!trying.Add(id))
            {
                standing = new(standing.State == LockState.Locked ? LockState.Locked : LockState.Wait, 1);
                return null;
            }
            return new Turn(this, id);
        }
    }

    /// <summary>
    /// A wrong password or code for this name: the next try waits twice as long, and at most (3, 5 or 10, fewer when the
    /// domain locks sooner) the name locks. known: it's an account here, so it's kept by name and listed. On a locked name
    /// it's a failed try to unlock with the code, and the next waits five minutes; heavy: its code was right, so whoever
    /// sent it has the authenticator, and five of those make it a day.
    /// </summary>
    public LockAnswer Wrong(string key, string? from, Asker asker = default, bool known = false, int most = Most, bool heavy = false)
    {
        lock (gate)
        {
            Sync();
            var now = Now;
            Sweep(now);
            most = Math.Clamp(most, 1, Most);
            var e = Find(key, asker);
            if (e is not null && Ended(e, now))
            {
                names.Remove(e.Id);
                e = null;
            }
            if (e is null || (known && Hashed(e.Id)))
            {
                // New, or an account here that was counted before anyone knew it was one: by name from now on.
                var id = known ? Plain(key, asker) : Hash(key, asker);
                if (e is not null)
                {
                    names.Remove(e.Id);
                }
                else if (!known)
                {
                    Room();
                }
                e = names[id] = new LockEntry { Id = id, Wrong = e?.Wrong ?? 0, Locked = e?.Locked, Wait = e?.Wait, Unlocks = e?.Unlocks ?? 0 };
            }
            e.Wrong = Math.Min(e.Wrong + 1, 1_000_000);
            e.Last = now;
            e.From = from;
            e.Name = Hashed(e.Id) ? "" : key[(key.IndexOf(':') + 1)..];
            if (e.Locked is not null)
            {
                e.Unlocks += heavy ? 1 : 0;
                var until = e.Unlocks >= MostUnlocks ? (int) Forget.TotalSeconds : LongestWait;
                e.Wait = now.AddSeconds(until);
                Save(now, atOnce: heavy);
                return new(LockState.Locked, until);
            }
            if (asker.Browser is { } browser && e.Wrong >= most)
            {
                // A remembered browser that used up its own tries is a stranger from here on, and this one counts as a stranger's.
                names.Remove(e.Id);
                browsers.Remove(browser);
                return Wrong(key, from, default, known, most);
            }
            if (asker == default && e.Wrong >= most)
            {
                e.Locked = now;
                e.Wait = null;
                Save(now, atOnce: true);
                return new(LockState.Locked, 0, Just: true);
            }
            var wait = WaitAfter(e.Wrong);
            e.Wait = now.AddSeconds(wait);
            Save(now, atOnce: false);
            return new(LockState.Wait, wait);
        }
    }

    /// <summary>A sign-in that went all the way through: the name's count starts over. A locked name stays locked.</summary>
    public void Right(string key, Asker asker = default)
    {
        lock (gate)
        {
            Sync();
            if (Find(key, asker) is { Locked: null } e)
            {
                names.Remove(e.Id);
                Save(Now, atOnce: false);
            }
        }
    }

    /// <summary>Unlocks a name, and its count starts over. False when it wasn't locked.</summary>
    public bool Unlock(string key)
    {
        lock (gate)
        {
            Sync();
            if (Find(key, default) is not { Locked: not null } e)
            {
                return false;
            }
            names.Remove(e.Id);
            Save(Now, atOnce: true);
            return true;
        }
    }

    /// <summary>The accounts here that are locked, the latest first, and how many other names are.</summary>
    public (IReadOnlyList<LockEntry> Accounts, int Others) Locked()
    {
        lock (gate)
        {
            Sync();
            var now = Now;
            var locked = names.Values.Where(e => e.Locked is not null && !Ended(e, now)).ToList();
            return ([.. locked.Where(e => !Hashed(e.Id)).OrderByDescending(e => e.Locked).Select(e => new LockEntry
                { Id = e.Id, Name = e.Name, Wrong = e.Wrong, Last = e.Last, From = e.From, Wait = e.Wait, Locked = e.Locked })], locked.Count(e => Hashed(e.Id)));
        }
    }

    /// <summary>
    /// The browser's id when the token in its cookie is one it got by signing in, within the last 180 days: as this
    /// account (key), or as any when no key is given. Null for every other browser.
    /// </summary>
    public string? Browser(string? token, string? key = null)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 100)
        {
            return null;
        }
        lock (gate)
        {
            Sync();
            var id = Mark(token);
            return browsers.TryGetValue(id, out var b) && (key is null || b.Key == key) && Now - b.At < TimeSpan.FromDays(BrowserDays) ? id : null;
        }
    }

    /// <summary>The token for the cookie of a browser that just signed in as this account: the one it has when that's its own, else a new one.</summary>
    public string Remember(string? token, string key)
    {
        lock (gate)
        {
            Sync();
            var now = Now;
            if (!string.IsNullOrEmpty(token) && token.Length <= 100 && browsers.TryGetValue(Mark(token), out var mine) && mine.Key == key)
            {
                mine.At = now;
                Save(now, atOnce: false);
                return token;
            }
            foreach (var old in browsers.Values.Where(b => b.Key == key).OrderByDescending(b => b.At).Skip(MostBrowsers - 1).Select(b => b.Id).ToList())
            {
                browsers.Remove(old);
            }
            var made = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            browsers[Mark(made)] = new KnownBrowser { Id = Mark(made), Key = key, At = now };
            Save(now, atOnce: true);
            return made;
        }
    }

    /// <summary>Forgets the browsers remembered for an account, as when its password changes: each is a stranger again.</summary>
    public void ForgetBrowsers(string key)
    {
        lock (gate)
        {
            Sync();
            var gone = browsers.Values.Where(b => b.Key == key).Select(b => b.Id).ToList();
            foreach (var id in gone)
            {
                browsers.Remove(id);
                names.Remove(Plain(key, new Asker(id)));
            }
            if (gone.Count > 0)
            {
                Save(Now, atOnce: true);
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            flush?.Dispose();
            flush = null;
            Sync(); // something else wrote it since (--unlock): that stands, and what was unsaved here is let go
            if (unsaved)
            {
                Write();
            }
        }
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>A name as the directory compares it, near enough: no case, no accents, wide letters as plain ones.</summary>
    private static string Fold(string name)
    {
        try
        {
            var plain = new StringBuilder(name.Length);
            foreach (var c in name.Normalize(NormalizationForm.FormKD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    plain.Append(c);
                }
            }
            return plain.ToString().ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return name.ToLowerInvariant(); // not text Unicode can fold
        }
    }

    /// <summary>Reads the file. Names that aren't accounts and browser tokens are hashed with its secret; without it they can't be found again and go.</summary>
    private void Load()
    {
        stamp = File.GetLastWriteTimeUtc(path); // taken first: a write that lands meanwhile is noticed next time
        file = folder.Json<LocksFile>(FileName, Json);
        var f = file.Read();
        var opened = f.Secret is null ? null : secrets.TryDecrypt(f.Secret);
        secret = opened is null ? RandomNumberGenerator.GetBytes(32) : Convert.FromBase64String(opened);
        kept = opened is null ? secrets.Encrypt(Convert.ToBase64String(secret)) : f.Secret!;
        names.Clear();
        browsers.Clear();
        foreach (var e in f.Names.Where(e => opened is not null || !Hashed(e.Id)))
        {
            names[e.Id] = e;
        }
        foreach (var b in opened is null ? [] : f.Browsers)
        {
            browsers[b.Id] = b;
        }
        unsaved = false;
    }

    /// <summary>Something else wrote the file (--unlock, on the server): what it holds now is what counts.</summary>
    private void Sync()
    {
        if (File.GetLastWriteTimeUtc(path) != stamp)
        {
            Load();
        }
    }

    // The server itself and each remembered browser have counts of their own; a name that isn't an account is kept as a keyed hash.
    private static string Plain(string key, Asker asker) => asker.Here ? "here|" + key : asker.Browser is { } b ? $"b:{b}|{key}" : key;

    private string Hash(string key, Asker asker) => "#" + Mark(Plain(key, asker));

    private string Mark(string text) => Convert.ToBase64String(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(text)));

    private static bool Hashed(string id) => id.StartsWith('#');

    private LockEntry? Find(string key, Asker asker) =>
        names.TryGetValue(Plain(key, asker), out var e) || names.TryGetValue(Hash(key, asker), out e) ? e : null;

    /// <summary>Whether there's nothing left of it: a count a day old, or the lock of a name that isn't an account a day on.</summary>
    private static bool Ended(LockEntry e, DateTime now) =>
        e.Locked is null ? now - e.Last > Forget : Hashed(e.Id) && now - e.Locked > Forget;

    /// <summary>Once a minute, drops what has ended: old counts, and browsers not seen for 180 days.</summary>
    private void Sweep(DateTime now)
    {
        if (now - swept < TimeSpan.FromMinutes(1))
        {
            return;
        }
        swept = now;
        foreach (var gone in names.Values.Where(e => Ended(e, now)).Select(e => e.Id).ToList())
        {
            names.Remove(gone);
            unsaved = true;
        }
        foreach (var gone in browsers.Values.Where(b => now - b.At >= TimeSpan.FromDays(BrowserDays)).Select(b => b.Id).ToList())
        {
            browsers.Remove(gone);
            unsaved = true;
        }
    }

    /// <summary>Room for one more name that isn't an account: past the most, the oldest hundred go, the locked ones last.</summary>
    private void Room()
    {
        var others = names.Values.Where(e => Hashed(e.Id)).ToList();
        if (others.Count < MostOthers)
        {
            return;
        }
        foreach (var old in others.OrderBy(e => e.Locked is not null).ThenBy(e => e.Last).Take(100))
        {
            names.Remove(old.Id);
        }
    }

    /// <summary>
    /// A lock, an unlock or a new browser is written at once. Counts are written every two seconds at the most: the ones
    /// in between wait for a timer set just for them, or for the server stopping.
    /// </summary>
    private void Save(DateTime now, bool atOnce)
    {
        if (!atOnce && now - saved < SaveEvery)
        {
            unsaved = true;
            flush ??= clock.CreateTimer(_ => Flush(), null, SaveEvery, Timeout.InfiniteTimeSpan);
            return;
        }
        saved = now;
        Write();
    }

    private void Flush()
    {
        lock (gate)
        {
            flush?.Dispose();
            flush = null;
            Sync();
            if (unsaved)
            {
                saved = Now;
                Write();
            }
        }
    }

    private void Write()
    {
        unsaved = false;
        try
        {
            file.Save(new LocksFile { Secret = kept, Names = [.. names.Values], Browsers = [.. browsers.Values] });
            stamp = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            unsaved = true; // the next change tries again; what's in memory still counts
        }
    }

    /// <summary>A name's turn to have a password checked, given back when the try is over.</summary>
    private sealed class Turn(SignInLocks owner, string id) : IDisposable
    {
        public void Dispose()
        {
            lock (owner.gate)
            {
                owner.trying.Remove(id);
            }
        }
    }
}
