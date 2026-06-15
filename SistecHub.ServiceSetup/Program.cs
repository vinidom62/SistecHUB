using SistecHub.Core;

namespace SistecHub.ServiceSetup;

static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
            return PrintUsage();

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "install" => RunInstall(args),
                "ensure-after-update" => RunEnsureAfterUpdate(args),
                "uninstall" => ServiceSetupCommands.Uninstall(),
                _ => PrintUsage(),
            };
        }
        catch (Exception ex)
        {
            ServiceLogWriter.LogException("Setup", ex, "Comando falhou.");
            return 1;
        }
    }

    static int RunInstall(string[] args)
    {
        var serviceExe = ReadRequiredOption(args, "--service-exe");
        return serviceExe is null ? PrintUsage() : ServiceSetupCommands.Install(serviceExe);
    }

    static int RunEnsureAfterUpdate(string[] args)
    {
        var serviceExe = ReadRequiredOption(args, "--service-exe");
        return serviceExe is null ? PrintUsage() : ServiceSetupCommands.EnsureAfterUpdate(serviceExe);
    }

    static string? ReadRequiredOption(string[] args, string optionName)
    {
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
                return args[i + 1].Trim('"');
        }

        return null;
    }

    static int PrintUsage()
    {
        ServiceLogWriter.Warn("Setup", "Uso inválido. Comandos: install, ensure-after-update, uninstall");
        return 64;
    }
}
