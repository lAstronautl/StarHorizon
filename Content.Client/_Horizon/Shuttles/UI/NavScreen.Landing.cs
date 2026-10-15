using Content.Shared._Horizon.Shuttles.Components;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    /// <summary>Raised with true for take off and false for landing.</summary>
    public event Action<bool>? OnLandingRequested;

    private void HorizonLandingInitialize()
    {
        LandButton.OnPressed += _ => OnLandingRequested?.Invoke(false);
        TakeOffButton.OnPressed += _ => OnLandingRequested?.Invoke(true);
    }

    private void HorizonLandingUpdate()
    {
        if (_shuttleEntity is not { } shuttle || !_entManager.TryGetComponent(shuttle, out LandableShuttleComponent? landable))
        {
            LandingButtons.Visible = false;
            return;
        }

        LandingButtons.Visible = true;
        LandButton.Disabled = landable.State == LandState.Landed;
        TakeOffButton.Disabled = landable.State == LandState.Flying;
    }
}
