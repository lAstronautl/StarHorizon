using Content.Shared.Procedural;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Salvage.Expeditions.Modifiers; // Horizon
using Content.Shared.Dataset;
using Robust.Shared.Prototypes;
using Content.Server.Salvage.Expeditions; // Frontier
using Content.Server._Horizon.SpaceHazards; // Horizon
using Content.Shared._Horizon.SpaceHazards; // Horizon
using Content.Shared.Popups; // Frontier

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    public static readonly EntProtoId CoordinatesDisk = "CoordinatesDisk";
    public static readonly ProtoId<LocalizedDatasetPrototype> PlanetNames = "NamesBorer";

    [Dependency] private readonly SharedPopupSystem _popupSystem = default!; // Frontier
    [Dependency] private readonly SalvageSystem _salvage = default!; // Frontier

    // Консоль экспедиций информационная: экспедиция запускается сбросом дроп-пода на планету.

    private void OnSalvageConsoleInit(Entity<SalvageExpeditionConsoleComponent> console, ref ComponentInit args)
    {
        UpdateConsole(console);
    }

    private void OnSalvageConsoleParent(Entity<SalvageExpeditionConsoleComponent> console, ref EntParentChangedMessage args)
    {
        UpdateConsole(console);
    }

    private void OnSalvageConsoleOpened(Entity<SalvageExpeditionConsoleComponent> console, ref BoundUIOpenedEvent args)
    {
        UpdateConsole(console);
    }

    private void UpdateConsole(Entity<SalvageExpeditionConsoleComponent> console)
    {
        _ui.SetUiState(console.Owner, SalvageConsoleUiKey.Expedition, BuildConsoleState());
    }

    /// <summary>Экспедиция каждой планеты: задание, статус и когда закроется. Одинакова для всех консолей.</summary>
    public SalvageExpeditionConsoleState BuildConsoleState()
    {
        return new SalvageExpeditionConsoleState(GetPlanetInfos());
    }

    /// <summary>Все планеты с их экспедициями (для консоли экспедиций и консоли дроп-подов), по названию.</summary>
    public List<PlanetExpeditionInfo> GetPlanetInfos()
    {
        var result = new List<PlanetExpeditionInfo>();
        var query = EntityQueryEnumerator<ExpeditionPlanetComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var planet, out var meta))
        {
            if (EnsurePlanetMission((uid, planet)) is not { } mission)
                continue;

            result.Add(new PlanetExpeditionInfo(GetNetEntity(uid), meta.EntityName, planet.Status, mission, planet.EndTime));
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    /// <summary>A biome with dedicated difficulties (Ardonium wastes: Inferno) only offers those, every other biome offers the rest.</summary>
    private bool BiomeFitsDifficulty(string biomeId, string difficultyId)
    {
        if (_prototypeManager.TryIndex<SalvageBiomeModPrototype>(biomeId, out var biome) && biome.Difficulties is { Count: > 0 })
            return biome.Difficulties.Contains(difficultyId);

        foreach (var other in _prototypeManager.EnumeratePrototypes<SalvageBiomeModPrototype>())
        {
            if (other.Difficulties != null && other.Difficulties.Contains(difficultyId))
                return false;
        }

        return true;
    }

    /// <summary>Отправляет всем открытым консолям экспедиций свежее состояние (статус планеты изменился).</summary>
    public void RefreshConsoles()
    {
        var state = BuildConsoleState();
        var query = AllEntityQuery<SalvageExpeditionConsoleComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out _, out var uiComp))
        {
            _ui.SetUiState((uid, uiComp), SalvageConsoleUiKey.Expedition, state);
        }
    }
}
