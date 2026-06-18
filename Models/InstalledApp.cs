namespace Veil.Models;

public sealed class InstalledApp
{
    public string DisplayName { get; set; } = "";
    public string ExecutableName { get; set; } = "";
    public string Path { get; set; } = "";

    public override string ToString() =>
        string.IsNullOrWhiteSpace(ExecutableName) ? DisplayName : $"{DisplayName} ({ExecutableName})";
}
