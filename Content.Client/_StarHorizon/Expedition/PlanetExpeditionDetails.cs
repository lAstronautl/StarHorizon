using Content.Client.Salvage;
using Content.Shared.Procedural;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Salvage.Expeditions.Modifiers;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._StarHorizon.Expedition;

/// <summary>
/// Параметры экспедиции планеты для консолей: биом (растительность), враги, модификаторы (атмосфера, температура,
/// освещение), рекомендуемое число утилизаторов. Считаются так же, как на сервере (по заданию и сиду).
/// </summary>
public static class PlanetExpeditionDetails
{
    public readonly record struct Line(string Title, string Value);

    public static List<Line> Get(PlanetExpeditionInfo info)
    {
        var lines = new List<Line>();
        var proto = IoCManager.Resolve<IPrototypeManager>();
        var salvage = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SalvageSystem>();

        if (!proto.TryIndex<SalvageDifficultyPrototype>(info.Mission.Difficulty, out var difficulty))
            return lines;

        SalvageMission mission;
        try
        {
            mission = salvage.GetMission(info.Mission.MissionType, difficulty, info.Mission.Seed, info.Mission.Biome);
        }
        catch (InvalidOperationException)
        {
            return lines;
        }

        if (proto.TryIndex<SalvageBiomeModPrototype>(mission.Biome, out var biome))
            lines.Add(new Line(Loc.GetString("salvage-expedition-window-biome"), Describe(biome.Description, biome.ID)));

        if (proto.TryIndex<SalvageFactionPrototype>(mission.Faction, out var faction))
            lines.Add(new Line(Loc.GetString("salvage-expedition-window-hostiles"), Describe(faction.Description, faction.ID)));

        if (mission.Modifiers.Count > 0)
            lines.Add(new Line(Loc.GetString("salvage-expedition-window-modifiers"), string.Join(", ", mission.Modifiers)));

        lines.Add(new Line(Loc.GetString("salvage-expedition-difficulty-players"),
            info.Mission.Difficulty == "Inferno"
                ? Loc.GetString("salvage-expedition-difficulty-players-error")
                : difficulty.RecommendedPlayers.ToString()));

        return lines;
    }

    private static string Describe(string description, string fallback)
    {
        var text = Loc.GetString(description);
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }
}
