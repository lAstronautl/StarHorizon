using System.Numerics;
using Content.Shared._StarHorizon.DropPod;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._StarHorizon.DropPod;

/// <summary>
/// Анимации капсулы: тряска и взлёт перед уходом в блюспейс, падение "с неба" при приземлении.
/// </summary>
public sealed class DropPodVisualizerSystem : VisualizerSystem<DropPodComponent>
{
    private const string AnimationKey = "droppod";

    /// <summary>Масштаб спрайта капсулы из прототипа.</summary>
    private static readonly Vector2 BaseScale = new(0.75f, 0.75f);

    protected override void OnAppearanceChange(EntityUid uid, DropPodComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!AppearanceSystem.TryGetData<DropPodState>(uid, DropPodVisuals.State, out var state, args.Component))
            return;

        var player = EnsureComp<AnimationPlayerComponent>(uid);
        if (AnimationSystem.HasRunningAnimation(player, AnimationKey))
            AnimationSystem.Stop((uid, player), AnimationKey);

        switch (state)
        {
            case DropPodState.ZoomingOut:
                AnimationSystem.Play((uid, player), MakeLaunchAnimation(component), AnimationKey);
                break;
            case DropPodState.Landing:
                AnimationSystem.Play((uid, player), MakeLandingAnimation(), AnimationKey);
                break;
            default:
                SpriteSystem.SetScale((uid, args.Sprite), BaseScale);
                SpriteSystem.SetOffset((uid, args.Sprite), Vector2.Zero);
                SpriteSystem.SetRotation((uid, args.Sprite), Angle.Zero);
                SpriteSystem.SetColor((uid, args.Sprite), Color.White);
                break;
        }
    }

    /// <summary>Нарастающая тряска, затем капсула уходит вниз (как в люк корабля): уменьшается и исчезает.</summary>
    private static Animation MakeLaunchAnimation(DropPodComponent comp)
    {
        var length = (float) Math.Max(0.5, comp.ZoomOutTime.TotalSeconds);
        var shakeEnd = length * 0.75f;

        var offset = new AnimationTrackComponentProperty
        {
            ComponentType = typeof(SpriteComponent),
            Property = nameof(SpriteComponent.Offset),
            InterpolationMode = AnimationInterpolationMode.Linear,
        };

        // Тряска с растущей амплитудой.
        const int shakes = 14;
        for (var i = 0; i <= shakes; i++)
        {
            var amp = 0.01f + 0.07f * i / shakes;
            offset.KeyFrames.Add(new AnimationTrackProperty.KeyFrame(new Vector2(i % 2 == 0 ? amp : -amp, 0), i == 0 ? 0 : shakeEnd / shakes));
        }

        // Уход вниз.
        offset.KeyFrames.Add(new AnimationTrackProperty.KeyFrame(new Vector2(0, -1.5f), length - shakeEnd));

        var scale = new AnimationTrackComponentProperty
        {
            ComponentType = typeof(SpriteComponent),
            Property = nameof(SpriteComponent.Scale),
            InterpolationMode = AnimationInterpolationMode.Linear,
            KeyFrames =
            {
                new AnimationTrackProperty.KeyFrame(BaseScale, 0),
                new AnimationTrackProperty.KeyFrame(BaseScale, shakeEnd),
                new AnimationTrackProperty.KeyFrame(BaseScale * 0.35f, length - shakeEnd),
            },
        };

        var color = new AnimationTrackComponentProperty
        {
            ComponentType = typeof(SpriteComponent),
            Property = nameof(SpriteComponent.Color),
            InterpolationMode = AnimationInterpolationMode.Linear,
            KeyFrames =
            {
                new AnimationTrackProperty.KeyFrame(Color.White, 0),
                new AnimationTrackProperty.KeyFrame(Color.White, shakeEnd),
                new AnimationTrackProperty.KeyFrame(new Color(0.5f, 0.8f, 1f, 0f), length - shakeEnd),
            },
        };

        return new Animation
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks = { offset, scale, color },
        };
    }

    /// <summary>Капсула падает под углом 45°: большая и прозрачная, затем садится на место и выпрямляется.</summary>
    private static Animation MakeLandingAnimation()
    {
        const float fall = DropPodTrailSystem.FallTime;
        const float bounce = 0.15f;
        const float length = fall + bounce;

        return new Animation
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
                        new AnimationTrackProperty.KeyFrame(DropPodTrailSystem.FallDirection * DropPodTrailSystem.FallHeight, 0),
                        new AnimationTrackProperty.KeyFrame(new Vector2(0, 0), fall),
                        new AnimationTrackProperty.KeyFrame(new Vector2(0, 0.12f), bounce / 2),
                        new AnimationTrackProperty.KeyFrame(new Vector2(0, 0), bounce / 2),
                    },
                },
                // Летит под углом 45° и выпрямляется при касании.
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Rotation),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(DropPodTrailSystem.Tilt, 0),
                        new AnimationTrackProperty.KeyFrame(DropPodTrailSystem.Tilt, fall),
                        new AnimationTrackProperty.KeyFrame(Angle.Zero, bounce),
                    },
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(BaseScale * 2f, 0),
                        new AnimationTrackProperty.KeyFrame(BaseScale, fall),
                        new AnimationTrackProperty.KeyFrame(BaseScale, bounce),
                    },
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Color(1f, 1f, 1f, 0f), 0),
                        new AnimationTrackProperty.KeyFrame(Color.White, fall * 0.6f),
                        new AnimationTrackProperty.KeyFrame(Color.White, fall * 0.4f + bounce),
                    },
                },
            },
        };
    }
}
