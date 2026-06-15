namespace SistecHub.Core;

/// <summary>Detecta instalações em Program Files (MSI per-machine).</summary>
internal static class PerMachineUpdatePermissions
{
    internal static bool IsPerMachinePath(string path) =>
        path.Contains(@"\Program Files\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\Program Files (x86)\", StringComparison.OrdinalIgnoreCase);
}
