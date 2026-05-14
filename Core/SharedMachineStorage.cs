using System.Security.AccessControl;
using System.Security.Principal;

namespace SistecHub.Core;

/// <summary>Pasta partilhada por todos os utilizadores da máquina (<c>ProgramData</c>).</summary>
internal static class SharedMachineStorage
{
    public static string RootPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SistecHub");

    public static string LegacyUserDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SistecHub");

    public static void EnsureDirectory()
    {
        System.IO.Directory.CreateDirectory(RootPath);
        GrantAuthenticatedUsersModifyAccess(RootPath);
    }

    static void GrantAuthenticatedUsersModifyAccess(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            var security = dir.GetAccessControl();
            var rule = new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                FileSystemRights.Modify | FileSystemRights.Read | FileSystemRights.Write,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);
            security.ModifyAccessRule(AccessControlModification.Add, rule, out _);
            dir.SetAccessControl(security);
        }
        catch
        {
            // Melhor esforço: sem permissão para alterar ACL, mantém o fluxo normal.
        }
    }
}
