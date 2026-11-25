using Content.Client.Salvage;
using Content.Shared._StarHorizon.DropPod;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Containers;

namespace Content.Client._StarHorizon.DropPod;

/// <summary>
/// Когда экран пассажира капсулы переключается на место посадки (капсула в состоянии Arriving), показывает название
/// планеты: только имя, без фона, со звуком печати.
/// </summary>
public sealed class PlanetTitleSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    private PlanetTitleOverlay? _overlay;

    /// <summary>Название, которое сейчас на экране (для тестов).</summary>
    public string? CurrentTitle => _overlay?.Title;

    public override void Shutdown()
    {
        base.Shutdown();
        RemoveOverlay();
    }

    public override void Update(float frameTime)
    {
        if (_overlay is { Finished: true })
            RemoveOverlay();

        WatchPod();

        // Состояние капсулы и её перенос на карту посадки приходят вместе: карту проверяем уже в кадре, а не в событии.
        if (_pendingPod is { } pod)
        {
            _pendingTime += frameTime;
            if (TryShow(pod) || _pendingTime > 2f || !Exists(pod))
                _pendingPod = null;
        }
    }

    private DropPodState? _lastState;
    private EntityUid? _watchedPod;

    /// <summary>
    /// Следит за капсулой, в которой сидит игрок (на её визуальное событие уже подписан визуализатор): когда она переходит
    /// в Arriving, то есть экран переключился на место посадки, готовим показ названия.
    /// </summary>
    private void WatchPod()
    {
        if (_player.LocalEntity is not { } player
            || !_container.TryGetContainingContainer((player, null, null), out var container)
            || !HasComp<DropPodComponent>(container.Owner)
            || !_appearance.TryGetData<DropPodState>(container.Owner, DropPodVisuals.State, out var state))
        {
            _watchedPod = null;
            _lastState = null;
            return;
        }

        if (_watchedPod != container.Owner)
        {
            _watchedPod = container.Owner;
            _lastState = null;
        }

        if (state == DropPodState.Arriving && _lastState != DropPodState.Arriving)
        {
            _pendingPod = container.Owner;
            _pendingTime = 0f;
        }

        _lastState = state;
    }

    private EntityUid? _pendingPod;
    private float _pendingTime;

    private bool TryShow(EntityUid pod)
    {
        if (Transform(pod).MapUid is not { } map || !HasComp<SalvageExpeditionComponent>(map))
            return false;

        var name = MetaData(map).EntityName;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        RemoveOverlay();
        _overlay = new PlanetTitleOverlay(name);
        _overlays.AddOverlay(_overlay);
        return true;
    }

    private void RemoveOverlay()
    {
        if (_overlay == null)
            return;

        _overlays.RemoveOverlay(_overlay);
        _overlay.Cleanup();
        _overlay = null;
    }
}
