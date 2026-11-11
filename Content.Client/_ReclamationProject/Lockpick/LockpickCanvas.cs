using System.Numerics;
using Content.Shared._ReclamationProject.Lockpick;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Input;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using G = Content.Shared._ReclamationProject.Lockpick.LockpickGeometry;

namespace Content.Client._ReclamationProject.Lockpick;

/// <summary>
/// #Reclamation Project - the lock cross-section. Mouse X slides a rigid hook pick under a pin; hold and drag up
/// to lift it. The binding pin is stiff, others are springy. All drawing is in 640 x 250 lock pixels, scaled to fit.
/// </summary>
public sealed class LockpickCanvas : Control
{
    [Dependency] private readonly IEntityManager _entities = default!;

    private static readonly Color Housing = Color.FromHex("#262a31");
    private static readonly Color Plug = Color.FromHex("#343a43");
    private static readonly Color Keyway = Color.FromHex("#1d2026");
    private static readonly Color Line = Color.FromHex("#8a909b");
    private static readonly Color Band = Color.FromHex("#2e6b4a");
    private static readonly Color Driver = Color.FromHex("#8f959f");
    private static readonly Color Brass = Color.FromHex("#c9a24a");
    private static readonly Color SetGreen = Color.FromHex("#3fae7f");
    private static readonly Color JamRed = Color.FromHex("#d9534f");
    private static readonly Color FalseOrange = Color.FromHex("#d8803a");
    private static readonly Color Steel = Color.FromHex("#d6dae0");
    private static readonly Color Grip = Color.FromHex("#5a6070");
    private static readonly Color Hint = Color.FromHex("#e0a040");

    private static readonly SoundSpecifier ClickSound = new SoundPathSpecifier("/Audio/Weapons/click.ogg");

    private const float PinHalfWidth = 12f;
    private const float Chamber = 22f;
    private const float Hook = 14f;

    private LockpickBuiState? _state;
    private float[] _lift = Array.Empty<float>();
    private float[] _base = Array.Empty<float>();
    private int _pending = -1;
    private int _lastClicks;

    private Vector2 _mouse = new(160, 200);
    private bool _down;
    private int _under = -1;
    private float _pressY;
    private int _lastRidge;
    private float _strain;
    private float _slipTimer;

    public Func<float>? TensionSource;
    public event Action<int, float, LockpickTension>? OnRelease;
    public event Action? OnSnap;
    public event Action? OnSlip;
    public event Action<float>? OnStrainChanged;

    public LockpickCanvas()
    {
        IoCManager.InjectDependencies(this);
        MinSize = new Vector2(G.Width, G.Height);
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
    }

    public static LockpickTension TensionOf(float value) =>
        value < 25 ? LockpickTension.Light : value > 75 ? LockpickTension.Heavy : LockpickTension.Right;

    private LockpickTension Tension => TensionOf(TensionSource?.Invoke() ?? 50);

    public void UpdateState(LockpickBuiState state)
    {
        if (_lift.Length != state.Pins.Count)
        {
            _lift = new float[state.Pins.Count];
            _base = new float[state.Pins.Count];
        }

        for (var i = 0; i < state.Pins.Count; i++)
        {
            var pin = state.Pins[i];
            if (pin.Set)
                _lift[i] = G.SetLift(pin.Length);
            else if (i == _pending && pin.FalseSet)
                _lift[i] = Math.Max(0, _lift[i] - 14);
            else if (i == _pending && !pin.Jammed && pin.Type != LockPinType.Serrated)
                _lift[i] = 0;
        }

        if (state.Clicks > _lastClicks)
            Click(0.5f);
        _lastClicks = state.Clicks;
        _pending = -1;
        _state = state;
    }

    public void DropAllLocal()
    {
        Array.Clear(_lift);
        _down = false;
        _under = -1;
    }

    private void Click(float volume)
    {
        _entities.System<SharedAudioSystem>().PlayGlobal(ClickSound, Filter.Local(), false,
            AudioParams.Default.WithVolume(volume >= 0.4f ? -2f : -10f));
    }

    private float PinX(int i)
    {
        var count = _state?.Pins.Count ?? 5;
        var bodyCentre = (G.PivotX + G.Width) / 2f;
        return bodyCentre - (count - 1) / 2f * G.PinGap + i * G.PinGap;
    }

    private float Scale => Math.Min(PixelSize.X / G.Width, PixelSize.Y / G.Height);

    /// <summary>Top-left of the drawing in control pixels, so the lock sits centred in the window.</summary>
    private Vector2 Origin => new((PixelSize.X - G.Width * Scale) / 2f, (PixelSize.Y - G.Height * Scale) / 2f);

    private Vector2 ToLock(Vector2 pixel) => (pixel - Origin) / Scale;

    private bool Busy(int i) => _state == null || _state.Done || _state.Pins[i].Set || _state.Pins[i].Jammed;

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        var pos = ToLock(args.RelativePixelPosition);
        if (_down && _under >= 0 && MathF.Abs(pos.X - PinX(_under)) > 20)
            LetGo();
        _mouse = pos;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick || _state == null || _state.Done)
            return;

        _mouse = ToLock(args.RelativePixelPosition);
        for (var i = 0; i < _state.Pins.Count; i++)
        {
            if (MathF.Abs(PinX(i) - _mouse.X) > 20 || Busy(i) || i == _pending)
                continue;

            _down = true;
            _under = i;
            _pressY = _mouse.Y;
            _base[i] = _lift[i];
            _lastRidge = (int) (_lift[i] / 9);
            args.Handle();
            return;
        }
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIClick)
            LetGo();
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        LetGo();
    }

    private void LetGo()
    {
        if (!_down || _under < 0 || _state == null)
        {
            _down = false;
            return;
        }

        var i = _under;
        _down = false;
        _under = -1;
        _pending = i;
        OnRelease?.Invoke(i, _lift[i], Tension);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_state == null)
            return;

        var dt = Math.Min(0.05f, args.DeltaSeconds);
        var tension = Tension;

        if (_down && _under >= 0 && !_state.Done)
        {
            var pin = _state.Pins[_under];
            var max = G.Rest - pin.Length - 12;
            var want = Math.Clamp(_base[_under] + (_pressY - _mouse.Y) * 1.1f, 0, max);
            var binding = _under == _state.Stiff;
            var k = binding ? (tension == LockpickTension.Heavy ? 2f : 4f) : 14f;
            _lift[_under] += (want - _lift[_under]) * Math.Min(1f, k * dt);

            if (pin.Type == LockPinType.Serrated && binding)
            {
                var ridge = (int) (_lift[_under] / 9);
                if (ridge > _lastRidge)
                    Click(0.2f);
                _lastRidge = ridge;
            }

            if (tension == LockpickTension.Heavy && binding)
            {
                _strain += dt * 25;
                if (_strain >= 100)
                {
                    _strain = 0;
                    DropAllLocal();
                    OnSnap?.Invoke();
                }
            }
        }

        if (!(tension == LockpickTension.Heavy && _down) && _strain > 0)
            _strain = Math.Max(0, _strain - dt * 8);
        OnStrainChanged?.Invoke(_strain);

        // Springs push every loose pin back down. Serrated pins catch on their ridges unless tension is light.
        for (var i = 0; i < _state.Pins.Count; i++)
        {
            var pin = _state.Pins[i];
            if (pin.Set || pin.Jammed || i == _under || i == _pending || _lift[i] <= 0)
                continue;
            if (pin.Type == LockPinType.Serrated && tension != LockpickTension.Light)
                continue;
            _lift[i] = Math.Max(0, _lift[i] - 300 * dt);
        }

        // Too little tension for too long lets a set pin slip back down.
        var anySet = false;
        var falseSet = false;
        foreach (var pin in _state.Pins)
        {
            anySet |= pin.Set;
            falseSet |= pin.FalseSet;
        }

        if (tension == LockpickTension.Light && anySet && !falseSet && !_state.Done)
        {
            _slipTimer += dt;
            if (_slipTimer > 2.5f)
            {
                _slipTimer = 0;
                OnSlip?.Invoke();
            }
        }
        else
        {
            _slipTimer = 0;
        }
    }

    private float PlugTurn()
    {
        if (_state == null)
            return 0;
        if (_state.Done)
            return 22;

        var set = 0;
        var falseSet = false;
        foreach (var pin in _state.Pins)
        {
            if (pin.Set)
                set++;
            falseSet |= pin.FalseSet;
        }

        var turn = Math.Min(8f, set * 1.5f);
        return falseSet ? Math.Min(14f, turn + 7f) : turn;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var s = Scale;
        var o = Origin;

        void Rect(float x, float y, float w, float h, Color c) =>
            handle.DrawRect(new UIBox2(o.X + x * s, o.Y + y * s, o.X + (x + w) * s, o.Y + (y + h) * s), c);
        void Outline(float x, float y, float w, float h, Color c) =>
            handle.DrawRect(new UIBox2(o.X + x * s, o.Y + y * s, o.X + (x + w) * s, o.Y + (y + h) * s), c, false);
        void Thick(Vector2 a, Vector2 b, float width, Color c)
        {
            var dir = b - a;
            if (dir.LengthSquared() < 0.001f)
                return;
            var n = new Vector2(-dir.Y, dir.X) / dir.Length() * (width / 2f);
            // A plain array: the client sandbox doesn't allow stackalloc.
            var quad = new[] { o + (a + n) * s, o + (b + n) * s, o + (a - n) * s, o + (b - n) * s };
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, quad, c);
        }

        var turn = PlugTurn();
        Rect(G.PivotX, 0, G.Width - G.PivotX, G.Shear, Housing);
        Rect(G.PivotX, G.Shear, turn, G.Keyway + 30 - G.Shear, Keyway);
        Rect(G.PivotX + turn, G.Shear, G.Width - G.PivotX - turn, G.Keyway + 30 - G.Shear, Plug);
        Outline(G.PivotX, 0, G.Width - G.PivotX - 1, G.Keyway + 30, Line);
        for (var x = G.PivotX; x < G.Width; x += 9)
            handle.DrawLine(o + new Vector2(x, G.Shear) * s, o + new Vector2(Math.Min(x + 5, G.Width), G.Shear) * s, Line);
        Rect(G.PivotX, G.Rest + 4, G.Width - G.PivotX, G.Keyway + 22 - G.Rest, Keyway);

        if (_state != null)
        {
            for (var i = 0; i < _state.Pins.Count; i++)
            {
                var pin = _state.Pins[i];
                var x = PinX(i);
                var band = G.Band(_state.Agility, pin.Type);
                Rect(x - Chamber, G.Shear - band / 2f, Chamber * 2, band, Band);
                Outline(x - Chamber, 8, Chamber * 2, G.Rest - 4, _state.Binding == i ? Hint : Line);

                var top = G.Top(pin.Length, _lift[i]);

                // Spring
                var springBottom = top - 40;
                var prev = new Vector2(x - 7, 10);
                for (var k = 1; k <= 8; k++)
                {
                    var next = new Vector2(x + (k % 2 == 1 ? 7 : -7), 10 + (springBottom - 10) * k / 8f);
                    Thick(prev, next, 2, Line);
                    prev = next;
                }

                // Driver pin (shape shows the pin type)
                var dy = top - 40;
                if (pin.Type == LockPinType.Spool)
                {
                    Rect(x - PinHalfWidth, dy, PinHalfWidth * 2, 9, Driver);
                    Rect(x - 6, dy + 8, 12, 22, Driver);
                    Rect(x - PinHalfWidth, dy + 29, PinHalfWidth * 2, 9, Driver);
                }
                else
                {
                    Rect(x - PinHalfWidth, dy, PinHalfWidth * 2, 38, Driver);
                    if (pin.Type == LockPinType.Serrated)
                    {
                        for (var k = 1; k < 5; k++)
                            Rect(x - PinHalfWidth, dy + k * 7.5f - 1, PinHalfWidth * 2, 2, Housing);
                    }
                }

                // Key pin
                var color = pin.Jammed ? JamRed : pin.Set ? SetGreen : pin.FalseSet ? FalseOrange : Brass;
                Rect(x - PinHalfWidth, top, PinHalfWidth * 2, pin.Length, color);
            }
        }

        // Rigid hook pick, levered from the keyway entrance.
        var pivot = new Vector2(G.PivotX, G.Keyway + 8);
        var tipX = Math.Clamp(_mouse.X, G.PivotX + 40, G.Width - 20);
        var tipTop = _down && _under >= 0 ? G.Rest - _lift[_under] + 2 : G.Rest + 2;
        var hookBase = new Vector2(tipX, tipTop + Hook);
        var dir = Vector2.Normalize(hookBase - pivot);
        Thick(pivot - dir * 90, pivot - dir * 12, 11, Grip);
        Thick(pivot - dir * 14, hookBase, 3, Steel);
        Thick(hookBase, hookBase + new Vector2(dir.Y * Hook * 0.25f + dir.X * 3, -Hook), 3, Steel);

        // Tension wrench
        Thick(new Vector2(G.PivotX + 26, G.Keyway + 18), new Vector2(40, G.Keyway + 18), 5, Grip);
        Thick(new Vector2(40, G.Keyway + 18), new Vector2(40, G.Height - 4), 5, Grip);
    }
}
