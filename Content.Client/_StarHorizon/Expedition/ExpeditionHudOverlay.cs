using System.Numerics;
using Content.Shared._StarHorizon.Expedition;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._StarHorizon.Expedition;

/// <summary>
/// Рисует интерфейс экспедиции (<see cref="ExpeditionHudSystem"/>) в стиле Deep Rock Galactic: тёмные полупрозрачные
/// плашки с оранжевой полосой, задачи с галочками и прогрессом, крупный таймер эвакуации по центру.
/// </summary>
public sealed class ExpeditionHudOverlay : Overlay
{
    [Dependency] private readonly IResourceCache _cache = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IEyeManager _eye = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private static readonly Color Accent = Color.FromHex("#A6E35A"); // салатовый
    private static readonly Color Panel = Color.Black.WithAlpha(0.55f);
    private static readonly Color Done = Color.FromHex("#7FD35A");
    private static readonly Color Danger = Color.FromHex("#FF4A3A");
    private static readonly Color Muted = Color.FromHex("#C8C8C8");

    private readonly ExpeditionHudSystem _system;
    private float _fontScale = -1f;
    private Font _header = default!;
    private Font _text = default!;
    private Font _timer = default!;

    public ExpeditionHudOverlay(ExpeditionHudSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
    }

    private void EnsureFonts(float scale)
    {
        if (MathHelper.CloseTo(scale, _fontScale))
            return;

        _fontScale = scale;
        var bold = _cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf");
        var regular = _cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf");
        _header = new VectorFont(bold, (int) MathF.Round(13 * scale));
        _text = new VectorFont(regular, (int) MathF.Round(12 * scale));
        _timer = new VectorFont(bold, (int) MathF.Round(26 * scale));
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _system.TryGetView(out _);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_system.TryGetView(out var view))
            return;

        var scale = _ui.DefaultUIScale;
        EnsureFonts(scale);
        var h = args.ScreenHandle;
        var bounds = args.ViewportBounds;

        // Всё по центру сверху: таймер, под ним компас, под ним задачи (слева сверху их закрывали кнопки меню и слоты).
        var centerX = (bounds.Left + bounds.Right) / 2f;
        var timerBottom = DrawTimer(h, view, new Vector2(centerX, bounds.Top + 12 * scale), scale);
        var compassBottom = DrawCompass(h, view, new Vector2(centerX, timerBottom + 10 * scale), scale);
        DrawObjectives(h, view, new Vector2(centerX, compassBottom + 10 * scale), scale);
    }

    /// <param name="topCenter">Середина верхнего края плашки.</param>
    private void DrawObjectives(DrawingHandleScreen h, ExpeditionHudView view, Vector2 topCenter, float scale)
    {
        var pad = 8 * scale;
        var line = 20 * scale;
        var box = 11 * scale;
        var header = Loc.GetString(view.Completed ? "expedition-hud-objectives-done" : "expedition-hud-objectives");

        // Ширина плашки по самой длинной строке.
        var width = h.GetDimensions(_header, header, 1f).X;
        foreach (var o in view.Objectives)
            width = MathF.Max(width, box + 6 * scale + h.GetDimensions(_text, $"{o.Text}  {o.Progress}", 1f).X);
        if (view.WaveLine != null)
            width = MathF.Max(width, h.GetDimensions(_text, view.WaveLine, 1f).X);
        width += pad * 2 + 4 * scale;
        // Ширина не дёргается от смены цифр прогресса: не меньше фиксированной.
        width = MathF.Max(width, 300 * scale);
        var origin = new Vector2(topCenter.X - width / 2f, topCenter.Y);

        var rows = 1 + view.Objectives.Count + (view.WaveLine != null ? 1 : 0);
        var height = pad * 2 + rows * line;
        h.DrawRect(UIBox2.FromDimensions(origin, new Vector2(width, height)), Panel);
        h.DrawRect(UIBox2.FromDimensions(origin, new Vector2(3 * scale, height)), view.Completed ? Done : Accent);

        var x = origin.X + pad + 4 * scale;
        var y = origin.Y + pad;
        h.DrawString(_header, new Vector2(x, y), header, view.Completed ? Done : Accent);
        y += line;

        foreach (var o in view.Objectives)
        {
            // Галочка: пустой квадрат или закрашенный.
            var check = UIBox2.FromDimensions(new Vector2(x, y + 3 * scale), new Vector2(box, box));
            if (o.Finished)
                h.DrawRect(check, Done);
            else
                h.DrawRect(check, Muted, filled: false);

            var color = o.Finished ? Done : o.Paused ? Danger : Color.White;
            h.DrawString(_text, new Vector2(x + box + 6 * scale, y), $"{o.Text}  {o.Progress}", color);
            y += line;
        }

        if (view.WaveLine != null)
        {
            var pulse = 0.65f + 0.35f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 5f);
            h.DrawString(_text, new Vector2(x, y), view.WaveLine, Danger.WithAlpha(pulse));
        }
    }

    /// <returns>Нижний край плашки таймера.</returns>
    private float DrawTimer(DrawingHandleScreen h, ExpeditionHudView view, Vector2 top, float scale)
    {
        var (label, color) = view.Phase switch
        {
            ExpeditionHudPhase.Countdown => ("expedition-hud-extraction-in", Color.White),
            ExpeditionHudPhase.ShuttleIncoming => ("expedition-hud-shuttle-incoming", Accent),
            ExpeditionHudPhase.Return => ("expedition-hud-return", Accent),
            ExpeditionHudPhase.Aboard => ("expedition-hud-aboard", Done),
            _ => ("expedition-hud-departed", Danger),
        };

        var labelText = Loc.GetString(label);
        var seconds = (int) Math.Ceiling(Math.Max(0, view.Timer.TotalSeconds));
        var timerText = $"{seconds / 60:00}:{seconds % 60:00}";

        // Последние 30 секунд до вылета: таймер красный и мигает.
        var timerColor = Color.White;
        if (view.Phase is ExpeditionHudPhase.ShuttleIncoming or ExpeditionHudPhase.Return && seconds <= 30)
        {
            var pulse = 0.6f + 0.4f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 8f);
            timerColor = Danger.WithAlpha(pulse);
        }

        var labelSize = h.GetDimensions(_header, labelText, 1f);
        var timerSize = view.Phase == ExpeditionHudPhase.Departed ? Vector2.Zero : h.GetDimensions(_timer, timerText, 1f);
        var pad = 8 * scale;
        // Ширина плашки фиксирована: по самой длинной из надписей таймера, чтобы плашка не меняла размер.
        var width = FixedTimerWidth(h) + pad * 4;
        var height = pad * 2 + labelSize.Y + timerSize.Y;
        var origin = new Vector2(top.X - width / 2f, top.Y);

        h.DrawRect(UIBox2.FromDimensions(origin, new Vector2(width, height)), Panel);
        h.DrawRect(UIBox2.FromDimensions(origin + new Vector2(0, height - 3 * scale), new Vector2(width, 3 * scale)), color);

        var y = origin.Y + pad;
        h.DrawString(_header, new Vector2(top.X - labelSize.X / 2f, y), labelText, color);
        y += labelSize.Y;

        if (view.Phase != ExpeditionHudPhase.Departed)
        {
            h.DrawString(_timer, new Vector2(top.X - timerSize.X / 2f, y), timerText, timerColor);
            y += timerSize.Y;
        }

        return origin.Y + height;
    }

    /// <summary>
    /// Компас под таймером: стороны света и стрелка к цели (данж, место задания, приёмник или шаттл) с расстоянием.
    /// Всё поворачивается вместе с камерой: «С» показывает туда, где на экране север.
    /// </summary>
    private static readonly string[] TimerLabels =
    {
        "expedition-hud-extraction-in", "expedition-hud-shuttle-incoming", "expedition-hud-return",
        "expedition-hud-aboard", "expedition-hud-departed",
    };

    private float _fixedTimerWidth;
    private float _fixedTimerScale = -1f;

    private float FixedTimerWidth(DrawingHandleScreen h)
    {
        if (MathHelper.CloseTo(_fixedTimerScale, _fontScale))
            return _fixedTimerWidth;

        var width = h.GetDimensions(_timer, "88:88", 1f).X;
        foreach (var key in TimerLabels)
            width = MathF.Max(width, h.GetDimensions(_header, Loc.GetString(key), 1f).X);

        _fixedTimerWidth = width;
        _fixedTimerScale = _fontScale;
        return width;
    }

    /// <returns>Нижний край компаса (с подписью цели).</returns>
    private float DrawCompass(DrawingHandleScreen h, ExpeditionHudView view, Vector2 top, float scale)
    {
        var radius = 30 * scale;
        var center = top + new Vector2(0, radius);
        h.DrawCircle(center, radius, Panel);
        h.DrawCircle(center, radius, Accent.WithAlpha(0.5f), filled: false);

        // Стороны света с учётом поворота камеры.
        var letters = new (Vector2 Dir, string Key)[]
        {
            (new Vector2(0, 1), "expedition-hud-compass-n"),
            (new Vector2(1, 0), "expedition-hud-compass-e"),
            (new Vector2(0, -1), "expedition-hud-compass-s"),
            (new Vector2(-1, 0), "expedition-hud-compass-w"),
        };
        foreach (var (dir, key) in letters)
        {
            var letter = Loc.GetString(key);
            var size = h.GetDimensions(_text, letter, 1f);
            var pos = center + ToScreen(dir) * (radius - 9 * scale) - size / 2f;
            h.DrawString(_text, pos, letter, key == "expedition-hud-compass-n" ? Danger : Muted);
        }

        if (view.ToCompassTarget is not { } delta)
            return center.Y + radius;

        var color = view.CompassKind switch
        {
            ExpeditionCompassTarget.Shuttle => Accent,
            ExpeditionCompassTarget.Objective => Done,
            ExpeditionCompassTarget.Depot => Color.FromHex("#5AC8FF"),
            _ => Color.White,
        };

        // Стрелка от центра к краю и метка цели на ободе. Рядом с целью вместо стрелки точка в центре.
        var distance = delta.Length();
        if (distance > 1.5f)
        {
            var dir = ToScreen(Vector2.Normalize(delta));
            var side = new Vector2(-dir.Y, dir.X);
            var tip = center + dir * (radius - 2 * scale);
            var baseCenter = center + dir * (radius - 16 * scale);
            h.DrawPrimitives(DrawPrimitiveTopology.TriangleList,
                new[] { tip, baseCenter + side * 6 * scale, baseCenter - side * 6 * scale }, color);
            h.DrawLine(center, baseCenter, color.WithAlpha(0.8f));
        }

        h.DrawCircle(center, 3 * scale, color);

        var label = Loc.GetString("expedition-hud-compass-target-" + view.CompassKind.ToString().ToLowerInvariant(),
            ("meters", (int) MathF.Round(distance)));
        var labelSize = h.GetDimensions(_text, label, 1f);
        var labelPos = new Vector2(center.X - labelSize.X / 2f, center.Y + radius + 4 * scale);
        h.DrawRect(UIBox2.FromDimensions(labelPos - new Vector2(4 * scale, 1 * scale), labelSize + new Vector2(8 * scale, 2 * scale)), Panel);
        h.DrawString(_text, labelPos, label, color);
        return labelPos.Y + labelSize.Y;
    }

    /// <summary>
    /// Направление в мире в направление на экране с учётом поворота камеры (как у пинпоинтера: угол камеры
    /// прибавляется), Y экрана вниз.
    /// </summary>
    private Vector2 ToScreen(Vector2 worldDir)
    {
        return WorldToScreenDirection(_eye.CurrentEye.Rotation, worldDir);
    }

    /// <summary>Направление в мире при повороте камеры <paramref name="eyeRotation"/> в направление на экране (Y вниз).</summary>
    public static Vector2 WorldToScreenDirection(Angle eyeRotation, Vector2 worldDir)
    {
        var view = eyeRotation.RotateVec(worldDir);
        return new Vector2(view.X, -view.Y);
    }
}
