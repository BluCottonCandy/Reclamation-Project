using System.Numerics;
using System.Linq;
using Content.Shared._ReclamationProject.Triggermen;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._ReclamationProject.Triggermen;

public sealed class PoolBoundUserInterface(EntityUid owner, Enum key) : BoundUserInterface(owner, key)
{
    private PoolWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<PoolWindow>();
        _window.Move += (point, power, action) => SendMessage(new PoolMove(point, power, action));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is PoolState pool)
            _window?.ShowTable(pool, PlayerManager.LocalEntity is {} actor ? EntMan.GetNetEntity(actor) : null);
    }
}

public sealed class PoolWindow : DefaultWindow
{
    private readonly PoolFelt _felt = new();
    private readonly Label _status = new() { Text = "Join to play solo or with a second player." };
    private readonly Label _help = new();
    private readonly Label _remaining = new();
    private readonly Button _join = new() { Text = "Join" };
    private readonly Button _rack = new() { Text = "Rack", Disabled = true };
    private readonly Button _shoot = new() { Text = "Shoot", Disabled = true };
    private readonly LineEdit _power = new() { Text = "60", MinSize = new Vector2(60, 0) };
    private bool _canAct;
    private bool _ballInHand;
    public event Action<Vector2, int, PoolAction>? Move;

    public PoolWindow()
    {
        Title = "Lucky 38 Pool";
        var layout = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        Contents.AddChild(layout);
        layout.AddChild(_status);
        layout.AddChild(_remaining);
        layout.AddChild(_felt);
        var options = new BoxContainer { SeparationOverride = 8 };
        options.AddChild(_join);
        options.AddChild(_rack);
        options.AddChild(new Label { Text = "Power (1–100)" });
        options.AddChild(_power);
        options.AddChild(_shoot);
        layout.AddChild(options);
        layout.AddChild(_help);
        _help.Text = "Join, click the felt to aim, then press Shoot.";
        _join.OnPressed += _ => Move?.Invoke(Vector2.Zero, 0, PoolAction.Join);
        _rack.OnPressed += _ => Move?.Invoke(Vector2.Zero, 0, PoolAction.Rack);
        _power.OnTextChanged += _ => UpdateShootButton();
        _felt.PointSelected += point =>
        {
            if (_canAct && _ballInHand)
                Move?.Invoke(point, 0, PoolAction.PlaceCue);
            UpdateShootButton();
        };
        _shoot.OnPressed += _ =>
        {
            if (!_canAct || _ballInHand || _felt.Aim is not {} target ||
                !int.TryParse(_power.Text, out var strength) || strength is < 1 or > 100)
                return;
            _shoot.Disabled = true;
            Move?.Invoke(target, strength, PoolAction.Shoot);
        };
    }

    public void ShowTable(PoolState pool, NetEntity? actor)
    {
        _felt.Balls = pool.Balls;
        _status.Text = pool.Status;
        var joined = actor is {} player && pool.Players.Contains(player);
        _canAct = joined && !pool.Moving && !pool.Finished &&
                  pool.Turn < pool.Players.Length && pool.Players[pool.Turn] == actor;
        var wasPlacing = _ballInHand;
        _ballInHand = pool.BallInHand;
        _felt.CanAim = _canAct;
        _felt.PlaceCue = _canAct && _ballInHand;
        if (!_canAct || _ballInHand || wasPlacing)
            _felt.ClearAim();
        _join.Disabled = joined || pool.Players.Length >= 2 || pool.Moving;
        _rack.Disabled = !joined || pool.Moving || (pool.Players.Length == 2 && !pool.Finished);
        _power.Editable = _canAct && !_ballInHand;
        _help.Text = !joined ? "Join to play. A second player starts a fresh two-player rack." :
            pool.Finished ? "Press Rack for a new game." :
            !_canAct ? "Wait for your turn and for the balls to stop." :
            _ballInHand ? "Ball in hand: click an empty spot on the felt to place the cue ball." :
            "Click the felt to aim. Set power from 1 to 100, then press Shoot.";
        _remaining.Text = $"Solids left: {pool.Balls.Skip(1).Take(7).Count(p => p.X >= 0)}    " +
                          $"Stripes left: {pool.Balls.Skip(9).Count(p => p.X >= 0)}";
        UpdateShootButton();
    }

    private void UpdateShootButton()
        => _shoot.Disabled = !_canAct || _ballInHand || !_felt.HasAim ||
                             !int.TryParse(_power.Text, out var power) || power is < 1 or > 100;
}

public sealed class PoolFelt : Control
{
    private static readonly Vector2 Rail = new(6, 6);
    private Vector2? _hover;
    public Vector2? Aim { get; private set; }
    public bool HasAim => Aim is {} aim && Balls.Length > 0 && Vector2.DistanceSquared(aim, Balls[0]) >= 1;
    public bool CanAim;
    public bool PlaceCue;
    public Vector2[] Balls = [];
    public event Action<Vector2>? PointSelected;

    public PoolFelt()
    {
        MinSize = new Vector2(492, 252);
        MouseFilter = MouseFilterMode.Stop;
        OnMouseExited += _ => _hover = null;
    }

    public void ClearAim() => Aim = null;

    protected override void MouseMove(GUIMouseMoveEventArgs args)
        => _hover = AimPoint(args.RelativePosition);

    private static Vector2 AimPoint(Vector2 position)
        => Vector2.Clamp(position - Rail, Vector2.Zero, new Vector2(480, 240));

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (!CanAim || args.Function != Robust.Shared.Input.EngineKeyFunctions.UIClick)
            return;

        args.Handle();
        var point = AimPoint(args.RelativePosition);
        Aim = point;
        PointSelected?.Invoke(point);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var scale = UIScale;
        handle.DrawRect(new UIBox2(0, 0, 492 * scale, 252 * scale), Color.FromHex("#714727"));
        handle.DrawRect(new UIBox2(6 * scale, 6 * scale, 486 * scale, 246 * scale), Color.FromHex("#285841"));
        foreach (var x in new[] { 6, 246, 486 })
        foreach (var y in new[] { 6, 246 })
            handle.DrawCircle(new Vector2(x, y) * scale, 9 * scale, Color.Black);

        if (CanAim && (Aim ?? _hover) is {} aim && Balls.Length > 0 && Balls[0].X >= 0)
        {
            if (PlaceCue)
                handle.DrawCircle((aim + Rail) * scale, 6 * scale, Color.FromHex("#b6d6bd"), false);
            var delta = aim - Balls[0];
            if (!PlaceCue && delta.LengthSquared() >= 1)
            {
                var direction = Vector2.Normalize(delta);
                var center = Balls[0] + Rail;
                var tip = center - direction * 8;
                var butt = center - direction * 70;
                handle.DrawLine((center + direction * 7) * scale, (aim + Rail) * scale, Color.FromHex("#a5c8ab"));
                handle.DrawLine(butt * scale, tip * scale, Color.FromHex("#dda866"));
                handle.DrawLine((tip - direction * 6) * scale, tip * scale, Color.White);
                handle.DrawCircle((aim + Rail) * scale, 4 * scale, Color.FromHex("#a5c8ab"), false);
            }
        }

        for (var i = 0; i < Balls.Length; i++)
        {
            var point = Balls[i];
            if (point.X < 0)
                continue;
            point = (point + Rail) * scale;
            var color = i switch
            {
                0 => Color.White,
                8 => Color.Black,
                1 or 9 => Color.FromHex("#efc341"),
                2 or 10 => Color.FromHex("#3977d6"),
                3 or 11 => Color.FromHex("#d74640"),
                4 or 12 => Color.FromHex("#9451bc"),
                5 or 13 => Color.FromHex("#e98a35"),
                6 or 14 => Color.FromHex("#319b65"),
                _ => Color.FromHex("#873b38")
            };
            handle.DrawCircle(point, 7 * scale, Color.FromHex("#111a15"));
            handle.DrawCircle(point, 6 * scale, color);
            if (i > 8)
                handle.DrawRect(new UIBox2(point.X - 5 * scale, point.Y - scale, point.X + 5 * scale, point.Y + scale), Color.White);
            handle.DrawCircle(point + new Vector2(-2, -2) * scale, 1.4f * scale, Color.FromHex("#e1e7df"));
        }
    }
}
