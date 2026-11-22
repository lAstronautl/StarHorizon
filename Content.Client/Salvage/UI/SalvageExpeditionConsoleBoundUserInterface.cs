using Content.Client._NF.Salvage.UI; // Frontier
using Content.Shared.Salvage.Expeditions;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Salvage.UI;

/// <summary>Консоль экспедиций: показывает экспедицию каждой планеты. Запускает экспедицию дроп-под, а не консоль.</summary>
[UsedImplicitly]
public sealed partial class SalvageExpeditionConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SalvageExpeditionWindow? _window;

    public SalvageExpeditionConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<SalvageExpeditionWindow>();
        _window.Title = Loc.GetString("salvage-expedition-window-title");
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SalvageExpeditionConsoleState current || _window == null)
            return;

        _window.SetPlanets(current.Planets);
    }
}
