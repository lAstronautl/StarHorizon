using System.Numerics;
using Content.Shared._StarHorizon.DropPod;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._StarHorizon.DropPod;

/// <summary>
/// Шлейф за падающей капсулой. Нижний край следует за капсулой (она падает под углом 45° на место посадки),
/// а полоса постепенно вырастает до <see cref="TrailLength"/> тайлов позади неё. Потом плавно гаснет.
/// </summary>
public sealed class DropPodTrailSystem : EntitySystem
{
    [Dependency] private readonly AnimationPlayerSystem _animation = default!;

    /// <summary>Должно совпадать с падением в <see cref="DropPodVisualizerSystem"/>.</summary>
    public const float FallTime = 0.5f;
    public const float FallHeight = 4f;

    /// <summary>Капсула падает под углом: летит справа-сверху налево-вниз.</summary>
    public static readonly Vector2 FallDirection = Vector2.Normalize(new Vector2(1f, 1f));

    /// <summary>Наклон капсулы и шлейфа вдоль траектории (по часовой стрелке на 45°).</summary>
    public static readonly Angle Tilt = Angle.FromDegrees(-45);

    /// <summary>Длина шлейфа в тайлах.</summary>
    public const float TrailLength = 3f;

    private const float Width = 1.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DropPodTrailComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<DropPodTrailComponent> ent, ref ComponentStartup args)
    {
        var length = MathF.Max((float) ent.Comp.Lifetime.TotalSeconds, FallTime + 0.5f);
        var player = EnsureComp<AnimationPlayerComponent>(ent);

        // Центр полосы = низ (позиция капсулы) + половина длины; длина растёт от 0 до TrailLength,
        // пока капсула падает с FallHeight до 0, так что нижний край всё время у капсулы.
        const float start = 0.01f;
        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(FallDirection * (FallHeight + start / 2), 0),
                        new AnimationTrackProperty.KeyFrame(FallDirection * (TrailLength / 2), FallTime),
                        new AnimationTrackProperty.KeyFrame(FallDirection * (TrailLength / 2), length - FallTime),
                    },
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(Width, start), 0),
                        new AnimationTrackProperty.KeyFrame(new Vector2(Width, TrailLength), FallTime),
                        new AnimationTrackProperty.KeyFrame(new Vector2(Width, TrailLength), length - FallTime),
                    },
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Color(1f, 1f, 1f, 0.9f), 0),
                        new AnimationTrackProperty.KeyFrame(new Color(1f, 1f, 1f, 0.9f), FallTime),
                        new AnimationTrackProperty.KeyFrame(new Color(1f, 1f, 1f, 0f), length - FallTime),
                    },
                },
            },
        };

        _animation.Play((ent, player), animation, "trail");
    }
}
