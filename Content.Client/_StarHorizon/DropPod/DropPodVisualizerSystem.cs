using System.Numerics;
using Content.Shared._StarHorizon.DropPod;
using Content.Shared.Storage;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._StarHorizon.DropPod;

/// <summary>
/// Анимации капсулы: тряска и уход вниз перед БСС. Падение при приземлении здесь не анимируется:
/// его показывает отдельная сущность с вертикальным падением (см. <c>StarHorizonDropPodLandingAnimation</c>).
/// Видимость слоёв (стоит / после падения / дверца / воронка) тоже здесь.
/// </summary>
public sealed class DropPodVisualizerSystem : VisualizerSystem<DropPodComponent>
{
    private const string AnimationKey = "droppod";

    /// <summary>Масштаб спрайта капсулы из прототипа (свои спрайты капсул нарисованы в размер).</summary>
    private static readonly Vector2 BaseScale = Vector2.One;

    /// <summary>Капсула нарисована носом вверх, спрайт не поворачиваем.</summary>
    private static readonly Angle BaseRotation = Angle.Zero;

    protected override void OnAppearanceChange(EntityUid uid, DropPodComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        UpdateLayers((uid, args.Sprite), args.Component);

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
            default:
                SpriteSystem.SetScale((uid, args.Sprite), BaseScale);
                SpriteSystem.SetOffset((uid, args.Sprite), Vector2.Zero);
                SpriteSystem.SetRotation((uid, args.Sprite), BaseRotation);
                SpriteSystem.SetColor((uid, args.Sprite), Color.White);
                break;
        }
    }

    /// <summary>
    /// Слои капсулы: стоит (корпус и дверца, если закрыта), в полёте скрыта, после падения (стоит в воронке, дверца,
    /// если закрыта). Всё в одном месте, чтобы разные визуализаторы не спорили за одни и те же слои.
    /// </summary>
    private void UpdateLayers(Entity<SpriteComponent> sprite, AppearanceComponent appearance)
    {
        AppearanceSystem.TryGetData<bool>(sprite, DropPodVisuals.InFlight, out var inFlight, appearance);
        AppearanceSystem.TryGetData<bool>(sprite, DropPodVisuals.Landed, out var landed, appearance);
        AppearanceSystem.TryGetData<bool>(sprite, StorageVisuals.Open, out var open, appearance);

        var standing = !inFlight && !landed;
        SetVisible(sprite, "droppodBase", standing);
        SetVisible(sprite, "droppodDoor", standing && !open);
        SetVisible(sprite, "droppodGround", landed);
        SetVisible(sprite, "droppodLanded", landed);
        SetVisible(sprite, "droppodLandedDoor", landed && !open);
    }

    private void SetVisible(Entity<SpriteComponent> sprite, string layer, bool visible)
    {
        if (SpriteSystem.LayerMapTryGet(sprite.AsNullable(), layer, out var index, false))
            SpriteSystem.LayerSetVisible(sprite.AsNullable(), index, visible);
    }

    /// <summary>Нарастающая тряска, затем капсула взлетает носом вверх: уменьшается и исчезает.</summary>
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

        // Взлёт вверх.
        offset.KeyFrames.Add(new AnimationTrackProperty.KeyFrame(new Vector2(0, 2.5f), length - shakeEnd));

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
}
