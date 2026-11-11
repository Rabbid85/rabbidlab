using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace RabbidLab.Setup
{
    /// <summary>
    /// What's new, for the side of the window when updating: the console's own release notes (RELEASE_NOTES in console.js,
    /// inside payload.zip), for the versions after the one installed, newest first, each note's headline. The same
    /// words the console's What's new shows, so the two never differ. Empty when there are none (a build without the
    /// console inside, or the same version again).
    /// </summary>
    internal static class WhatsNew
    {
        public static List<string> Since(string installed, int most = 4)
        {
            var notes = new List<string>();
            try
            {
                using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
                {
                    if (payload == null)
                    {
                        return notes;
                    }
                    using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
                    {
                        var entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith("wwwroot/Assets/console.js", StringComparison.OrdinalIgnoreCase));
                        if (entry == null)
                        {
                            return notes;
                        }
                        using (var reader = new StreamReader(entry.Open()))
                        {
                            notes.AddRange(Parse(reader.ReadToEnd(), installed));
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException)
            {
                return notes;
            }
            return notes.Take(most).ToList();
        }

        /// <summary>Each note of every version newer than installed, its first sentence, newest version first.</summary>
        public static IEnumerable<string> Parse(string script, string installed)
        {
            var start = script.IndexOf("const RELEASE_NOTES = [", StringComparison.Ordinal);
            if (start < 0)
            {
                yield break;
            }
            Version.TryParse(installed ?? "", out var from);
            foreach (Match version in VersionBlock.Matches(script, start))
            {
                if (!Version.TryParse(version.Groups[1].Value, out var v))
                {
                    continue;
                }
                if (from != null && v <= from)
                {
                    yield break; // newest first: everything after this is older still
                }
                foreach (Match note in Note.Matches(version.Groups[2].Value))
                {
                    yield return FirstSentence(note.Groups[1].Value.Replace("\\'", "'"));
                }
                if (from == null)
                {
                    yield break; // no version to compare with: the newest's notes only
                }
            }
        }

        /// <summary>A note's headline: its first sentence, or what comes before its colon when that's sooner ("Sync with Microsoft 365: …").</summary>
        private static string FirstSentence(string text)
        {
            var end = text.IndexOf(". ", StringComparison.Ordinal);
            var colon = text.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 20 && (end < 0 || colon < end))
            {
                return text.Substring(0, colon) + ".";
            }
            return end > 0 ? text.Substring(0, end + 1) : text;
        }

        private static readonly Regex VersionBlock = new Regex(@"\['(\d+\.\d+\.\d+)', \[(.*?)\]\],", RegexOptions.Singleline);
        private static readonly Regex Note = new Regex(@"'((?:[^'\\]|\\.)*)'", RegexOptions.Singleline);
    }
}
