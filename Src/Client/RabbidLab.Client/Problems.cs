using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace RabbidLab.Client;

/// <summary>An error the client ran into: where (update, windows-update, inventory, connection, orders, tray), what, when last, how often.</summary>
[DataContract]
internal sealed class ProblemReport
{
    [DataMember(Name = "area")] public string Area { get; set; } = "";
    [DataMember(Name = "message")] public string Message { get; set; } = "";

    /// <summary>When it last happened, as ISO 8601.</summary>
    [DataMember(Name = "at")] public string At { get; set; } = "";
    [DataMember(Name = "count")] public int Count { get; set; } = 1;
}

/// <summary>
/// The errors waiting to go to the server with the next check-in (Agent.cs). Kept on disk, so ones from before a
/// restart (a client update that didn't install, say) still go. The same error again counts up instead of adding a line.
/// Once the server has them they're gone from here: the server keeps them.
/// </summary>
internal static class Problems
{
    private const int Most = 20;
    private const int MostText = 300;
    private static readonly object Gate = new();
    private static string FilePath => Folder.Kept("problems.json");

    public static void Note(string area, string message)
    {
        try
        {
            lock (Gate)
            {
                var list = Load();
                var text = message.Trim();
                text = text.Length > MostText ? text.Substring(0, MostText) : text;
                var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                var same = list.FirstOrDefault(p => p.Area == area && p.Message == text);
                if (same is not null)
                {
                    same.Count++;
                    same.At = now;
                }
                else
                {
                    list.Add(new ProblemReport { Area = area, Message = text, At = now });
                    if (list.Count > Most)
                    {
                        list.RemoveAt(0);
                    }
                }
                Save(list);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SerializationException)
        {
            // Not kept, then: the error itself is what matters, and it'll likely happen again.
        }
    }

    /// <summary>The ones to send now; null when there are none.</summary>
    public static List<ProblemReport>? Waiting()
    {
        lock (Gate)
        {
            var list = Load();
            return list.Count == 0 ? null : list.Select(p => new ProblemReport { Area = p.Area, Message = p.Message, At = p.At, Count = p.Count }).ToList();
        }
    }

    /// <summary>The server has these. One that happened again meanwhile stays, with only the new times counted.</summary>
    public static void Sent(List<ProblemReport> sent)
    {
        try
        {
            lock (Gate)
            {
                var list = Load();
                foreach (var s in sent)
                {
                    var here = list.FirstOrDefault(p => p.Area == s.Area && p.Message == s.Message);
                    if (here is null)
                    {
                        continue;
                    }
                    if (here.At == s.At || here.Count <= s.Count)
                    {
                        list.Remove(here);
                    }
                    else
                    {
                        here.Count -= s.Count;
                    }
                }
                Save(list);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SerializationException)
        {
            // Sent again next round; the server counts them up.
        }
    }

    private static List<ProblemReport> Load()
    {
        try
        {
            using var file = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return (List<ProblemReport>?) new DataContractJsonSerializer(typeof(List<ProblemReport>)).ReadObject(file) ?? new List<ProblemReport>();
        }
        catch (Exception e) when (e is IOException or SerializationException or UnauthorizedAccessException)
        {
            return new List<ProblemReport>();
        }
    }

    private static void Save(List<ProblemReport> list)
    {
        if (list.Count == 0)
        {
            File.Delete(FilePath);
            return;
        }
        var temp = FilePath + ".tmp";
        using (var file = File.Create(temp))
        {
            new DataContractJsonSerializer(typeof(List<ProblemReport>)).WriteObject(file, list);
        }
        if (File.Exists(FilePath))
        {
            File.Replace(temp, FilePath, null);
        }
        else
        {
            File.Move(temp, FilePath);
        }
    }
}
