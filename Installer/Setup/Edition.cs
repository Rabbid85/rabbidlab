using System;
using System.IO;

namespace RabbidLab.Setup
{
    /// <summary>
    /// Which Rabbid Lab this Setup installs: the release, or the dev build (-p:Dev=true, made by Installer\build.ps1 -Dev).
    /// Rabbid Lab Dev installs beside the release on the same computer, with its own service, program and data folders,
    /// port, Start menu folder, Apps entry, firewall rule and certificate, and looks exactly like the release but for a DEV
    /// tag beside its name (DevLook) and a DEV band on its icon. It never takes port 80. Rabbid Lab is free and open
    /// source: its Setup has no license key and no license agreement.
    /// </summary>
    public static class Edition
    {
#if DEV
        public static readonly bool Dev = true;
        public const string Name = "Rabbid Lab Dev";
        /// <summary>The service's name, and the name of its entry in Settings > Apps.</summary>
        public const string Id = "RabbidLabDev";
        public const int DefaultPort = 8421;
        /// <summary>Without "Rabbid Lab" in it: the release looks for its certificate by a subject containing that.</summary>
        public const string CertSubject = "CN=RabbidLab-Dev";
        /// <summary>The newest dev build's manifest: the dev-latest pre-release's release.json (Installer\dev.sh).</summary>
        public const string Updates = Downloads + "dev-latest/release.json";
#else
        public static readonly bool Dev = false;
        public const string Name = "Rabbid Lab";
        public const string Id = "RabbidLab";
        public const int DefaultPort = 8420;
        public const string CertSubject = "CN=Rabbid Lab";
        /// <summary>The newest release's manifest: GitHub's own link to the newest release's files (Installer\publish.sh).</summary>
        public const string Updates = Repository + "/releases/latest/download/release.json";
#endif

        /// <summary>
        /// What an install whose saved answers don't say how it's reached was set up with (Choices.ReadInstalled). Every
        /// Rabbid Lab Setup saves it, so this only matters for an install made by hand: HTTPS, Setup's default.
        /// </summary>
        public const bool EarlierHttps = true;

        /// <summary>What to do once a new installation is done, on Setup's last page.</summary>
        public const string FirstSignIn = "Open Rabbid Lab to finish setting it up: create an administrator account, or connect it to Active Directory.";

        /// <summary>
        /// Rabbid Lab's repository on GitHub, whose releases are where updates come from, as Rabbid Lab itself reads them
        /// (Updates.cs): Setup installs the newest release when it's newer than itself, downloaded from GitHub only
        /// (SetupUpdate), and only when it carries Rabbid Lab's release signature.
        /// </summary>
        public const string Repository = "https://github.com/Rabbid85/rabbidlab";

        /// <summary>Where every release's files are: only a download under here is ever fetched.</summary>
        public const string Downloads = Repository + "/releases/download/";

        public static readonly string ProgramDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Name);
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Name);
        public static readonly string StartMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Name);
        public static readonly string DesktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), Name + ".url");
    }
}
