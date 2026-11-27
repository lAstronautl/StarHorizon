using System.Numerics;

namespace Content.Shared._StarHorizon.DropPod;

/// <summary>
/// Геометрия и тайминги посадочной анимации (наш спрайт _StarHorizon/Effects/DropPod/landingpod.rsi, кадр 140x320,
/// 15 кадров: первый держится 1.25 с, остальные 14 по 0.04 с). Капсула падает строго вертикально, разгоняясь, и
/// касается точки сущности. Дым строится вдоль этой траектории по реальным позициям капсулы в кадрах спрайта
/// (у всех вариантов капсул траектория одинаковая).
/// </summary>
public static class DropPodLandingGeometry
{
    /// <summary>Пауза первого кадра анимации, пока капсулы почти не видно.</summary>
    public const float StartDelay = 1.25f;

    /// <summary>Длительность одного кадра падения.</summary>
    public const float FrameTime = 0.04f;

    /// <summary>Номер последнего кадра (капсула в точке касания).</summary>
    public const int LastFrame = 14;

    /// <summary>Первый кадр, на котором капсулу уже можно разглядеть.</summary>
    public const int FirstVisibleFrame = 4;

    /// <summary>Длительность самого падения (14 кадров по 0.04 с).</summary>
    public const float FallDuration = LastFrame * FrameTime;

    /// <summary>Полная длительность анимации.</summary>
    public const float TotalTime = StartDelay + FallDuration;

    /// <summary>Длина шлейфа за капсулой, тайлы.</summary>
    public const float TrailLength = 3f;

    /// <summary>Поворот вертикального спрайта шлейфа вдоль траектории: падение вертикальное.</summary>
    public const double TrailRotationDegrees = 0;

    /// <summary>
    /// Центр капсулы в каждом кадре спрайта относительно точки касания (тайлы, оси мира, Y вверх).
    /// Снято по альфа-каналу *-landing.png (кадр 140x320, 32 пикселя на тайл, по нижнему краю капсулы).
    /// </summary>
    private static readonly Vector2[] FrameOffsets =
    {
        new(0f, 9.281f),
        new(0f, 8.875f),
        new(0f, 8.625f),
        new(0f, 8.156f),
        new(0f, 7.594f),
        new(0f, 7.094f),
        new(0f, 6.312f),
        new(0f, 5.625f),
        new(0f, 4.906f),
        new(0f, 4.250f),
        new(0f, 3.312f),
        new(0f, 2.219f),
        new(0f, 1.188f),
        new(0f, 0.469f),
        new(0f, 0.000f),
    };

    /// <summary>Положение капсулы в кадре <paramref name="frame"/> (0..14) относительно точки касания.</summary>
    public static Vector2 FrameOffset(int frame) => FrameOffsets[Math.Clamp(frame, 0, LastFrame)];

    /// <summary>Когда (секунды от начала анимации) начинается кадр <paramref name="frame"/> (1..14).</summary>
    public static float FrameStart(int frame) => StartDelay + (frame - 1) * FrameTime;

    /// <summary>Единичный вектор от точки касания назад по траектории (туда, откуда падает капсула).</summary>
    public static readonly Vector2 PathDirection = Vector2.Normalize(FrameOffsets[FirstVisibleFrame]);

    /// <summary>Кадр, на котором <paramref name="index"/>-й из <paramref name="count"/> клубов дыма остаётся на траектории.</summary>
    public static int PuffFrame(int index, int count)
    {
        var span = LastFrame - FirstVisibleFrame;
        return FirstVisibleFrame + (int) MathF.Round((index + 0.5f) / count * span);
    }
}
