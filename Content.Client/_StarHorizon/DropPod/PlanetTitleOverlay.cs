using System.Numerics;
using Content.Client._Horizon.ShiftStartEffect;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._StarHorizon.DropPod;

/// <summary>
/// Название планеты при посадке капсулы: только имя, без фона и остального, печатается по буквам со звуком печати
/// (как заголовок интро в начале смены), затем плавно гаснет.
/// </summary>
public sealed class PlanetTitleOverlay : Overlay
{
    [Dependency] private readonly IAudioManager _audio = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IResourceCache _cache = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private const float CharsPerSecond = 11f;
    private const float Hold = 2.2f;
    private const float FadeOut = 1f;
    // Не чаще ~22 щелчков в секунду.
    private const float TypeSoundInterval = 0.045f;

    /// <summary>Название доиграло, система может убрать оверлей.</summary>
    public bool Finished;

    public readonly string Title;

    private readonly TimeSpan _start;
    private readonly TypingSoundSynth _typing;
    private readonly Font _font;
    private int _typedPrev;
    private TimeSpan _nextSound;

    public PlanetTitleOverlay(string title)
    {
        IoCManager.InjectDependencies(this);
        Title = title.ToUpperInvariant();
        _start = _timing.RealTime;
        _typing = new TypingSoundSynth(_audio, _timing);
        _font = new VectorFont(_cache.GetResource<FontResource>("/Fonts/_Horizon/Pixelizer.ttf"), 56);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var t = (float) (_timing.RealTime - _start).TotalSeconds;
        var typeTime = Title.Length / CharsPerSecond;
        var total = typeTime + Hold + FadeOut;
        if (t >= total)
        {
            Finished = true;
            return;
        }

        var typed = Math.Min(Title.Length, (int) (t * CharsPerSecond) + 1);
        var alpha = Math.Clamp((total - t) / FadeOut, 0f, 1f);

        var size = (Vector2) args.ViewportBounds.Size;
        if (_clyde.MainWindow != null)
            size.Y = MathF.Min(size.Y, _clyde.MainWindow.Size.Y);

        var h = args.ScreenHandle;
        var width = h.GetDimensions(_font, Title, 1f).X;
        h.DrawString(_font, new Vector2(size.X / 2f - width / 2f, size.Y * 0.4f), Title[..typed], Color.White.WithAlpha(alpha));

        // Щелчок печати на каждый новый символ.
        _typing.Update();
        var now = _timing.RealTime;
        var fresh = typed - _typedPrev;
        _typedPrev = typed;
        if (fresh > 0 && typed <= Title.Length && t < typeTime + 0.1f && now >= _nextSound)
        {
            _typing.Tick();
            _nextSound = now + TimeSpan.FromSeconds(TypeSoundInterval);
        }
    }

    /// <summary>Освобождает звуковые ресурсы, вызывается при удалении оверлея.</summary>
    public void Cleanup()
    {
        _typing.Dispose();
    }
}
