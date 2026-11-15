using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using RabbidLab.Shared;

namespace RabbidLab.Client;

/// <summary>
/// WMI reads for Devices, as SYSTEM: a query in a namespace on this computer, and the namespaces and classes there to
/// pick from. Only reads: WQL can't change anything, and the namespace has to be this computer's own (WmiAsk).
/// </summary>
internal static class Wmi
{
    private const int MaxRows = 5000;
    private const int MaxChars = 1_000_000; // about 1 MB of text in one answer
    private const int MaxCell = 4000;
    private const int MaxClasses = 6000;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    public static bool Is(AccountOrder order) => order.Action == AccountOrder.Wmi || order.Action == AccountOrder.WmiClasses;

    public static AccountAnswer Do(AccountOrder order)
    {
        var space = WmiAsk.Space(order.Space);
        var bad = WmiAsk.BadSpace(order.Space) ?? (order.Action == AccountOrder.Wmi ? WmiAsk.BadQuery(order.Query) : null);
        if (bad is not null)
        {
            return AccountAnswer.Failed(order.Id, bad);
        }
        try
        {
            var scope = new ManagementScope(space, new ConnectionOptions { Timeout = Limit });
            scope.Connect();
            return order.Action == AccountOrder.WmiClasses ? Classes(scope, space) : Query(scope, order.Query!.Trim());
        }
        catch (ManagementException e) when (e.ErrorCode == ManagementStatus.Timedout && order.Action == AccountOrder.WmiClasses)
        {
            // The first list after Windows starts can take most of a minute; the next one takes a moment.
            return AccountAnswer.Failed(order.Id, "Windows is still putting its class list together. Try again in a minute.");
        }
        catch (ManagementException e)
        {
            return AccountAnswer.Failed(order.Id, Said(e, space));
        }
        catch (UnauthorizedAccessException)
        {
            return AccountAnswer.Failed(order.Id, "Windows refused to read that, even as SYSTEM.");
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException)
        {
            return AccountAnswer.Failed(order.Id, $"WMI said: {Clean(e.Message)}");
        }
    }

    private static AccountAnswer Query(ManagementScope scope, string text)
    {
        var options = new EnumerationOptions { Timeout = Limit, ReturnImmediately = true, Rewindable = false, BlockSize = 200 };
        var found = new List<Dictionary<string, string>>();
        var kinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seen = new List<string>();
        var chars = 0;
        var more = false;
        using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(text), options))
        using (var results = searcher.Get())
        {
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    if (found.Count >= MaxRows || chars >= MaxChars)
                    {
                        more = true;
                        break;
                    }
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in item.Properties)
                    {
                        if (!kinds.ContainsKey(p.Name))
                        {
                            kinds[p.Name] = "";
                            seen.Add(p.Name);
                        }
                        if (p.Value is null)
                        {
                            continue;
                        }
                        var value = Text(p);
                        if (kinds[p.Name].Length == 0)
                        {
                            kinds[p.Name] = Kind(p, value);
                        }
                        row[p.Name] = value;
                        chars += value.Length;
                    }
                    found.Add(row);
                }
            }
        }
        // The columns in the order the query named them, then the rest as WMI gave them.
        var asked = Asked(text);
        var columns = asked.Where(a => seen.Contains(a, StringComparer.OrdinalIgnoreCase)).Select(a => seen.First(s => string.Equals(s, a, StringComparison.OrdinalIgnoreCase)))
            .Concat(seen.Where(s => !asked.Contains(s, StringComparer.OrdinalIgnoreCase))).ToList();
        return new AccountAnswer
        {
            Ok = true,
            Columns = columns,
            Kinds = columns.Select(c => kinds[c].Length == 0 ? "text" : kinds[c]).ToList(),
            Rows = found.Select(r => columns.Select(c => r.TryGetValue(c, out var v) ? v : "").ToList()).ToList(),
            More = more,
        };
    }

    // The properties named between SELECT and FROM, when it names them.
    private static List<string> Asked(string text)
    {
        var m = Regex.Match(text, @"^\s*select\s+(.+?)\s+from\s", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return !m.Success ? [] : m.Groups[1].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0 && s != "*").ToList();
    }

    private static AccountAnswer Classes(ManagementScope scope, string space)
    {
        var spaces = new List<string>();
        var options = new EnumerationOptions { Timeout = Limit, ReturnImmediately = true, Rewindable = false };
        using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name FROM __NAMESPACE"), options))
        using (var results = searcher.Get())
        {
            foreach (ManagementBaseObject n in results)
            {
                using (n)
                {
                    if (n["Name"] is string name)
                    {
                        spaces.Add($@"{space}\{name}");
                    }
                }
            }
        }
        var classes = new List<string>();
        var deep = new EnumerationOptions { Timeout = Limit, ReturnImmediately = true, Rewindable = false, EnumerateDeep = true, BlockSize = 500 };
        using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM meta_class"), deep))
        using (var results = searcher.Get())
        {
            foreach (ManagementBaseObject c in results)
            {
                using (c)
                {
                    // __ classes are WMI's own workings.
                    if (c["__CLASS"] is string name && !name.StartsWith("__", StringComparison.Ordinal))
                    {
                        classes.Add(name);
                    }
                }
                if (classes.Count >= MaxClasses)
                {
                    break;
                }
            }
        }
        spaces.Sort(StringComparer.OrdinalIgnoreCase);
        classes.Sort(StringComparer.OrdinalIgnoreCase);
        return new AccountAnswer { Ok = true, Spaces = spaces, Classes = classes };
    }

    // A value as text: dates read as dates, lists joined, and long ones cut short.
    private static string Text(PropertyData p)
    {
        var v = p.Value;
        if (p.IsArray && v is Array list)
        {
            if (v is byte[] bytes)
            {
                return bytes.Length <= 32 ? BitConverter.ToString(bytes) : $"({bytes.Length} bytes)";
            }
            if (v is ushort[] numbers && Letters(numbers) is { } word)
            {
                return word;
            }
            var parts = new List<string>();
            foreach (var x in list)
            {
                parts.Add(One(p.Type, x));
            }
            return Cut(string.Join(", ", parts));
        }
        return Cut(One(p.Type, v));
    }

    private static string One(CimType type, object? v)
    {
        switch (v)
        {
            case null:
                return "";
            case ManagementBaseObject inner:
                using (inner)
                {
                    return $"({inner.ClassPath?.ClassName ?? "object"})";
                }
            case string s when type == CimType.DateTime:
                return When(s);
            case bool b:
                return b ? "True" : "False";
            case IFormattable f:
                return f.ToString(null, CultureInfo.InvariantCulture);
            default:
                return v.ToString() ?? "";
        }
    }

    // A DMTF date (20261114183000.000000-300) as 2026-11-14 18:30:00 in this computer's time; an interval as d.hh:mm:ss.
    private static string When(string dmtf)
    {
        try
        {
            if (Interval(dmtf))
            {
                return ManagementDateTimeConverter.ToTimeSpan(dmtf).ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture);
            }
            return ManagementDateTimeConverter.ToDateTime(dmtf).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return dmtf;
        }
    }

    private static bool Interval(string dmtf) => dmtf.Length >= 25 && dmtf[21] == ':';

    private static string Kind(PropertyData p, string value)
    {
        if (p.IsArray)
        {
            return "text";
        }
        switch (p.Type)
        {
            case CimType.SInt8 or CimType.UInt8 or CimType.SInt16 or CimType.UInt16 or CimType.SInt32 or CimType.UInt32 or CimType.SInt64 or CimType.UInt64
                or CimType.Real32 or CimType.Real64:
                return "number";
            case CimType.DateTime:
                return p.Value is string s && !Interval(s) && value != s ? "date" : "text";
            case CimType.Boolean:
                return "yes";
            default:
                return "text";
        }
    }

    // Monitor names and serial numbers (WmiMonitorID) come as numbers that are letters: those read as the text they are.
    private static string? Letters(ushort[] values)
    {
        if (values.Length < 8)
        {
            return null;
        }
        var text = new StringBuilder();
        var ended = false;
        foreach (var v in values)
        {
            if (v == 0)
            {
                ended = true;
                continue;
            }
            if (ended || v < 32 || v > 126)
            {
                return null;
            }
            text.Append((char) v);
        }
        return text.Length > 0 ? text.ToString() : null;
    }

    private static string Cut(string s) => s.Length > MaxCell ? s.Substring(0, MaxCell) + "…" : s;

    // The page puts the computer's name in front of these.
    private static string Said(ManagementException e, string space) => e.ErrorCode switch
    {
        ManagementStatus.InvalidNamespace => $"There's no namespace {space} on it.",
        ManagementStatus.InvalidClass or ManagementStatus.NotFound => $"There's no class like that in {space}.",
        ManagementStatus.InvalidQuery or ManagementStatus.InvalidQueryType => $"WMI couldn't read that query: check the class and property names. ({Clean(e.Message)})",
        ManagementStatus.AccessDenied => "WMI refused to read that, even as SYSTEM.",
        ManagementStatus.Timedout => "It took longer than 30 seconds. Ask for less, with a WHERE.",
        _ => $"WMI said: {Clean(e.Message)}",
    };

    private static string Clean(string message) => message.Trim().TrimEnd('.') + ".";
}
