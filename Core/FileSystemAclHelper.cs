using System.Security.AccessControl;
using System.Security.Principal;

namespace SistecHub.Core;

/// <summary>Concede permissões de escrita a utilizadores autenticados (sem ser administrador).</summary>
internal static class FileSystemAclHelper
{
    public static void GrantAuthenticatedUsersModifyAccess(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists)
                dir.Create();

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
