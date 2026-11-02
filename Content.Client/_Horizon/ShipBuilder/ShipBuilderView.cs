using System.Numerics;
using Content.Client.Eye;
using Content.Client.Viewport;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._Horizon.ShipBuilder;

/// <summary>
/// Область с камерой верфи. ЛКМ - строить (можно вести зажатой), ПКМ - двигать камеру, колесо - масштаб.
/// </summary>
public sealed class ShipBuilderView : Control
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    private const float MinZoom = 0.25f;
    private const float MaxZoom = 2f;
    private const float FlySpeed = 9f; // тайлов в секунду, как у глаза ИИ

    private readonly EyeLerpingSystem _eyeLerping;
    private readonly ScalingViewport _view;
    private readonly FixedEye _defaultEye = new();

    private EntityUid? _camera;
    private bool _painting;
    private bool _panning;
    private Vector2i? _lastCell;
    private Vector2 _pendingPan;
    private float _zoom = 1f;
    private bool _up;
    private bool _down;
    private bool _left;
    private bool _right;

    /// <summary>Клик/протяжка по карте: мировые координаты точки.</summary>
    public event Action<Vector2>? OnPaint;

    /// <summary>Накопленный сдвиг камеры в мировых единицах.</summary>
    public event Action<Vector2>? OnPan;

    public ShipBuilderView()
    {
        IoCManager.InjectDependencies(this);
        _eyeLerping = _entManager.System<EyeLerpingSystem>();

        MouseFilter = MouseFilterMode.Stop;
        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        HorizontalExpand = true;
        VerticalExpand = true;

        _view = new ScalingViewport
        {
            MouseFilter = MouseFilterMode.Ignore,
            MinSize = new Vector2(480, 480),
            HorizontalExpand = true,
            VerticalExpand = true,
            Eye = _defaultEye,
        };
        _view.OnResized += OnViewResized;
        AddChild(_view);
    }

    private void OnViewResized()
    {
        var width = Math.Max(_view.PixelWidth, (int) Math.Floor(_view.MinWidth));
        var height = Math.Max(_view.PixelHeight, (int) Math.Floor(_view.MinHeight));
        _view.ViewportSize = new Vector2i(width, height);
    }

    private NetEntity? _pendingCamera;

    public void SetCamera(NetEntity? camera)
    {
        _pendingCamera = camera;
    }

    private void SetCamera(EntityUid camera)
    {
        if (_camera == camera)
            return;

        if (_camera != null)
            _eyeLerping.RemoveEye(_camera.Value);

        _eyeLerping.AddEye(camera);
        _camera = camera;
    }

    public void Release()
    {
        if (_camera != null)
            _eyeLerping.RemoveEye(_camera.Value);

        _camera = null;
    }

    private void Paint(Vector2 screenPosition)
    {
        var world = _view.PixelToMap(screenPosition).Position;
        var cell = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
        if (_lastCell == cell)
            return;

        _lastCell = cell;
        OnPaint?.Invoke(world);
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();
        GrabKeyboardFocus();
    }

    protected override void KeyboardFocusExited()
    {
        base.KeyboardFocusExited();
        _up = _down = _left = _right = false;
    }

    private bool SetMoveKey(BoundKeyFunction function, bool pressed)
    {
        if (function == EngineKeyFunctions.MoveUp) _up = pressed;
        else if (function == EngineKeyFunctions.MoveDown) _down = pressed;
        else if (function == EngineKeyFunctions.MoveLeft) _left = pressed;
        else if (function == EngineKeyFunctions.MoveRight) _right = pressed;
        else return false;

        return true;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (SetMoveKey(args.Function, true))
        {
            args.Handle();
            return;
        }

        if (args.Function == EngineKeyFunctions.UIClick)
        {
            _painting = true;
            _lastCell = null;
            Paint(args.PointerLocation.Position);
            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            _panning = true;
            args.Handle();
        }
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (SetMoveKey(args.Function, false))
        {
            args.Handle();
            return;
        }

        if (args.Function == EngineKeyFunctions.UIClick)
            _painting = false;
        else if (args.Function == EngineKeyFunctions.UIRightClick)
            _panning = false;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (_painting)
            Paint(args.GlobalPixelPosition.Position);

        if (_panning)
        {
            var current = args.GlobalPixelPosition.Position;
            var previous = current - args.Relative * UIScale;
            _pendingPan += _view.PixelToMap(previous).Position - _view.PixelToMap(current).Position;
        }
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        _zoom = Math.Clamp(_zoom - args.Delta.Y * 0.15f * _zoom, MinZoom, MaxZoom);
        args.Handle();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // BUI-состояние приходит раньше игрового, поэтому ждём, пока камера появится на клиенте.
        if (_pendingCamera is { } net
            && _entManager.TryGetEntity(net, out var cameraUid))
        {
            SetCamera(cameraUid.Value);
        }

        // Свободный полёт камеры, как у глаза ИИ. Клавиши движения перехватываются здесь,
        // поэтому персонаж у консоли не двигается.
        var dir = new Vector2((_right ? 1 : 0) - (_left ? 1 : 0), (_up ? 1 : 0) - (_down ? 1 : 0));
        if (dir != Vector2.Zero)
            _pendingPan += Vector2.Normalize(dir) * FlySpeed * Math.Max(_zoom, 0.5f) * args.DeltaSeconds;

        if (_pendingPan != Vector2.Zero)
        {
            OnPan?.Invoke(_pendingPan);
            _pendingPan = Vector2.Zero;
        }

        if (_camera != null && _entManager.TryGetComponent<EyeComponent>(_camera, out var eye) && eye.Eye != null)
            _view.Eye = eye.Eye;
        else
            _view.Eye = _defaultEye;

        _view.Eye.Zoom = new Vector2(_zoom, _zoom);
    }
}
