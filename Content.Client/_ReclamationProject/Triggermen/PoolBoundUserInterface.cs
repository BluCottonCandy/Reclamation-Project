using System.Numerics;
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
        _window.Move += (point, power, join) => SendMessage(new PoolMove(point, power, join));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is PoolState pool)
            _window?.ShowTable(pool);
    }
}

public sealed class PoolWindow : DefaultWindow
{
    private readonly PoolFelt _felt = new();
    private readonly Label _status = new() { Text = "Join to play solo or with a second player." };
    public event Action<Vector2, int, bool>? Move;

    public PoolWindow()
    {
        Title = "Lucky 38 Pool";
        var layout = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        Contents.AddChild(layout);
        layout.AddChild(_status);
        layout.AddChild(_felt);
        var options = new BoxContainer { SeparationOverride = 8 };
        var join = new Button { Text = "Join / Rack" };
        var power = new LineEdit { Text = "75", MinSize = new Vector2(60, 0) };
        options.AddChild(join);
        options.AddChild(new Label { Text = "Power (1–100)" });
        options.AddChild(power);
        layout.AddChild(options);
        layout.AddChild(new Label { Text = "Set your power and click the felt to aim and shoot." });
        join.OnPressed += _ => Move?.Invoke(Vector2.Zero, 0, true);
        _felt.Shoot += point =>
        {
            if (int.TryParse(power.Text, out var strength))
                Move?.Invoke(point, strength, false);
        };
    }

    public void ShowTable(PoolState pool)
    {
        _felt.Balls = pool.Balls;
        _status.Text = pool.Status;
    }
}

public sealed class PoolFelt : Control
{
    private static readonly Vector2 Rail = new(6, 6);
    private Vector2? _aim;
    public Vector2[] Balls = [];
    public event Action<Vector2>? Shoot;

    public PoolFelt()
    {
        MinSize = new Vector2(492, 252);
        MouseFilter = MouseFilterMode.Stop;
        OnMouseExited += _ => _aim = null;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
        => _aim = AimPoint(args.RelativePosition);

    private static Vector2 AimPoint(Vector2 position)
        => Vector2.Clamp(position - Rail, Vector2.Zero, new Vector2(480, 240));

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function != Robust.Shared.Input.EngineKeyFunctions.UIClick)
            return;

        args.Handle();
        var point = AimPoint(args.RelativePosition);
        _aim = point;
        Shoot?.Invoke(point);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var scale = UIScale;
        handle.DrawRect(new UIBox2(0, 0, 492 * scale, 252 * scale), Color.FromHex("#714727"));
        handle.DrawRect(new UIBox2(6 * scale, 6 * scale, 486 * scale, 246 * scale), Color.FromHex("#285841"));
        foreach (var x in new[] { 6, 246, 486 })
        foreach (var y in new[] { 6, 246 })
            handle.DrawCircle(new Vector2(x, y) * scale, 9 * scale, Color.Black);

        if (_aim is {} aim && Balls.Length > 0 && Balls[0].X >= 0)
        {
            var delta = aim - Balls[0];
            if (delta.LengthSquared() >= 1)
            {
                var direction = Vector2.Normalize(delta);
                var center = Balls[0] + Rail;
                var tip = center - direction * 8;
                var butt = center - direction * 70;
                handle.DrawLine((center + direction * 7) * scale, (aim + Rail) * scale, Color.FromHex("#a5c8ab"));
                handle.DrawLine(butt * scale, tip * scale, Color.FromHex("#dda866"));
                handle.DrawLine((tip - direction * 6) * scale, tip * scale, Color.White);
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
                < 8 => Color.FromHex("#e9b94d"),
                _ => Color.FromHex("#bb5141")
            };
            handle.DrawCircle(point, 6 * scale, color);
            if (i > 8)
                handle.DrawRect(new UIBox2(point.X - 5 * scale, point.Y - scale, point.X + 5 * scale, point.Y + scale), Color.White);
        }
    }
}
