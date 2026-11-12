using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>This computer's own Windows accounts: .\name sign-ins, as Windows itself takes them, and the user tools' changes.</summary>
public interface IWindowsAccounts
{
    /// <summary>The account's name when the password is right and it's in this computer's Administrators group, otherwise null.</summary>
    Task<string?> Administrator(string name, string password, CancellationToken stop);

    /// <summary>Lists or changes this computer's accounts (WindowsUsers, the same code the client runs).</summary>
    Task<AccountAnswer> Do(AccountOrder order, CancellationToken stop);
}

public sealed class WindowsAccounts : IWindowsAccounts
{
    public Task<string?> Administrator(string name, string password, CancellationToken stop) =>
        name.Length == 0 || password.Length == 0 ? Task.FromResult<string?>(null)
        : Task.Run(() => OperatingSystem.IsWindows() ? Check(name, password) : null, stop);

    public Task<AccountAnswer> Do(AccountOrder order, CancellationToken stop) =>
        Task.Run(() => OperatingSystem.IsWindows() ? WindowsUsers.Do(order) : AccountAnswer.Failed(order.Id, $"{Product.Title} isn't running on Windows here, so this server has no Windows accounts."), stop);

    [SupportedOSPlatform("windows")]
    private static string? Check(string name, string password)
    {
        try
        {
            using var machine = new PrincipalContext(ContextType.Machine);
            if (!machine.ValidateCredentials(name, password))
            {
                return null;
            }
            using var user = UserPrincipal.FindByIdentity(machine, IdentityType.SamAccountName, name);
            using var admins = GroupPrincipal.FindByIdentity(machine, IdentityType.Sid, "S-1-5-32-544"); // Administrators, in any language
            return user is not null && admins is not null && user.IsMemberOf(admins) ? user.SamAccountName : null;
        }
        catch (Exception e) when (e is PrincipalException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
