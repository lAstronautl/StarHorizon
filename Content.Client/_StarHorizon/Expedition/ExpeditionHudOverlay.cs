using System.Numerics;
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

    private static readonly Color Accent = Color.FromHex("#F0A030");
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

        DrawObjectives(h, view, new Vector2(bounds.Left + 16 * scale, bounds.Top + 70 * scale), scale);
        DrawTimer(h, view, new Vector2((bounds.Left + bounds.Right) / 2f, bounds.Top + 12 * scale), scale);
    }

    private void DrawObjectives(DrawingHandleScreen h, ExpeditionHudView view, Vector2 origin, float scale)
    {
        var pad = 8 * scale;
        var line = 20 * scale;
        var box = 11 * scale;
        var header = Loc.GetString(view.Completed ? "expedition-hud-objectives-done" : "expedition-hud-objectives");

        // Ширина плашки по самой длинной строке.
        var width = h.GetDimensions(_header, header, 1f).X;
        foreach (var o in view.Objectives)
            width = MathF.Max(width, box + 6 * scale + h.GetDimensions(_text, $"{o.Text}  {o.Done}/{o.Total}", 1f).X);
        if (view.WaveLine != null)
            width = MathF.Max(width, h.GetDimensions(_text, view.WaveLine, 1f).X);
        width += pad * 2 + 4 * scale;

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

            var color = o.Finished ? Done : Color.White;
            h.DrawString(_text, new Vector2(x + box + 6 * scale, y), $"{o.Text}  {o.Done}/{o.Total}", color);
            y += line;
        }

        if (view.WaveLine != null)
        {
            var pulse = 0.65f + 0.35f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 5f);
            h.DrawString(_text, new Vector2(x, y), view.WaveLine, Danger.WithAlpha(pulse));
        }
    }

    private void DrawTimer(DrawingHandleScreen h, ExpeditionHudView view, Vector2 top, float scale)
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

        string? distance = null;
        if (view.ToShuttle is { } delta)
            distance = Loc.GetString("expedition-hud-distance", ("meters", (int) MathF.Round(delta.Length())));

        var labelSize = h.GetDimensions(_header, labelText, 1f);
        var timerSize = view.Phase == ExpeditionHudPhase.Departed ? Vector2.Zero : h.GetDimensions(_timer, timerText, 1f);
        var distSize = distance != null ? h.GetDimensions(_text, distance, 1f) + new Vector2(18 * scale, 0) : Vector2.Zero;
        var pad = 8 * scale;
        var width = MathF.Max(labelSize.X, MathF.Max(timerSize.X, distSize.X)) + pad * 4;
        var height = pad * 2 + labelSize.Y + timerSize.Y + distSize.Y;
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

        if (distance != null && view.ToShuttle is { } toShuttle)
        {
            var textX = top.X - distSize.X / 2f + 18 * scale;
            DrawArrow(h, new Vector2(top.X - distSize.X / 2f + 6 * scale, y + distSize.Y / 2f), toShuttle, 6 * scale, Accent);
            h.DrawString(_text, new Vector2(textX, y), distance, Color.White);
        }
    }

    /// <summary>Треугольная стрелка в сторону цели с учётом поворота камеры.</summary>
    private void DrawArrow(DrawingHandleScreen h, Vector2 center, Vector2 worldDelta, float size, Color color)
    {
        if (worldDelta.LengthSquared() < 0.01f)
            return;

        var view = (-_eye.CurrentEye.Rotation).RotateVec(worldDelta);
        var dir = Vector2.Normalize(new Vector2(view.X, -view.Y));
        var side = new Vector2(-dir.Y, dir.X);
        var tip = center + dir * size;
        var left = center - dir * size * 0.7f + side * size * 0.75f;
        var right = center - dir * size * 0.7f - side * size * 0.75f;
        h.DrawPrimitives(DrawPrimitiveTopology.TriangleList, new[] { tip, left, right }, color);
    }
}
