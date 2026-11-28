using System.Linq;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Shared._StarHorizon.Expedition;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Content.Shared.Salvage.Fulton;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Utility;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Задания планетных экспедиций «Квота добычи», «Сбор образцов» и «Ремонт шаттла»: приёмник в капсуле, сдача руды и
/// образцов, ремонт узлов разбитого эвакуационного шаттла и оборона, пока запускаются его системы.
/// </summary>
public sealed class ExpeditionObjectivesSystem : EntitySystem
{
    public const string DepotProto = "ExpeditionDepot";
    public const string SampleProto = "ExpeditionSample";
    public const string RepairPointProto = "ExpeditionRepairPoint";

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SalvageSystem _salvage = default!;
    [Dependency] private readonly ExpeditionWaveSystem _waves = default!;
    [Dependency] private readonly PursuePlayerSystem _pursue = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly AnchorableSystem _anchorable = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly TransformSystem _xform = default!;
    [Dependency] private readonly Robust.Server.Audio.AudioSystem _audio = default!;

    private static readonly Robust.Shared.Prototypes.ProtoId<TagPrototype> OreTag = "Ore";
    private static readonly TimeSpan DepotDelay = TimeSpan.FromSeconds(15);

    private readonly List<(EntityUid Uid, System.Numerics.Vector2 Pos)> _players = new();
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExpeditionDepotComponent, InteractUsingEvent>(OnDepotInteractUsing);
        SubscribeLocalEvent<ExpeditionDepotComponent, GetVerbsEvent<InteractionVerb>>(OnDepotVerbs);
        SubscribeLocalEvent<ExpeditionDepotComponent, ExpeditionDepotFultonDoAfterEvent>(OnDepotFultonDoAfter);
        SubscribeLocalEvent<ExpeditionRepairPointComponent, InteractHandEvent>(OnRepairInteract);
        SubscribeLocalEvent<ExpeditionRepairPointComponent, ExpeditionRepairDoAfterEvent>(OnRepairDoAfter);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);

        var depots = EntityQueryEnumerator<ExpeditionDepotHolderComponent, SalvageExpeditionComponent>();
        while (depots.MoveNext(out var map, out var holder, out var expedition))
        {
            UpdateDepot((map, holder));
            CheckDepotObjective((map, holder), expedition);
        }

        var repairs = EntityQueryEnumerator<SalvageRepairExpeditionComponent, SalvageExpeditionComponent>();
        while (repairs.MoveNext(out var map, out var repair, out var expedition))
        {
            UpdateRepair((map, repair), expedition);
        }
    }

    #region Depot

    /// <summary>Приёмник сбрасывают в капсуле рядом с первым игроком, через несколько секунд после его появления.</summary>
    private void UpdateDepot(Entity<ExpeditionDepotHolderComponent> ent)
    {
        if (ent.Comp.Dropped)
            return;

        _pursue.GetAlivePlayers(ent.Owner, _players);
        if (_players.Count == 0)
            return;

        var now = _timing.CurTime;
        ent.Comp.DropAt ??= now + DepotDelay;
        if (now < ent.Comp.DropAt)
            return;

        ent.Comp.Dropped = true;
        var depot = Spawn(DepotProto, Robust.Shared.Map.MapCoordinates.Nullspace);
        EnsureComp<ExpeditionDepotComponent>(depot).Expedition = ent.Owner;
        ent.Comp.Depot = depot;
        _waves.DropCargo(ent.Owner, _players[0].Pos, depot);
        _salvage.Announce(ent.Owner, Loc.GetString("expedition-depot-dropped"));
    }

    private void OnDepotInteractUsing(Entity<ExpeditionDepotComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var map = ent.Comp.Expedition;
        var mining = TryComp<SalvageMiningExpeditionComponent>(map, out var miningComp);
        var sampling = TryComp<SalvageSamplingExpeditionComponent>(map, out var samplingComp);
        if (!mining && !sampling)
            return;

        // Сдаём сам предмет или всё подходящее из сумки (сумка для руды, рюкзак).
        var items = new List<EntityUid>();
        if (Accepts(args.Used, mining, sampling))
            items.Add(args.Used);
        else if (TryComp<StorageComponent>(args.Used, out var storage))
            items.AddRange(storage.Container.ContainedEntities.Where(e => Accepts(e, mining, sampling)));

        if (items.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("expedition-depot-rejected"), ent, args.User);
            return;
        }

        args.Handled = true;
        var ore = 0;
        var samples = 0;
        foreach (var item in items)
        {
            if (HasComp<ExpeditionSampleComponent>(item))
                samples++;
            else
                ore += TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;

            QueueDel(item);
        }

        if (miningComp != null)
            miningComp.Collected += ore;
        if (samplingComp != null)
            samplingComp.Collected += samples;

        var amount = ore + samples;
        _popup.PopupEntity(Loc.GetString("expedition-depot-accepted", ("count", amount)), ent, args.User);
    }

    private bool Accepts(EntityUid item, bool mining, bool sampling)
    {
        return mining && _tags.HasTag(item, OreTag) || sampling && HasComp<ExpeditionSampleComponent>(item);
    }

    private void CheckDepotObjective(Entity<ExpeditionDepotHolderComponent> ent, SalvageExpeditionComponent expedition)
    {
        UpdateFulton(ent, expedition);
        if (ent.Comp.Done)
            return;

        // Квота добычи: выполнена, как только руда сдана.
        if (TryComp<SalvageMiningExpeditionComponent>(ent, out var mining) && mining.Collected >= mining.Quota)
        {
            ent.Comp.Done = true;
            expedition.Completed = true;
            _salvage.Announce(ent.Owner, Loc.GetString("expedition-quota-done"));
            return;
        }

        // Сбор образцов: образцы собраны, теперь приёмник надо отправить фултоном.
        if (!ent.Comp.FultonReady && TryComp<SalvageSamplingExpeditionComponent>(ent, out var sampling)
            && sampling.Collected >= sampling.Required)
        {
            ent.Comp.FultonReady = true;
            _salvage.Announce(ent.Owner, Loc.GetString("expedition-samples-fulton"));
        }
    }

    /// <summary>Приёмник улетел на фултоне: задание выполнено, приёмник и маяк убираются после анимации.</summary>
    private void UpdateFulton(Entity<ExpeditionDepotHolderComponent> ent, SalvageExpeditionComponent expedition)
    {
        var comp = ent.Comp;
        if (comp.CleanupAt is { } cleanup)
        {
            if (_timing.CurTime < cleanup)
                return;

            comp.CleanupAt = null;
            QueueDel(comp.Depot);
            QueueDel(comp.FultonBeacon);
            comp.Depot = null;
            comp.FultonBeacon = null;
            return;
        }

        if (!comp.FultonAttached || comp.Done || comp.Depot is not { } depot)
            return;

        // FultonSystem снимает FultonedComponent, когда фултон поднял груз (перенёс к маяку).
        if (HasComp<FultonedComponent>(depot) && !TerminatingOrDeleted(depot))
            return;

        comp.Done = true;
        expedition.Completed = true;
        comp.CleanupAt = _timing.CurTime + TimeSpan.FromSeconds(3);
        _salvage.Announce(ent.Owner, Loc.GetString("expedition-samples-done"));
    }

    private void OnDepotVerbs(Entity<ExpeditionDepotComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract
            || !TryComp<ExpeditionDepotHolderComponent>(ent.Comp.Expedition, out var holder)
            || !holder.FultonReady || holder.FultonAttached)
        {
            return;
        }

        var user = args.User;
        var depot = ent.Owner;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("expedition-depot-verb-fulton"),
            Icon = new SpriteSpecifier.Rsi(new ResPath("Objects/Tools/fulton.rsi"), "extraction_pack"),
            Act = () => _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, holder.AttachTime,
                new ExpeditionDepotFultonDoAfterEvent(), depot, target: depot)
            {
                BreakOnMove = true,
                NeedHand = true,
            }),
        });
    }

    private void OnDepotFultonDoAfter(Entity<ExpeditionDepotComponent> ent, ref ExpeditionDepotFultonDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        AttachFulton(ent);
    }

    /// <summary>
    /// Привязать приёмник к фултону: настоящий фултон (шар над грузом, звук, анимация подъёма) уносит его к невидимому
    /// маяку далеко за картой. Снять нельзя.
    /// </summary>
    public bool AttachFulton(Entity<ExpeditionDepotComponent> ent)
    {
        if (!TryComp<ExpeditionDepotHolderComponent>(ent.Comp.Expedition, out var holder)
            || !holder.FultonReady || holder.FultonAttached)
        {
            return false;
        }

        holder.FultonAttached = true;
        var depotPos = _xform.GetWorldPosition(ent.Owner);
        holder.FultonBeacon = Spawn(null, new EntityCoordinates(ent.Comp.Expedition, depotPos + new System.Numerics.Vector2(0, 3000)));

        var fultoned = EnsureComp<FultonedComponent>(ent);
        fultoned.Beacon = holder.FultonBeacon;
        fultoned.FultonDuration = holder.FultonDuration;
        fultoned.NextFulton = _timing.CurTime + holder.FultonDuration;
        fultoned.Removeable = false;
        Dirty(ent.Owner, fultoned);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Items/Mining/fultext_deploy.ogg"), ent);

        _salvage.Announce(ent.Comp.Expedition, Loc.GetString("expedition-samples-fulton-attached",
            ("seconds", (int) holder.FultonDuration.TotalSeconds)));
        return true;
    }

    #endregion

    #region Repair

    private void UpdateRepair(Entity<SalvageRepairExpeditionComponent> ent, SalvageExpeditionComponent expedition)
    {
        var comp = ent.Comp;
        if (!comp.PointsSpawned)
        {
            TrySpawnPoints(ent);
            return;
        }

        var now = _timing.CurTime;
        switch (comp.Stage)
        {
            case ExpeditionRepairStage.Repairing:
                if (comp.Points.Count > 0 && comp.Points.All(p => !Exists(p) || Comp<ExpeditionRepairPointComponent>(p).Repaired))
                {
                    comp.Stage = ExpeditionRepairStage.Defending;
                    comp.DefenseStart = now;
                    comp.DefenseEnd = now + comp.DefenseDuration;
                    comp.NextDefenseWave = now + comp.DefenseWaveInterval;
                    _salvage.Announce(ent.Owner, Loc.GetString("expedition-repair-defend",
                        ("seconds", (int) comp.DefenseDuration.TotalSeconds)));
                    _waves.ForceWave(ent.Owner);
                }
                break;

            case ExpeditionRepairStage.Defending:
                if (now >= comp.DefenseEnd)
                {
                    comp.Stage = ExpeditionRepairStage.Done;
                    expedition.Completed = true;
                    if (GetShuttle(ent.Owner) is { } shuttle)
                        RemComp<ExpeditionBrokenShuttleComponent>(shuttle);
                    _salvage.Announce(ent.Owner, Loc.GetString("expedition-repair-done"));
                }
                else if (now >= comp.NextDefenseWave)
                {
                    comp.NextDefenseWave = now + comp.DefenseWaveInterval;
                    _waves.ForceWave(ent.Owner);
                }
                break;
        }
    }

    /// <summary>Разбитый шаттл (эвакуационный шаттл экспедиции, пока он разбит).</summary>
    public EntityUid? GetShuttle(EntityUid map)
    {
        return TryComp<ExpeditionExtractionComponent>(map, out var extraction) && extraction.Shuttle is { } shuttle
                                                                               && HasComp<ExpeditionBrokenShuttleComponent>(shuttle)
            ? shuttle
            : null;
    }

    /// <summary>Шаттл сел: на свободных тайлах его пола появляются повреждённые узлы.</summary>
    private void TrySpawnPoints(Entity<SalvageRepairExpeditionComponent> ent)
    {
        if (GetShuttle(ent.Owner) is not { } shuttle
            || Transform(shuttle).MapUid != ent.Owner
            || TryComp<FTLComponent>(shuttle, out var ftl) && ftl.State is not (FTLState.Cooldown or FTLState.Available)
            || !TryComp<MapGridComponent>(shuttle, out var grid))
        {
            return;
        }

        var tiles = _map.GetAllTiles(shuttle, grid).Select(t => t.GridIndices)
            .Where(t => _anchorable.TileFree((shuttle, grid), t, (int) (CollisionGroup.Impassable | CollisionGroup.MachineLayer),
                (int) (CollisionGroup.Impassable | CollisionGroup.MachineLayer)))
            .ToList();
        _random.Shuffle(tiles);

        foreach (var tile in tiles.Take(ent.Comp.Total))
        {
            var point = Spawn(RepairPointProto, _map.GridTileToLocal(shuttle, grid, tile));
            EnsureComp<ExpeditionRepairPointComponent>(point).Expedition = ent.Owner;
            _appearance.SetData(point, ExpeditionRepairVisuals.Repaired, false);
            ent.Comp.Points.Add(point);
        }

        ent.Comp.Total = ent.Comp.Points.Count;
        ent.Comp.PointsSpawned = true;
        _salvage.Announce(ent.Owner, Loc.GetString("expedition-repair-crashed", ("count", ent.Comp.Total)));
    }

    private void OnRepairInteract(Entity<ExpeditionRepairPointComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || ent.Comp.Repaired)
            return;

        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.RepairTime,
            new ExpeditionRepairDoAfterEvent(), ent, target: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
        _popup.PopupEntity(Loc.GetString("expedition-repair-start"), ent, args.User);
    }

    private void OnRepairDoAfter(Entity<ExpeditionRepairPointComponent> ent, ref ExpeditionRepairDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Repaired)
            return;

        args.Handled = true;
        Repair(ent);
        _popup.PopupEntity(Loc.GetString("expedition-repair-point-done"), ent, args.User);
    }

    /// <summary>Узел починен (после ремонта или командой/тестом).</summary>
    public void Repair(Entity<ExpeditionRepairPointComponent> ent)
    {
        ent.Comp.Repaired = true;
        _appearance.SetData(ent, ExpeditionRepairVisuals.Repaired, true);
    }

    #endregion
}
