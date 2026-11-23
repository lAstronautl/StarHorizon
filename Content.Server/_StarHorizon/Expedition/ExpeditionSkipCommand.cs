using System.Linq;
using Content.Server.Administration;
using Content.Server.Salvage.Expeditions;
using Content.Shared._Horizon.SpaceHazards;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Отладка: мотает время планетной экспедиции так, чтобы до конца оставалось 5 минут (или заданное число минут).
/// Без аргумента: экспедиция, на карте которой стоит вызвавший команду, а если его там нет, все идущие экспедиции.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class ExpeditionSkipCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override string Command => "expeditionskip";
    public override string Description => "Мотает время планетной экспедиции: до конца остаётся 5 минут (или указанное число минут).";
    public override string Help => $"{Command} [минут=5] [uid карты экспедиции]";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var minutes = 5f;
        if (args.Length >= 1
            && (!float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out minutes) || minutes <= 0))
        {
            shell.WriteError(Loc.GetString("shell-argument-must-be-number"));
            return;
        }

        EntityUid? only = null;
        if (args.Length >= 2)
        {
            if (!NetEntity.TryParse(args[1], out var net) || !EntityManager.TryGetEntity(net, out var uid))
            {
                shell.WriteError(Loc.GetString("shell-invalid-entity-uid", ("uid", args[1])));
                return;
            }

            only = uid;
        }
        else if (shell.Player?.AttachedEntity is { } player
                 && EntityManager.TryGetComponent(player, out TransformComponent? xform)
                 && xform.MapUid is { } map
                 && EntityManager.HasComponent<SalvageExpeditionComponent>(map))
        {
            only = map;
        }

        var target = _timing.CurTime + TimeSpan.FromMinutes(minutes);
        var count = 0;
        var query = EntityManager.AllEntityQueryEnumerator<SalvageExpeditionComponent>();
        while (query.MoveNext(out var uid, out var expedition))
        {
            if (!expedition.Planet.IsValid() || only != null && only != uid)
                continue;

            // Только вперёд: если до конца уже меньше, ничего не меняем.
            if (expedition.EndTime <= target)
            {
                shell.WriteLine($"{uid}: до конца уже {(expedition.EndTime - _timing.CurTime).TotalMinutes:F1} мин., не трогаю.");
                continue;
            }

            expedition.EndTime = target;
            if (EntityManager.TryGetComponent(expedition.Planet, out ExpeditionPlanetComponent? planet))
                planet.EndTime = target;

            shell.WriteLine($"{uid}: до конца экспедиции {minutes:F1} мин.");
            count++;
        }

        if (count == 0 && only == null)
            shell.WriteLine("Идущих планетных экспедиций нет.");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint("минут (по умолчанию 5)"),
            2 => CompletionResult.FromHint("uid карты экспедиции (по умолчанию ваша)"),
            _ => CompletionResult.Empty,
        };
    }
}
