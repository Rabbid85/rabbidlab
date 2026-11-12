namespace RabbidLab.Server;

/// <summary>Named groups in an order someone chose, for Links and Sensors. Names match whatever their capitals.</summary>
public static class Grouping
{
    public const int Most = 100;
    public static readonly StringComparer Same = StringComparer.OrdinalIgnoreCase;

    /// <summary>The groups in order: the saved ones, then any still in use that an older file never listed.</summary>
    public static List<string> Known(IEnumerable<string>? saved, IEnumerable<string?> used)
    {
        var known = new List<string>(saved ?? []);
        foreach (var g in used.OfType<string>().OrderBy(g => g, Same))
        {
            if (!known.Contains(g, Same))
            {
                known.Add(g);
            }
        }
        return known;
    }

    public static string? Clean(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    /// <summary>Adds a group at the end; the answer is why it can't be.</summary>
    public static string? Add(List<string> known, string? name)
    {
        if (Clean(name) is not { Length: <= 40 } group)
        {
            return "Give the group a name of 40 characters at most.";
        }
        if (known.Contains(group, Same))
        {
            return $"There's already a group called {group}.";
        }
        if (known.Count >= Most)
        {
            return $"That's {Most} groups, which is as many as {Product.Title} keeps.";
        }
        known.Add(group);
        return null;
    }

    /// <summary>Renames a group where it stands. Old is the name it had, for moving what was in it.</summary>
    public static string? Rename(List<string> known, string? name, string? to, out string old)
    {
        old = "";
        if (Clean(to) is not { Length: <= 40 } next)
        {
            return "Give the group a name of 40 characters at most.";
        }
        var at = known.FindIndex(g => Same.Equals(g, Clean(name)));
        if (at < 0)
        {
            return "That group isn't here any more.";
        }
        if (known.Where((_, i) => i != at).Contains(next, Same))
        {
            return $"There's already a group called {next}.";
        }
        old = known[at];
        known[at] = next;
        return null;
    }

    /// <summary>Takes a group off the list; false when it isn't there. Old is the name as it was spelled.</summary>
    public static bool Remove(List<string> known, string? name, out string old)
    {
        var at = known.FindIndex(g => Same.Equals(g, Clean(name)));
        old = at < 0 ? "" : known[at];
        if (at >= 0)
        {
            known.RemoveAt(at);
        }
        return at >= 0;
    }

    /// <summary>The groups in a new order. One left out keeps its place after the ones named.</summary>
    public static List<string> Order(List<string> known, IReadOnlyList<string>? names)
    {
        var next = (names ?? []).Select(n => known.FirstOrDefault(g => Same.Equals(g, Clean(n)))).OfType<string>().Distinct(Same).ToList();
        next.AddRange(known.Where(g => !next.Contains(g, Same)));
        return next;
    }

    /// <summary>The group a new item names: the one that's there in its own spelling, or a new one added at the end.</summary>
    public static (string? Group, string? Problem) Join(List<string> known, string? name)
    {
        if (Clean(name) is not { } group)
        {
            return (null, null);
        }
        if (known.FirstOrDefault(g => Same.Equals(g, group)) is { } spelled)
        {
            return (spelled, null);
        }
        return Add(known, group) is { } problem ? (null, problem) : (group, null);
    }
}

/// <summary>A change to the groups: one to make or delete (Name), to rename (Name to To), or all of them in a new order (Names).</summary>
public sealed record GroupForm(string? Name, string? To, List<string>? Names);
