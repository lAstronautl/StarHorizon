using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._StarHorizon.DropPod;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._StarHorizon.DropPod;

/// <summary>
/// Капсула с грузом: падает с неба (наша анимация вертикального падения), встаёт в воронке, через
/// <see cref="OpenDelay"/> открывается и выпускает груз (мобов волны, приёмник). Пока мобы внутри, их ИИ выключен.
/// Пустая капсула через <see cref="Lifetime"/> исчезает.
/// </summary>
[RegisterComponent]
public sealed partial class ExpeditionCargoPodComponent : Component
{
    [ViewVariables] public ExpeditionCargoPodStage Stage;
    [ViewVariables] public TimeSpan LandAt;
    [ViewVariables] public TimeSpan OpenAt;
    [ViewVariables] public TimeSpan DeleteAt;

    /// <summary>Мобы внутри, у которых был включён ИИ: включаем обратно при выходе.</summary>
    [ViewVariables] public List<EntityUid> SleepingNpcs = new();
}

public sealed class ExpeditionCargoPodSystem : EntitySystem
{
    /// <summary>Синдикатская капсула (все волны и приёмник падают в ней).</summary>
    public const string PodProto = "ExpeditionCargoPod";
    public const string ContainerId = "cargo";
    public const string LandingAnimation = "StarHorizonDropPodLandingAnimationSyndicate";
    public const string SmokePuff = "StarHorizonDropPodSmokePuff";

    /// <summary>Через сколько после касания капсула открывается.</summary>
    public static readonly TimeSpan OpenDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>Сколько пустая открытая капсула лежит на земле.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    /// <summary>Звук падения тот же, что у дроп-пода.</summary>
    private static readonly SoundSpecifier LandingSound = new SoundPathSpecifier("/Audio/_Goobstation/DropPod/supplypodlanding.ogg");
    private static readonly SoundSpecifier ImpactSound = new SoundPathSpecifier("/Audio/Effects/explosion1.ogg");

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly TransformSystem _xform = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly PointLightSystem _light = default!;

    /// <summary>Сбросить капсулу с грузом в точку. Груз сразу оказывается внутри и выходит, когда капсула откроется.</summary>
    public EntityUid Drop(EntityCoordinates coords, IEnumerable<EntityUid> cargo)
    {
        var pod = Spawn(PodProto, coords);
        var comp = EnsureComp<ExpeditionCargoPodComponent>(pod);
        var container = _container.EnsureContainer<Container>(pod, ContainerId);

        foreach (var item in cargo)
        {
            if (TryComp<HTNComponent>(item, out var htn) && htn.Enabled)
            {
                htn.Enabled = false;
                _npc.SleepNPC(item, htn);
                comp.SleepingNpcs.Add(item);
            }

            _container.Insert(item, container);
        }

        var now = _timing.CurTime;
        comp.Stage = ExpeditionCargoPodStage.Falling;
        comp.LandAt = now + TimeSpan.FromSeconds(DropPodLandingGeometry.TotalTime);
        comp.OpenAt = comp.LandAt + OpenDelay;
        comp.DeleteAt = comp.OpenAt + Lifetime;
        _appearance.SetData(pod, ExpeditionCargoPodVisuals.Stage, ExpeditionCargoPodStage.Falling);
        _light.SetEnabled(pod, false);

        Spawn(LandingAnimation, coords);
        _audio.PlayPvs(LandingSound, pod);
        return pod;
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ExpeditionCargoPodComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            switch (comp.Stage)
            {
                case ExpeditionCargoPodStage.Falling when now >= comp.LandAt:
                    Land((uid, comp));
                    break;
                case ExpeditionCargoPodStage.Landed when now >= comp.OpenAt:
                    Open((uid, comp));
                    break;
                case ExpeditionCargoPodStage.Open when now >= comp.DeleteAt:
                    QueueDel(uid);
                    break;
            }
        }
    }

    private void Land(Entity<ExpeditionCargoPodComponent> ent)
    {
        ent.Comp.Stage = ExpeditionCargoPodStage.Landed;
        _appearance.SetData(ent, ExpeditionCargoPodVisuals.Stage, ExpeditionCargoPodStage.Landed);
        _light.SetEnabled(ent, true);
        _audio.PlayPvs(ImpactSound, ent);

        var impact = _xform.GetMapCoordinates(ent.Owner);
        for (var i = 0; i < 5; i++)
        {
            var offset = _random.NextVector2(0.2f, 1f);
            Spawn(SmokePuff, new MapCoordinates(impact.Position + offset, impact.MapId));
        }
    }

    private void Open(Entity<ExpeditionCargoPodComponent> ent)
    {
        ent.Comp.Stage = ExpeditionCargoPodStage.Open;
        _appearance.SetData(ent, ExpeditionCargoPodVisuals.Stage, ExpeditionCargoPodStage.Open);

        if (_container.TryGetContainer(ent, ContainerId, out var container))
            _container.EmptyContainer(container, force: true);

        foreach (var npc in ent.Comp.SleepingNpcs)
        {
            if (TerminatingOrDeleted(npc) || !TryComp<HTNComponent>(npc, out var htn))
                continue;

            htn.Enabled = true;
            _npc.WakeNPC(npc, htn);
        }

        ent.Comp.SleepingNpcs.Clear();
    }
}

/// <summary>На карте экспедиции: загрузчик биома на площадке посадки дроп-подов уже стоит.</summary>
[RegisterComponent]
public sealed partial class DropPodLandingLoaderComponent : Component
{
    [ViewVariables] public EntityUid Loader;
}
