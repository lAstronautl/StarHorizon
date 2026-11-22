using System.Numerics;
using Content.Shared._StarHorizon.DropPod;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._StarHorizon.DropPod;

/// <summary>
/// Шлейф за падающей капсулой и клубы дыма. Падение идёт по диагонали (см. <see cref="DropPodLandingGeometry"/>):
/// нижний край шлейфа следует за капсулой, а полоса вырастает позади неё до <see cref="DropPodLandingGeometry.TrailLength"/>
/// тайлов, потом гаснет. Клубы дыма растут и тают.
/// </summary>
public sealed class DropPodTrailSystem : EntitySystem
{
    [Dependency] private readonly AnimationPlayerSystem _animation = default!;

    private const float Width = 1.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DropPodTrailComponent, ComponentStartup>(OnTrailStartup);
        SubscribeLocalEvent<DropPodSmokePuffComponent, ComponentStartup>(OnPuffStartup);
    }

    private void OnTrailStartup(Entity<DropPodTrailComponent> ent, ref ComponentStartup args)
    {
        const int first = DropPodLandingGeometry.FirstVisibleFrame;
        const int last = DropPodLandingGeometry.LastFrame;
        const float step = DropPodLandingGeometry.FrameTime;
        const float tiny = 0.01f;

        var dir = DropPodLandingGeometry.PathDirection;
        var begin = DropPodLandingGeometry.FrameStart(first);
        var fallEnd = DropPodLandingGeometry.FrameStart(last);
        var length = MathF.Max((float) ent.Comp.Lifetime.TotalSeconds, fallEnd + 1.5f);
        var player = EnsureComp<AnimationPlayerComponent>(ent);

        // Нижний край шлейфа идёт по реальным позициям капсулы в кадрах спрайта (она разгоняется),
        // а длина растёт по мере пролёта, пока не дойдёт до TrailLength.
        var origin = DropPodLandingGeometry.FrameOffset(first);
        var offset = new AnimationTrackComponentProperty
        {
            ComponentType = typeof(SpriteComponent),
            Property = nameof(SpriteComponent.Offset),
            InterpolationMode = AnimationInterpolationMode.Linear,
            KeyFrames =
            {
                new AnimationTrackProperty.KeyFrame(origin + dir * (tiny / 2), 0),
                new AnimationTrackProperty.KeyFrame(origin + dir * (tiny / 2), begin),
            },
        };
        var scale = new AnimationTrackComponentProperty
        {
            ComponentType = typeof(SpriteComponent),
            Property = nameof(SpriteComponent.Scale),
            InterpolationMode = AnimationInterpolationMode.Linear,
            KeyFrames =
            {
                new AnimationTrackProperty.KeyFrame(new Vector2(Width, tiny), 0),
                new AnimationTrackProperty.KeyFrame(new Vector2(Width, tiny), begin),
            },
        };

        for (var frame = first + 1; frame <= last; frame++)
        {
            var pod = DropPodLandingGeometry.FrameOffset(frame);
            var travelled = (pod - origin).Length();
            var len = MathF.Min(DropPodLandingGeometry.TrailLength, MathF.Max(tiny, travelled));
            offset.KeyFrames.Add(new AnimationTrackProperty.KeyFrame(pod + dir * (len / 2), step));
            scale.KeyFrames.Add(new AnimationTrackProperty.KeyFrame(new Vector2(Width, len), step));
        }

        var rest = length - fallEnd;
        var endPod = DropPodLandingGeometry.FrameOffset(last);
        offset.KeyFrames.Add(new AnimationTrackProperty.KeyFrame(endPod + dir * (DropPodLandingGeometry.TrailLength / 2), rest));
        scale.KeyFrames.Add(new AnimationTrackProperty.KeyFrame(new Vector2(Width, DropPodLandingGeometry.TrailLength), rest));

        var color = new AnimationTrackComponentProperty
        {
            ComponentType = typeof(SpriteComponent),
            Property = nameof(SpriteComponent.Color),
            InterpolationMode = AnimationInterpolationMode.Linear,
            KeyFrames =
            {
                new AnimationTrackProperty.KeyFrame(new Color(1f, 1f, 1f, 0f), 0),
                new AnimationTrackProperty.KeyFrame(new Color(1f, 1f, 1f, 0f), begin),
                new AnimationTrackProperty.KeyFrame(new Color(1f, 1f, 1f, 0.85f), step),
                new AnimationTrackProperty.KeyFrame(new Color(1f, 1f, 1f, 0.85f), fallEnd - begin - step),
                new AnimationTrackProperty.KeyFrame(new Color(1f, 1f, 1f, 0f), rest),
            },
        };

        _animation.Play((ent, player), new Animation
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks = { offset, scale, color },
        }, "trail");
    }

    private void OnPuffStartup(Entity<DropPodSmokePuffComponent> ent, ref ComponentStartup args)
    {
        var length = MathF.Max((float) ent.Comp.Lifetime.TotalSeconds, 0.5f);
        var player = EnsureComp<AnimationPlayerComponent>(ent);

        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(ent.Comp.StartScale), 0),
                        new AnimationTrackProperty.KeyFrame(new Vector2(ent.Comp.EndScale), length),
                    },
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Color(1f, 1f, 1f, 0.75f), 0),
                        new AnimationTrackProperty.KeyFrame(new Color(1f, 1f, 1f, 0.45f), length * 0.5f),
                        new AnimationTrackProperty.KeyFrame(new Color(1f, 1f, 1f, 0f), length * 0.5f),
                    },
                },
            },
        };

        _animation.Play((ent, player), animation, "puff");
    }
}
