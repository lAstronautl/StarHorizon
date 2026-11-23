using System.Linq;
using Content.Server.Administration;
using Content.Server.Salvage.Expeditions;
using Content.Shared._Horizon.SpaceHazards;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Отладка: двигает время планетной экспедиции. <c>5</c> ставит остаток 5 минут, <c>+2</c> прибавляет 2 минуты к остатку,
/// <c>-3</c> убавляет 3 минуты (до 0: экспедиция закрывается). Без аргументов ставит остаток 5 минут.
/// Без uid: экспедиция, на карте которой стоит вызвавший команду, а если его там нет, все идущие экспедиции.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class ExpeditionSkipCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override string Command => "expeditionskip";
    public override string Description => "Двигает время планетной экспедиции: N ставит остаток N минут, +N прибавляет, -N убавляет.";
    public override string Help => $"{Command} [N | +N | -N] [uid карты экспедиции]   (минуты, по умолчанию 5)";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var mode = Mode.Set;
        var minutes = 5f;
        if (args.Length >= 1)
        {
            var text = args[0];
            if (text.StartsWith('+'))
                mode = Mode.Add;
            else if (text.StartsWith('-'))
                mode = Mode.Subtract;

            var number = mode == Mode.Set ? text : text[1..];
            if (!float.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out minutes)
                || minutes < 0)
            {
                shell.WriteError(Loc.GetString("shell-argument-must-be-number"));
                return;
            }
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

        var delta = TimeSpan.FromMinutes(minutes);
        var now = _timing.CurTime;
        var count = 0;
        var query = EntityManager.AllEntityQueryEnumerator<SalvageExpeditionComponent>();
        while (query.MoveNext(out var uid, out var expedition))
        {
            if (!expedition.Planet.IsValid() || only != null && only != uid)
                continue;

            var target = mode switch
            {
                Mode.Add => expedition.EndTime + delta,
                Mode.Subtract => expedition.EndTime - delta,
                _ => now + delta,
            };

            // Не раньше «сейчас»: экспедиция закрывается, а не уходит в прошлое.
            if (target < now)
                target = now;

            expedition.EndTime = target;
            if (EntityManager.TryGetComponent(expedition.Planet, out ExpeditionPlanetComponent? planet))
                planet.EndTime = target;

            shell.WriteLine($"{uid}: до конца экспедиции {(target - now).TotalMinutes:F1} мин.");
            count++;
        }

        if (count == 0)
            shell.WriteLine("Идущих планетных экспедиций нет.");
    }

    private enum Mode : byte
    {
        Set,
        Add,
        Subtract,
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint("N: остаток N минут, +N: прибавить, -N: убавить (по умолчанию 5)"),
            2 => CompletionResult.FromHint("uid карты экспедиции (по умолчанию ваша)"),
            _ => CompletionResult.Empty,
        };
    }
}
