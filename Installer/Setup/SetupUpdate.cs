using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RabbidLab.Setup
{
    /// <summary>
    /// Setup installing the newest Rabbid Lab, not just its own version. Before it installs, it reads the newest release's
    /// manifest on GitHub (Edition.Updates: release.json, which Installer\publish.sh puts in every release), as Rabbid Lab
    /// itself does (Updates.cs), and checks Rabbid Lab's release signature on the installer it names. When that's newer
    /// than this Setup, it downloads it from GitHub, makes sure it's exactly the file the signed manifest described, and
    /// runs it to do the installation instead, with the same choices. The newer Setup is told not to look again
    /// (/noupdatecheck). GitHub is asked nothing but for the files: no token, no name, nothing about this computer.
    /// </summary>
    public sealed class SetupUpdate
    {
        /// <summary>
        /// Rabbid Lab's release key (Release.ReleaseKey in the update engine; a test checks the two match), in the form
        /// .NET Framework reads. A newer Setup is used only when its release signature verifies against it, so GitHub
        /// alone can't make Setup run anything.
        /// </summary>
        public const string ReleaseKey = "<RSAKeyValue><Modulus>1ZEOG02KEfROevDV2KTCg32ipp/XU6HOeb4J1Q5CN92cQ4kt5v3nkcC/SMnI2Su/A6eHoD4behtrvHg8h4ixfPglN7IF62AyITgA9WbSrLXdwV5VuytSNoUkwMjllx94keCsTsYcGOR94PMilYEIvxVzu7CrTOTF0au0iwjyC8oW6vixKIIi0IFmt7a+Rrn3hWWDfQdqZW1qZZP4DgaGlh659xf23SxT/qZKMdjYiBNBUmp+PkUKjtaZVwCXjTKdYu0MmUnF/D9p7TFTFOWpko5Tuw8+jsgx7KzUcLpcCwyOwWPRlj5daNmCAY39zYF8ul7XJKqnOPO0J42eolCWrp06ppgVem23bT6b7NiRGoAvJ5iZIbbPNz3bYBeB6EZsDlK5Ev1prxVSa6gpy0wSTsVEL1DQGHCnH7HjBrfRyq9rPCjk8Ag4TqmV6LfnFt1F3Z7TqlZN6aWItqVBOO8hqCXcEUpGT81AbtQsUvMJABpRyZBBMAkM0NDlFXfgYNPB</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        /// <summary>
        /// Whether a release signature is genuine for this edition, version and installer (the message is as in Release.cs).
        /// The edition's name is in it, so a Rabbid Lab Dev build never passes for a release, or the other way round.
        /// </summary>
        public static bool ReleaseSigned(string version, string sha256, long size, string signature)
        {
            if (string.IsNullOrEmpty(signature))
            {
                return false;
            }
            try
            {
                var message = Encoding.UTF8.GetBytes(Edition.Name + " release\n" + version + "\n" + sha256.ToLowerInvariant() + "\n" + size.ToString(CultureInfo.InvariantCulture));
                using (var rsa = new RSACryptoServiceProvider { PersistKeyInCsp = false })
                {
                    rsa.FromXmlString(ReleaseKey);
                    return rsa.VerifyData(message, Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (Exception e) when (e is FormatException || e is CryptographicException)
            {
                return false;
            }
        }

        /// <summary>The command-line switch that tells a Setup not to look for a newer version (it is the newer one).</summary>
        public const string NoCheck = "noupdatecheck";

        public string Version = "";
        public long Size;
        public string Sha256 = "";
        public string Url = "";

        /// <summary>Newer than this Setup.</summary>
        public bool IsNewer => System.Version.TryParse(Version, out var v) && v > System.Version.Parse(Engine.Version);

        /// <summary>The newest version GitHub has, or null when it couldn't be asked or the manifest didn't check out.</summary>
        public static SetupUpdate Latest()
        {
            try
            {
                using (var response = Get(Edition.Updates, 15000))
                using (var stream = response.GetResponseStream())
                {
                    var manifest = Json(ReadAll(stream));
                    var windows = manifest.Element("windows");
                    if (windows == null || (string) windows.Attribute("type") != "object")
                    {
                        return null;
                    }
                    var offer = new SetupUpdate
                    {
                        Version = (string) windows.Element("version") ?? (string) manifest.Element("version") ?? "",
                        Size = (long?) windows.Element("size") ?? 0,
                        Sha256 = ((string) windows.Element("sha256") ?? "").ToLowerInvariant(),
                        Url = (string) windows.Element("url") ?? "",
                    };
                    var released = ReleaseSigned(offer.Version, offer.Sha256, offer.Size, (string) windows.Element("signature") ?? "");
                    return released && offer.Sha256.Length == 64 && offer.Size > 0 && offer.Url.StartsWith(Edition.Downloads, StringComparison.OrdinalIgnoreCase) ? offer : null;
                }
            }
            catch (Exception e) when (e is WebException || e is IOException || e is XmlException || e is FormatException || e is CryptographicException || e is InvalidOperationException)
            {
                (e as WebException)?.Response?.Dispose(); // an HTTP error's answer: let go, not left holding a connection
                return null;
            }
        }

        /// <summary>
        /// Downloads the newer Setup into its own new folder and returns its path. Throws, with the reason, when GitHub
        /// won't hand it over or what arrived isn't exactly the file the signed manifest described.
        /// </summary>
        public string Download()
        {
            var folder = Path.Combine(Path.GetTempPath(), "RabbidLab-Setup-" + Version + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "RabbidLab-Setup-" + Version + ".exe");
            try
            {
                using (var response = Get(Url, 600000))
                using (var stream = response.GetResponseStream())
                using (var file = File.Create(path))
                using (var sha = SHA256.Create())
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        file.Write(buffer, 0, read);
                        sha.TransformBlock(buffer, 0, read, null, 0);
                        total += read;
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    var hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                    if (total != Size || hash != Sha256)
                    {
                        throw new InvalidDataException("the downloaded file does not match the published release (size or SHA-256 hash mismatch)");
                    }
                }
                return path;
            }
            catch (WebException e) when (e.Response is HttpWebResponse refused)
            {
                Remove(folder);
                var status = (int) refused.StatusCode;
                refused.Dispose();
                throw new InvalidOperationException($"GitHub returned HTTP status {status}");
            }
            catch (Exception)
            {
                Remove(folder);
                throw;
            }
        }

        /// <summary>
        /// Runs the newer Setup with this one's command line, plus $extra, and the answer file this one found next to
        /// itself, which goes next to the new one where it looks.
        /// </summary>
        public static Process Run(string setup, IEnumerable<string> args, Choices choices, params string[] extra)
        {
            if (choices.ConfigFile != null && !choices.ConfigNamed)
            {
                File.Copy(choices.ConfigFile, Path.Combine(Path.GetDirectoryName(setup), "RabbidLab.xml"), true);
            }
            var line = string.Join(" ", args.Where(a => Choices.SplitArg(a).Name != NoCheck).Concat(extra).Concat(new[] { "/" + NoCheck }).Select(Arg));
            return Process.Start(new ProcessStartInfo(setup, line) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory });
        }

        /// <summary>One argument for a Windows command line: quoted when it has spaces or quotes, so it arrives as it was.</summary>
        public static string Arg(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return arg;
            }
            var quoted = new StringBuilder("\"");
            var slashes = 0;
            foreach (var ch in arg)
            {
                if (ch == '\\')
                {
                    slashes++;
                    continue;
                }
                quoted.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes).Append(ch);
                slashes = 0;
            }
            return quoted.Append('\\', slashes * 2).Append('"').ToString();
        }

        /// <summary>A file from GitHub (it sends downloads on to its file store; the redirect is followed).</summary>
        private static HttpWebResponse Get(string url, int timeout)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest) WebRequest.Create(url);
            request.UserAgent = "RabbidLab-Setup/" + Engine.Version;
            request.Timeout = 15000;
            request.ReadWriteTimeout = timeout;
            return (HttpWebResponse) request.GetResponse();
        }

        /// <summary>JSON as XML (.NET Framework's own JSON reader): an object's fields are child elements.</summary>
        private static XElement Json(byte[] json)
        {
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(json, XmlDictionaryReaderQuotas.Max))
            {
                return XElement.Load(reader);
            }
        }

        private static byte[] ReadAll(Stream stream)
        {
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }

        private static void Remove(string folder)
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (Exception)
            {
                // Only a temporary folder; Windows clears those.
            }
        }
    }
}
