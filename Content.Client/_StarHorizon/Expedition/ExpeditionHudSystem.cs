using System.Numerics;
using Content.Shared._StarHorizon.DropPod;
using Content.Shared._StarHorizon.Expedition;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Client._StarHorizon.Expedition;

/// <summary>Что сейчас показывает таймер интерфейса экспедиции.</summary>
public enum ExpeditionHudPhase : byte
{
    /// <summary>Шаттл ещё не вызван: время до эвакуации.</summary>
    Countdown,
    /// <summary>Шаттл вызван и летит: время до вылета.</summary>
    ShuttleIncoming,
    /// <summary>Шаттл сел: время до вылета, расстояние и направление к нему.</summary>
    Return,
    /// <summary>Игрок на борту шаттла.</summary>
    Aboard,
    /// <summary>Время вышло, шаттл улетел.</summary>
    Departed,
}

/// <summary>Строка задачи интерфейса.</summary>
public readonly record struct ExpeditionHudObjectiveLine(string Text, int Done, int Total, bool Finished);

/// <summary>Всё, что рисует интерфейс экспедиции в этот кадр.</summary>
public sealed class ExpeditionHudView
{
    public ExpeditionHudPhase Phase;
    public TimeSpan Timer;
    public readonly List<ExpeditionHudObjectiveLine> Objectives = new();
    public bool Completed;
    public string? WaveLine;
    /// <summary>До шаттла (мировые координаты, от игрока), когда он сел.</summary>
    public Vector2? ToShuttle;
}

/// <summary>
/// Интерфейс планетной экспедиции в стиле Deep Rock Galactic: слева сверху задачи с прогрессом и волна врагов,
/// по центру сверху время до эвакуации и вылета шаттла, расстояние и стрелка к севшему шаттлу. Виден, пока игрок на
/// карте экспедиции (не в капсуле во время посадки).
/// </summary>
public sealed class ExpeditionHudSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private ExpeditionHudOverlay? _overlay;
    private readonly ExpeditionHudView _view = new();

    public override void Initialize()
    {
        _overlay = new ExpeditionHudOverlay(this);
        _overlays.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        if (_overlay != null)
            _overlays.RemoveOverlay(_overlay);
    }

    /// <summary>Собирает то, что сейчас надо показать. false: интерфейс не виден.</summary>
    public bool TryGetView(out ExpeditionHudView view)
    {
        view = _view;
        if (_player.LocalEntity is not { } player
            || !TryComp(player, out TransformComponent? xform)
            || xform.MapUid is not { } map
            || !TryComp<ExpeditionHudComponent>(map, out var hud))
        {
            return false;
        }

        // Пока игрок в капсуле (посадка, катсцена), интерфейса нет.
        if (_container.TryGetContainingContainer((player, null, null), out var container)
            && HasComp<DropPodComponent>(container.Owner))
        {
            return false;
        }

        var now = _timing.CurTime;
        view.Objectives.Clear();
        foreach (var objective in hud.Objectives)
        {
            var key = objective.Kind switch
            {
                ExpeditionObjectiveKind.Destroy => "expedition-hud-objective-destroy",
                ExpeditionObjectiveKind.Eliminate => "expedition-hud-objective-eliminate",
                ExpeditionObjectiveKind.Find => "expedition-hud-objective-find",
                _ => "expedition-hud-objective-deliver",
            };
            view.Objectives.Add(new ExpeditionHudObjectiveLine(Loc.GetString(key, ("name", objective.Name)),
                objective.Done, objective.Total, objective.Finished));
        }

        view.Completed = hud.Completed;
        view.WaveLine = hud.WaveTotal > 0 && hud.WaveFighting > 0
            ? Loc.GetString("expedition-hud-wave", ("count", hud.WaveFighting))
            : null;
        view.ToShuttle = null;

        var aboard = hud.Shuttle is { } shuttleNet && xform.GridUid is { } grid && GetNetEntity(grid) == shuttleNet;
        if (now >= hud.EndTime)
        {
            view.Phase = aboard ? ExpeditionHudPhase.Aboard : ExpeditionHudPhase.Departed;
            view.Timer = TimeSpan.Zero;
        }
        else if (aboard)
        {
            view.Phase = ExpeditionHudPhase.Aboard;
            view.Timer = hud.EndTime - now;
        }
        else if (hud.ShuttleLanded)
        {
            view.Phase = ExpeditionHudPhase.Return;
            view.Timer = hud.EndTime - now;
            view.ToShuttle = hud.ShuttlePosition - _xform.GetWorldPosition(xform);
        }
        else if (now >= hud.ExtractionAt)
        {
            view.Phase = ExpeditionHudPhase.ShuttleIncoming;
            view.Timer = hud.EndTime - now;
        }
        else
        {
            view.Phase = ExpeditionHudPhase.Countdown;
            view.Timer = hud.ExtractionAt - now;
        }

        return true;
    }
}
