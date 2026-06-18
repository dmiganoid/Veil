namespace Veil.Models;

public enum SetupStep
{
    Idle,
    Connecting,
    CheckingSystem,
    Installing,
    ConfiguringServer,
    ObtainingCertificate,
    StartingService,
    Verifying,
    Completed,
    Failed
}

public static class SetupStepExtensions
{
    public static string DisplayText(this SetupStep step) => step switch
    {
        SetupStep.Idle => "Ready to install",
        SetupStep.Connecting => "Connecting via SSH...",
        SetupStep.CheckingSystem => "Checking system...",
        SetupStep.Installing => "Installing Veil...",
        SetupStep.ConfiguringServer => "Configuring server...",
        SetupStep.ObtainingCertificate => "Obtaining certificate...",
        SetupStep.StartingService => "Starting service...",
        SetupStep.Verifying => "Verifying...",
        SetupStep.Completed => "Installation complete",
        SetupStep.Failed => "Installation failed",
        _ => "Unknown"
    };

    public static int StepIndex(this SetupStep step) => step switch
    {
        SetupStep.Connecting => 0,
        SetupStep.CheckingSystem => 1,
        SetupStep.Installing => 2,
        SetupStep.ConfiguringServer => 3,
        SetupStep.ObtainingCertificate => 4,
        SetupStep.StartingService => 5,
        SetupStep.Verifying => 6,
        SetupStep.Completed => 7,
        _ => -1
    };
}
