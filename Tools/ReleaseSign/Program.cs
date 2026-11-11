// The Rabbid Lab release key: signs each release so that only the holder of the key can publish a version that installs
// will accept. GitHub only hosts the files; Rabbid Lab and its Setup refuse any update whose signature doesn't verify
// against the public key built into them (Release.cs, SetupUpdate.cs), so a hijacked GitHub account still can't push
// code to anyone. The private key never leaves the publisher's PC: it is kept encrypted with Windows DPAPI for the
// publisher's own Windows account, so a copy of the file is no use elsewhere.
//
//   ReleaseSign init                                   create the key (once); prints the public key to build in
//   ReleaseSign public                                 print the public key
//   ReleaseSign sign <installer> <version> [product]   print the release signature for an installer (base64)
//   ReleaseSign verify <installer> <version> <sig> [product]
//                                                      check a signature against the key built into Rabbid Lab
//   ReleaseSign export <file>                          write a backup of the key, encrypted with a passphrase
//   ReleaseSign import <file>                          restore the key from a backup on a new PC or Windows account;
//                                                      where the key is already present, check the backup instead
// export and import ask for the passphrase (or take it from the RABBIDLAB_RELEASE_PASSPHRASE environment variable).
// product is "Rabbid Lab" unless given. Installer/dev.sh gives "Rabbid Lab Dev", so neither edition takes the other's builds.
//
// What's signed (and verified, the same way, in Release.cs and SetupUpdate.cs):
//   "{product} release\n{version}\n{sha256 of the installer, lowercase hex}\n{size in bytes}"
using System.Security.Cryptography;
using System.Text;
using RabbidLab.Updater;

const string release = "Rabbid Lab";
var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RabbidLab");
var keyFile = Path.Combine(folder, "release-signing.key");
var entropy = Encoding.UTF8.GetBytes("Rabbid Lab release signing key");

RSA Load()
{
    if (!File.Exists(keyFile))
    {
        throw new InvalidOperationException($"There is no release key at {keyFile}. Restore it from its backup with 'ReleaseSign import <file>'.");
    }
    var pkcs8 = ProtectedData.Unprotect(File.ReadAllBytes(keyFile), entropy, DataProtectionScope.CurrentUser);
    var rsa = RSA.Create();
    rsa.ImportPkcs8PrivateKey(pkcs8, out _);
    CryptographicOperations.ZeroMemory(pkcs8);
    return rsa;
}

static string Message(string file, string version, string product)
{
    using var stream = File.OpenRead(file);
    var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
    return $"{product} release\n{version}\n{hash}\n{new FileInfo(file).Length}";
}

static void PrintPublic(RSA rsa)
{
    Console.WriteLine(rsa.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine(rsa.ToXmlString(false));
}

// Whether a key is the one Rabbid Lab and Setup trust.
static bool Trusted(RSA rsa)
{
    using var builtIn = RSA.Create();
    builtIn.ImportFromPem(Release.ReleaseKey);
    return builtIn.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(rsa.ExportSubjectPublicKeyInfo());
}

// The passphrase of a backup: from RABBIDLAB_RELEASE_PASSPHRASE, or typed without being shown.
static string Passphrase(bool confirm)
{
    var set = Environment.GetEnvironmentVariable("RABBIDLAB_RELEASE_PASSPHRASE");
    if (!string.IsNullOrEmpty(set))
    {
        return set;
    }
    static string Ask(string prompt)
    {
        Console.Error.Write(prompt);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? "";
        }
        var typed = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return typed.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                typed.Length = Math.Max(0, typed.Length - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }
    }
    var passphrase = Ask("Passphrase: ");
    if (confirm && Ask("Type the passphrase again: ") != passphrase)
    {
        throw new InvalidOperationException("The passphrases do not match. Nothing was written.");
    }
    return passphrase;
}

try
{
    switch (args.FirstOrDefault())
    {
        case "init":
        {
            if (File.Exists(keyFile))
            {
                Console.Error.WriteLine($"A release key already exists at {keyFile}. It is not replaced: installs trust only that key.");
                return 1;
            }
            // A new key is trusted by nothing already installed, so it would cut every install off from updates.
            if (args.Length < 2 || args[1] != "--replace-trusted-key")
            {
                Console.Error.WriteLine("Rabbid Lab already trusts a release key. On a new PC, restore it with 'ReleaseSign import <file>'. To start over with a new key (every install must then be reinstalled by hand), run 'ReleaseSign init --replace-trusted-key' and build the public key it prints into Release.cs and SetupUpdate.cs.");
                return 1;
            }
            Directory.CreateDirectory(folder);
            using var rsa = RSA.Create(3072);
            var pkcs8 = rsa.ExportPkcs8PrivateKey();
            File.WriteAllBytes(keyFile, ProtectedData.Protect(pkcs8, entropy, DataProtectionScope.CurrentUser));
            CryptographicOperations.ZeroMemory(pkcs8);
            Console.WriteLine($"Created the release key at {keyFile} (encrypted for this Windows account). Public key:");
            PrintPublic(rsa);
            return 0;
        }
        case "public":
        {
            using var rsa = Load();
            PrintPublic(rsa);
            return 0;
        }
        case "sign" when args.Length is 3 or 4:
        {
            using var rsa = Load();
            var message = Message(args[1], args[2], args.Length == 4 ? args[3] : release);
            var signature = rsa.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Console.WriteLine(Convert.ToBase64String(signature));
            return 0;
        }
        case "verify" when args.Length is 4 or 5:
        {
            // Against the public key built into Rabbid Lab: what installs will accept, whatever key this PC has.
            using var builtIn = RSA.Create();
            builtIn.ImportFromPem(Release.ReleaseKey);
            var message = Message(args[1], args[2], args.Length == 5 ? args[4] : release);
            var ok = builtIn.VerifyData(Encoding.UTF8.GetBytes(message), Convert.FromBase64String(args[3]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Console.WriteLine(ok ? "The signature is valid." : "The signature is NOT valid.");
            return ok ? 0 : 1;
        }
        case "export" when args.Length == 2:
        {
            using var rsa = Load();
            var passphrase = Passphrase(confirm: true);
            if (passphrase.Length < 16)
            {
                Console.Error.WriteLine("Use a passphrase of at least 16 characters. Nothing was written.");
                return 1;
            }
            var pem = rsa.ExportEncryptedPkcs8PrivateKeyPem(passphrase, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));
            File.WriteAllText(args[1], pem);
            Console.WriteLine($"Wrote an encrypted backup of the release key to {args[1]}.");
            Console.WriteLine("Store it offline, and the passphrase separately. To check the backup, run: ReleaseSign import " + args[1]);
            return 0;
        }
        case "import" when args.Length == 2:
        {
            using var restored = RSA.Create();
            try
            {
                restored.ImportFromEncryptedPem(File.ReadAllText(args[1]), Passphrase(confirm: false));
            }
            catch (Exception e) when (e is CryptographicException or ArgumentException)
            {
                Console.Error.WriteLine("The passphrase is not correct, or the file is not a release key backup. Nothing was changed.");
                return 1;
            }
            if (!Trusted(restored))
            {
                Console.Error.WriteLine("This backup is not the release key that Rabbid Lab trusts. Nothing was changed.");
                return 1;
            }
            if (File.Exists(keyFile))
            {
                Console.WriteLine("The backup is good: it opens with this passphrase and holds the release key that Rabbid Lab trusts. The key on this PC was left as it is.");
                return 0;
            }
            Directory.CreateDirectory(folder);
            var pkcs8 = restored.ExportPkcs8PrivateKey();
            File.WriteAllBytes(keyFile, ProtectedData.Protect(pkcs8, entropy, DataProtectionScope.CurrentUser));
            CryptographicOperations.ZeroMemory(pkcs8);
            Console.WriteLine($"Restored the release key to {keyFile} (encrypted for this Windows account). Releases can be published from this PC.");
            return 0;
        }
        default:
            Console.Error.WriteLine("Usage: ReleaseSign init | public | sign <installer> <version> [product] | verify <installer> <version> <signature> [product] | export <file> | import <file>");
            return 2;
    }
}
catch (Exception e) when (e is CryptographicException or IOException or InvalidOperationException or FormatException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
