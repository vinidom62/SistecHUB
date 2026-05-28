using SistecHub.Core;
using SistecHub.UI;

namespace SistecHub;

static class Program
{
    [STAThread]
    static void Main()
    {
        if (!SingleInstanceApp.TryEnterFirstInstance())
        {
            SingleInstanceApp.TryActivateExisting();
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();

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
