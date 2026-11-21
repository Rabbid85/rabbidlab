namespace RabbidLab.Server;

/// <summary>
/// The stopper for a script hammering the sign-in: 120 sign-in requests a minute from one address (an IPv6 /64 as one),
/// and past that they are turned away before anything is read, looked up or written. An address that keeps at it, ten
/// times over in its minute, is turned away for fifteen minutes. Requests from the server itself aren't counted.
/// </summary>
public sealed class SignInFlood(TimeProvider clock)
{
    public const int PerMinute = 120;
    private const int Stubborn = PerMinute * 10;
    private const int MostAddresses = 20000; // past this many at once a new address goes uncounted, rather than kept out
    private static readonly TimeSpan Out = TimeSpan.FromMinutes(15);
    private readonly Lock gate = new();
    private readonly Dictionary<string, Entry> seen = [];
    private long swept;

    private sealed class Entry
    {
        public long Minute;
        public int Count;
        public DateTimeOffset Until; // turned away until then
    }

    /// <summary>Whether this request may go on. justOut: this is the one that got its address turned away for fifteen minutes.</summary>
    public bool Let(string? address, out bool justOut)
    {
        justOut = false;
        var now = clock.GetUtcNow();
        var minute = now.ToUnixTimeSeconds() / 60;
        lock (gate)
        {
            var key = Addresses.Network(address);
            if (!seen.TryGetValue(key, out var e))
            {
                Sweep(now, minute);
                if (seen.Count >= MostAddresses)
                {
                    return true;
                }
                seen[key] = e = new Entry();
            }
            if (now < e.Until)
            {
                return false;
            }
            if (e.Minute != minute)
            {
                e.Minute = minute;
                e.Count = 0;
            }
            e.Count++;
            if (e.Count <= PerMinute)
            {
                return true;
            }
            if (e.Count == Stubborn)
            {
                e.Until = now + Out;
                justOut = true;
            }
            return false;
        }
    }

    /// <summary>Once a minute, forgets the addresses whose minute is over and that aren't turned away.</summary>
    private void Sweep(DateTimeOffset now, long minute)
    {
        if (swept == minute)
        {
            return;
        }
        swept = minute;
        foreach (var gone in seen.Where(s => s.Value.Minute < minute && now >= s.Value.Until).Select(s => s.Key).ToList())
        {
            seen.Remove(gone);
        }
    }
}
