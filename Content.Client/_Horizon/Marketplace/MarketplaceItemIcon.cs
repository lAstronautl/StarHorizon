using System.Numerics;
using Content.Client.Examine;
using Content.Shared.Input;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Horizon.Marketplace;

/// <summary>
/// Framed picture of a real item. Right click or the examine key shows the item's examine tooltip.
/// Falls back to the prototype's icon until the entity itself reaches the client.
/// </summary>
public sealed class MarketplaceItemIcon : PanelContainer
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    private static readonly Color Background = Color.FromHex("#0B0F16");
    private static readonly Color Border = Color.FromHex("#2E5D86");
    private static readonly Color BorderHover = Color.FromHex("#5FB4FF");

    private readonly StyleBoxFlat _style;
    private readonly SpriteView _spriteView;
    private readonly EntityPrototypeView _protoView;
    private readonly Label _badge;

    private NetEntity? _netEntity;
    private bool _hasPrototype;
    private EntityUid? _entity;

    public MarketplaceItemIcon(float size)
    {
        IoCManager.InjectDependencies(this);

        SetSize = new Vector2(size, size);
        MouseFilter = MouseFilterMode.Stop;
        ToolTip = Loc.GetString("marketplace-examine-hint");

        _style = new StyleBoxFlat
        {
            BackgroundColor = Background,
            BorderColor = Border,
            BorderThickness = new Thickness(1),
        };
        PanelOverride = _style;

        // Item sprites are 32px; scale them up by a whole factor so pixel art stays crisp.
        var scale = Math.Max(1f, MathF.Floor((size - 8) / 32f));

        _spriteView = new SpriteView
        {
            OverrideDirection = Direction.South,
            SetSize = new Vector2(size - 8, size - 8),
            Scale = new Vector2(scale, scale),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Visible = false,
        };
        _protoView = new EntityPrototypeView
        {
            OverrideDirection = Direction.South,
            SetSize = new Vector2(size - 8, size - 8),
            Scale = new Vector2(scale, scale),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Visible = false,
        };
        _badge = new Label
        {
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Bottom,
            Margin = new Thickness(0, 0, 4, 1),
            FontColorOverride = Color.FromHex("#FFD866"),
            Visible = false,
        };

        AddChild(_spriteView);
        AddChild(_protoView);
        AddChild(_badge);

        OnKeyBindDown += HandleKeyBind;
    }

    public void SetItem(NetEntity? netEntity, string? protoId, int? count = null)
    {
        _netEntity = netEntity;
        _entity = null;

        _hasPrototype = protoId != null;
        _protoView.SetPrototype(protoId == null ? (EntProtoId?) null : new EntProtoId(protoId));
        _protoView.Visible = _hasPrototype;
        _spriteView.Visible = false;

        _badge.Visible = count is > 1;
        _badge.Text = count is > 1 ? $"×{count}" : string.Empty;

        TryResolveEntity();
    }

    private void TryResolveEntity()
    {
        if (_entity != null && _entMan.EntityExists(_entity))
            return;

        _entity = null;
        if (_netEntity is not { } net || !_entMan.TryGetEntity(net, out var uid) || !_entMan.EntityExists(uid))
        {
            _spriteView.Visible = false;
            _protoView.Visible = _hasPrototype;
            return;
        }

        _entity = uid;
        _spriteView.SetEntity(uid);
        _spriteView.Visible = true;
        _protoView.Visible = false;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // Listed items stream in through PVS shortly after the UI opens.
        if (_netEntity != null && (_entity == null || !_entMan.EntityExists(_entity)))
            TryResolveEntity();
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();
        _style.BorderColor = BorderHover;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _style.BorderColor = Border;
    }

    private void HandleKeyBind(GUIBoundKeyEventArgs args)
    {
        if (args.Function != ContentKeyFunctions.ExamineEntity && args.Function != EngineKeyFunctions.UseSecondary)
            return;

        if (_entity is not { } uid || !_entMan.EntityExists(uid))
            return;

        _entMan.System<ExamineSystem>().DoExamine(uid);
        args.Handle();
    }
}
