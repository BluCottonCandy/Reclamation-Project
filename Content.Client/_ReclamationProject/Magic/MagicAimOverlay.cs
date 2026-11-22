using System.Numerics;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Actions;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - shows where a spell will reach, in its class colour, only to the caster:
/// - while aiming: a faint ring for how far you can cast it, plus the blast shape at the mouse
///   (circle for zones and bursts, a cone or a line from you toward the mouse);
/// - while charging a spell that hits around you: the circle it will cover.
/// </summary>
public sealed class MagicAimOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly IEntityManager _ents;
    private readonly IPlayerManager _player;
    private readonly IEyeManager _eye;
    private readonly IInputManager _input;
    private readonly IPrototypeManager _proto;
    private readonly SharedTransformSystem _transform;
    private readonly IUserInterfaceManager _ui;

    private static readonly Dictionary<MagicClass, Color> Colors = new()
    {
        [MagicClass.Elemental] = Color.FromHex("#ff9a5c"),
        [MagicClass.Atomic] = Color.FromHex("#9cff5a"),
        [MagicClass.Psychic] = Color.FromHex("#78b4ff"),
        [MagicClass.Dark] = Color.FromHex("#9a7cff"),
        [MagicClass.Blood] = Color.FromHex("#ff4a4a"),
        [MagicClass.Healing] = Color.FromHex("#8ff0a8"),
        [MagicClass.Conjuration] = Color.FromHex("#ffcf5a"),
    };

    public MagicAimOverlay(IEntityManager ents, IPlayerManager player, IEyeManager eye, IInputManager input,
        IPrototypeManager proto, SharedTransformSystem transform, IUserInterfaceManager ui)
    {
        _ents = ents;
        _player = player;
        _eye = eye;
        _input = input;
        _proto = proto;
        _transform = transform;
        _ui = ui;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } me)
            return;
        var origin = _transform.GetMapCoordinates(me);
        if (origin.MapId == MapId.Nullspace || origin.MapId != args.MapId)
            return;

        var actions = _ents.System<SharedActionsSystem>();
        var aiming = _ui.GetUIController<ActionUIController>().SelectingTargetFor;
        _ents.TryGetComponent<MagicCasterComponent>(me, out var caster);

        // What to show: the spell being aimed, else one being charged.
        EntityUid? actionId = aiming is { } a && _ents.HasComponent<MagicSpellActionComponent>(a) ? a : caster?.ChargingAction;
        if (actionId is not { } action || !actions.TryGetActionData(action, out var data))
            return;

        var color = Color.White;
        if (_ents.TryGetComponent<MagicSpellActionComponent>(action, out var spellAction) && spellAction.Spell is { } id
            && _proto.TryIndex(id, out var spell) && Colors.TryGetValue(spell.Class, out var c))
            color = c;

        var handle = args.WorldHandle;
        var mouse = _eye.PixelToMap(_input.MouseScreenPosition);
        var mouseOk = mouse.MapId == origin.MapId && aiming == action;
        var me2 = origin.Position;

        switch (data)
        {
            case WorldTargetActionComponent { Event: MagicZoneSpellEvent zone } world:
                if (!mouseOk)
                    return;
                RangeRing(handle, me2, world.Range, color);
                if (zone.Shape == MagicShape.Circle)
                {
                    Area(handle, mouse.Position, MathF.Max(zone.Radius, 0.5f), color);
                }
                else
                {
                    var dir = mouse.Position - me2;
                    if (dir.LengthSquared() < 0.01f)
                        dir = new Vector2(0, 1);
                    dir = Vector2.Normalize(dir);
                    if (zone.Shape == MagicShape.Cone)
                        Cone(handle, me2, dir, zone.Length, color);
                    else
                        Line(handle, me2, dir, zone.Length, MathF.Max(zone.Radius, 0.8f), color);
                }
                break;

            case EntityTargetActionComponent { Event: MagicTouchSpellEvent touch } entity:
                if (!mouseOk)
                    return;
                RangeRing(handle, me2, entity.Range, color);
                if (touch.BurstRadius > 0)
                    Area(handle, mouse.Position, touch.BurstRadius, color);
                break;

            case BaseTargetActionComponent target:
                if (mouseOk && target.Range > 0)
                    RangeRing(handle, me2, target.Range, color);
                break;

            case InstantActionComponent { Event: MagicAreaSpellEvent area } when area.Radius > 0:
                Area(handle, me2, area.Radius, color);
                break;
        }
    }

    private static void RangeRing(DrawingHandleWorld h, Vector2 center, float range, Color color)
    {
        if (range <= 0f || range > 30f)
            return;
        h.DrawCircle(center, range, color.WithAlpha(0.35f), filled: false);
    }

    private static void Area(DrawingHandleWorld h, Vector2 center, float radius, Color color)
    {
        h.DrawCircle(center, radius, color.WithAlpha(0.18f), filled: true);
        h.DrawCircle(center, radius, color.WithAlpha(0.8f), filled: false);
    }

    private static void Cone(DrawingHandleWorld h, Vector2 from, Vector2 dir, float length, Color color)
    {
        // About 45 degrees each side, matching how the spell picks its targets.
        const int steps = 12;
        var half = MathF.PI / 4f;
        var baseAngle = MathF.Atan2(dir.Y, dir.X);
        var fill = color.WithAlpha(0.18f);
        var edge = color.WithAlpha(0.8f);
        var prev = from + new Vector2(MathF.Cos(baseAngle - half), MathF.Sin(baseAngle - half)) * length;
        h.DrawLine(from, prev, edge);
        for (var i = 1; i <= steps; i++)
        {
            var ang = baseAngle - half + 2f * half * i / steps;
            var next = from + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * length;
            h.DrawPrimitives(DrawPrimitiveTopology.TriangleList, new[] { from, prev, next }, fill);
            h.DrawLine(prev, next, edge);
            prev = next;
        }
        h.DrawLine(from, prev, edge);
    }

    private static void Line(DrawingHandleWorld h, Vector2 from, Vector2 dir, float length, float halfWidth, Color color)
    {
        var side = new Vector2(-dir.Y, dir.X) * halfWidth;
        var end = from + dir * length;
        var a = from + side;
        var b = from - side;
        var c = end - side;
        var d = end + side;
        h.DrawPrimitives(DrawPrimitiveTopology.TriangleList, new[] { a, b, c, a, c, d }, color.WithAlpha(0.18f));
        var edge = color.WithAlpha(0.8f);
        h.DrawLine(a, d, edge);
        h.DrawLine(b, c, edge);
        h.DrawLine(d, c, edge);
    }
}
