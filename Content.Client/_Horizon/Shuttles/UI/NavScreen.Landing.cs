using Content.Shared.Shuttles.BUIStates;

namespace Content.Client.Shuttles.UI;

// Landing pads: the "Land" / "Take off" button of the shuttle console.
public sealed partial class NavScreen
{
    /// <summary>
    /// Raised when the pilot asks to land (true) or to take off (false).
    /// </summary>
    public event Action<bool>? OnLandingRequest;

    private bool _landed;

    private void HorizonInitialize()
    {
        LandingToggle.OnPressed += _ => OnLandingRequest?.Invoke(!_landed);
    }

    private void HorizonUpdateState(NavInterfaceState state)
    {
        _landed = state.Landed;
        LandingToggle.Text = Loc.GetString(_landed
            ? "shuttle-console-landing-takeoff"
            : "shuttle-console-landing-land");
    }
}
