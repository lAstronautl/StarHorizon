using Content.Server.Administration;
using Content.Server.Salvage.Expeditions;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Отладка: принудительно спавнит волну врагов на карте планетной экспедиции (мобы падают в дроп-подах у места, где
/// начал первый игрок). Без аргумента: карта, на которой стоит вызвавший команду, а если его там нет, все идущие экспедиции.
/// Волна по обычным правилам: тратит 40-60% mobBudget сложности, нужен хотя бы один живой игрок на карте.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class ExpeditionWaveCommand : LocalizedEntityCommands
{
    [Dependency] private readonly ExpeditionWaveSystem _waves = default!;

    public override string Command => "expeditionwave";
    public override string Description => "Принудительно спавнит волну врагов на карте планетной экспедиции.";
    public override string Help => $"{Command} [uid карты экспедиции]";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var maps = new List<EntityUid>();
        if (args.Length >= 1)
        {
            if (!NetEntity.TryParse(args[0], out var net) || !EntityManager.TryGetEntity(net, out var uid))
            {
                shell.WriteError(Loc.GetString("shell-invalid-entity-uid", ("uid", args[0])));
                return;
            }

            maps.Add(uid.Value);
        }
        else if (shell.Player?.AttachedEntity is { } player
                 && EntityManager.TryGetComponent(player, out TransformComponent? xform)
                 && xform.MapUid is { } here
                 && EntityManager.HasComponent<SalvageExpeditionComponent>(here))
        {
            maps.Add(here);
        }
        else
        {
            var query = EntityManager.AllEntityQueryEnumerator<SalvageExpeditionComponent>();
            while (query.MoveNext(out var uid, out var expedition))
            {
                if (expedition.Planet.IsValid())
                    maps.Add(uid);
            }
        }

        if (maps.Count == 0)
        {
            shell.WriteLine("Идущих планетных экспедиций нет.");
            return;
        }

        foreach (var map in maps)
        {
            if (EntityManager.GetComponent<MetaDataComponent>(map).EntityPaused)
            {
                shell.WriteLine($"{map}: карта на паузе (на неё ещё не прилетела капсула), волна не выйдет.");
                continue;
            }

            var spawned = _waves.ForceWave(map);
            shell.WriteLine(spawned switch
            {
                < 0 => $"{map}: это не карта планетной экспедиции.",
                0 => $"{map}: волна не вышла (нет живых игроков на карте или нечего покупать).",
                _ => $"{map}: волна упала, мобов: {spawned}.",
            });
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHint("uid карты экспедиции (по умолчанию ваша)")
            : CompletionResult.Empty;
    }
}
