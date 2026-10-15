using Content.Shared._Horizon.Shuttles.Components;
using Content.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.Shared._Horizon.Shuttles.Systems;

/// <summary>
/// Lets a landable shuttle's grid overlap a landing pad's grid. Only the grid-vs-grid collision is
/// cancelled; walls and other entity fixtures keep colliding as usual.
/// </summary>
public abstract class SharedShuttleLandingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LandableShuttleComponent, PreventCollideEvent>(OnPreventCollide);
    }

    private void OnPreventCollide(Entity<LandableShuttleComponent> ent, ref PreventCollideEvent args)
    {
        if (!HasComp<LandingPadComponent>(args.OtherEntity))
            return;

        const int grid = (int) CollisionGroup.MapGrid;
        if ((args.OurFixture.CollisionLayer & grid) == 0 || (args.OtherFixture.CollisionLayer & grid) == 0)
            return;

        args.Cancelled = true;
    }
}
