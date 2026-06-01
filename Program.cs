using SistecHub.Core;
using SistecHub.UI;
using Velopack;

namespace SistecHub;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        VelopackApp.Build()
            .SetArgs(args)
            .SetAutoApplyOnStartup(false)
            .OnAfterInstallFastCallback(_ => WindowsStartupRegistration.EnsureRegistered())
            .Run();

        if (!SingleInstanceApp.TryEnterFirstInstance())
        {
            SingleInstanceApp.TryActivateExisting();
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            WindowsStartupRegistration.EnsureRegistered();

            if (!AppSettingsStore.IsInitialSetupComplete())
            {
                using var setup = new InitialSetupForm();
                if (setup.ShowDialog() != DialogResult.OK)
                    return;
            }

            Application.Run(new MainForm());
        }
        finally
        {
            SingleInstanceApp.ReleaseFirstInstance();
        }
    }
}
