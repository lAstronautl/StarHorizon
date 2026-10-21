using Content.Shared._Horizon.SlimeAppearance;
using Robust.Client.UserInterface;

namespace Content.Client._Horizon.SlimeAppearance;

public sealed class SlimeAppearanceBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private SlimeAppearanceWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<SlimeAppearanceWindow>();
        _window.OnApply += (markings, skin, eyes) => SendMessage(new SlimeAppearanceApplyMessage(markings, skin, eyes));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is SlimeAppearanceBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }
}
