namespace SistecHub.Core;

/// <summary>Falha ao registar ou gerir o serviço Windows (ex.: UAC recusado).</summary>
public sealed class WindowsServiceSetupFailedException : Exception
{
    public WindowsServiceSetupFailedException(string message, bool userCancelledElevation = false, Exception? inner = null)
        : base(message, inner)
    {
        UserCancelledElevation = userCancelledElevation;
    }

    public bool UserCancelledElevation { get; }
}
