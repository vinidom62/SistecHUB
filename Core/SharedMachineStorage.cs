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
        FileSystemAclHelper.GrantAuthenticatedUsersModifyAccess(RootPath);
    }
}
