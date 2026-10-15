namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>Raised with true for take off and false for landing.</summary>
    public event Action<bool>? OnLandingRequested;
}
