using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._Horizon.Shipyard;

/// <summary>
/// Draws a shuttle tile layout. The layout is baked once into a texture (one pixel per tile),
/// so each frame costs a single textured quad no matter how large the shuttle is.
/// </summary>
public sealed class ShuttlePreviewTextureControl : Control
{
    private static readonly Color Background = Color.FromHex("#0b0b10");
    private const float PreviewMargin = 8f;

    [Dependency] private readonly IClyde _clyde = default!;

    private OwnedTexture? _texture;

    public ShuttlePreviewTextureControl()
    {
        IoCManager.InjectDependencies(this);
        RectClipContent = true;
    }

    public bool HasPreview => _texture != null;

    public void SetPreview(Vector2i[] tiles, Color[] colors)
    {
        Clear();

        if (tiles.Length == 0 || tiles.Length != colors.Length)
            return;

        var min = tiles[0];
        var max = tiles[0];
        foreach (var tile in tiles)
        {
            min = Vector2i.ComponentMin(min, tile);
            max = Vector2i.ComponentMax(max, tile);
        }

        var width = max.X - min.X + 1;
        var height = max.Y - min.Y + 1;

        using var image = new Image<Rgba32>(width, height, new Rgba32(0, 0, 0, 0));
        for (var i = 0; i < tiles.Length; i++)
        {
            var c = colors[i];
            image[tiles[i].X - min.X, max.Y - tiles[i].Y] = new Rgba32(c.R, c.G, c.B, c.A);
        }

        _texture = _clyde.LoadTextureFromImage(image, "shuttle-preview");
    }

    public void Clear()
    {
        _texture?.Dispose();
        _texture = null;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, Background);
        if (_texture == null)
            return;

        var size = (Vector2) _texture.Size;
        var scale = MathF.Min((PixelWidth - PreviewMargin * 2) / size.X, (PixelHeight - PreviewMargin * 2) / size.Y);
        // Whole-pixel scale keeps tiles crisp; fall back to fractional only when the shuttle doesn't fit.
        if (scale >= 1f)
            scale = MathF.Floor(scale);

        var drawSize = size * scale;
        var origin = (PixelSize - drawSize) / 2f;
        handle.DrawTextureRect(_texture, UIBox2.FromDimensions(origin, drawSize));
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        // Free the GPU texture as soon as the window is closed.
        Clear();
    }
}
