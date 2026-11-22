#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Xml;
using System.Xml.Linq;

namespace RabbidLab.Setup
{
    /// <summary>
    /// What removing the server means for the computers with its client. A client can only be uninstalled when its
    /// server allows it, so with the server and its data gone it would be stuck. Setup takes the client off this
    /// computer when it's connected to the server being removed, and says how many other computers still have it.
    /// </summary>
    public static class ClientPlan
    {
        /// <summary>The days without the server after which a client's lock lets go, unless the server's settings say otherwise.</summary>
        public const int UsualDays = 30;

        /// <summary>Whether a client set to this address is connected to the server being removed: one of its ports, on this computer.</summary>
        public static bool Ours(string server, ICollection<int> ports, Func<string, bool> here)
        {
            return Uri.TryCreate(server, UriKind.Absolute, out var at) && (at.Scheme == Uri.UriSchemeHttps || at.Scheme == Uri.UriSchemeHttp)
                && ports.Contains(at.Port) && here(at.DnsSafeHost);
        }

        /// <summary>
        /// From the server's clients.json: how many computers other than this one have the client, and after how many
        /// days without the server their lock lets go (0 for never). No file, or one that can't be read, is no computers.
        /// </summary>
        public static (int Others, int Days) Others(byte[] clientsJson, string thisComputer)
        {
            if (clientsJson == null || clientsJson.Length == 0)
            {
                return (0, UsualDays);
            }
            try
            {
                // Past a byte order mark, which the reader doesn't take.
                var skip = clientsJson.Length >= 3 && clientsJson[0] == 0xEF && clientsJson[1] == 0xBB && clientsJson[2] == 0xBF ? 3 : 0;
                XElement root;
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(clientsJson, skip, clientsJson.Length - skip, XmlDictionaryReaderQuotas.Max))
                {
                    root = XElement.Load(reader);
                }
                var others = root.Element("clients")?.Elements()
                    .Count(c => !string.Equals((string) c.Element("computer"), thisComputer, StringComparison.OrdinalIgnoreCase)) ?? 0;
                var days = int.TryParse((string) root.Element("unlockAfterDays"), out var said) && said >= 0 ? said : UsualDays;
                return (others, days);
            }
            catch (Exception e) when (e is XmlException || e is FormatException || e is InvalidOperationException)
            {
                return (0, UsualDays);
            }
        }

        /// <summary>The Remove page's note about the other computers; null when there are none.</summary>
        public static string OthersNote(int others, int days)
        {
            if (others <= 0)
            {
                return null;
            }
            var what = others == 1 ? "The Rabbid Lab client is installed on 1 other computer" : $"The Rabbid Lab client is installed on {others} other computers";
            var each = others == 1 ? "that computer" : "each computer";
            const string script = "After this server is removed, the client can be removed only with Remove-RabbidLabClient.ps1 (in the Tools folder of the Rabbid Lab GitHub repository)";
            var after = days > 0
                ? $"{script}, or uninstalled after the uninstall lock expires {days} days later."
                : $"{script}. This server is configured to never release the uninstall lock.";
            return $"{what}, and uninstalling the client requires approval from this server. Before you remove all data, uninstall the client from {each}: "
                + $"in Rabbid Lab, go to Devices, select the computer, and then select Uninstall now. {after}";
        }
    }
}
