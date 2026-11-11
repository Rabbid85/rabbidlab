using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RabbidLab.Updater;

namespace RabbidLab.Tests;

/// <summary>The release key (Release.cs) and what a signature covers: Setup (SetupUpdate.cs) must trust exactly the same key.</summary>
public sealed class ReleaseKeyTests
{
    /// <summary>The repository's root: the folder with Installer and Src in it, above the test's build output.</summary>
    public static string Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Installer", "Setup", "SetupUpdate.cs")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        return root.FullName;
    }

    [Fact]
    public void Setup_and_the_update_engine_trust_the_same_release_key()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "Installer", "Setup", "SetupUpdate.cs"));
        var xml = Regex.Match(source, "<RSAKeyValue>.*?</RSAKeyValue>").Value;
        Assert.NotEmpty(xml);
        using var setup = RSA.Create();
        setup.FromXmlString(xml);
        using var engine = RSA.Create();
        engine.ImportFromPem(Release.ReleaseKey);
        Assert.Equal(engine.ExportSubjectPublicKeyInfo(), setup.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void A_signature_covers_the_product_the_version_the_installer_s_hash_and_its_size()
    {
        Assert.Equal("Rabbid Lab release\n1.2.3\nab12\n42", Encoding.UTF8.GetString(Release.Message("1.2.3", "AB12", 42)));
    }

    [Fact]
    public void Only_a_signature_made_with_the_release_key_verifies()
    {
        using var other = RSA.Create(2048);
        var signature = Convert.ToBase64String(other.SignData(Release.Message("1.2.3", "ab12", 42), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.True(Release.Verify(other.ExportSubjectPublicKeyInfoPem(), "1.2.3", "ab12", 42, signature));
        Assert.False(Release.Verify(Release.ReleaseKey, "1.2.3", "ab12", 42, signature));
        Assert.False(Release.Verify(Release.ReleaseKey, "1.2.3", "ab12", 42, null));
        Assert.False(Release.Verify(Release.ReleaseKey, "1.2.3", "ab12", 42, "not base64"));
    }
}
