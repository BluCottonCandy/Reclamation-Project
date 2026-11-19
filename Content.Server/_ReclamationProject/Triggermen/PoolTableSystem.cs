using System.Linq;
using System.Numerics;
using Content.Shared._ReclamationProject.Triggermen;

namespace Content.Server._ReclamationProject.Triggermen;

[RegisterComponent]
public sealed partial class PoolTableComponent : Component
{
    public readonly List<EntityUid> Players = [];
    public readonly Vector2[] Balls = new Vector2[16];
    public readonly Vector2[] Velocity = new Vector2[16];
    public int Turn;
    public bool Moving;
    public bool Finished;
    public bool PocketedSolids;
    public bool PocketedStripes;
    public bool Scratch;
    public bool BallInHand;
    public bool? FirstPlayerSolids;
    public bool LegalEight;
    public int FirstHit = -1;
    public int FirstPocketed = -1;
    public float Elapsed;
    public float BroadcastElapsed;
    public string Result = "";
}

public sealed class PoolTableSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    private const float Step = 0.01f;
    private static readonly Vector2[] Pockets = [new(0,0), new(240,0), new(480,0), new(0,240), new(240,240), new(480,240)];
    private static readonly int[] RackOrder = [1, 9, 2, 10, 8, 3, 11, 4, 12, 5, 6, 13, 7, 14, 15];

    public override void Initialize()
    {
        Subs.BuiEvents<PoolTableComponent>(PoolKey.Key, subs =>
        {
            subs.Event<PoolMove>(OnMove);
            subs.Event<BoundUIClosedEvent>(OnClose);
            subs.Event<BoundUIOpenedEvent>((uid, table, _) =>
            {
                if (table.Players.Count == 0)
                    Reset(table);
                Publish(uid, table);
            });
        });
    }

    private void OnClose(EntityUid uid, PoolTableComponent table, BoundUIClosedEvent args)
    {
        if (!table.Players.Remove(args.Actor))
            return;

        // A departing player's shot must not continue under somebody else's turn.
        Reset(table);
        Publish(uid, table);
    }

    private void OnMove(EntityUid uid, PoolTableComponent table, PoolMove args)
    {
        if (args.Action == PoolAction.Join)
        {
            if (table.Moving || table.Players.Count == 2 || table.Players.Contains(args.Actor))
                return;
            table.Players.Add(args.Actor);
            Reset(table);
            Publish(uid, table);
            return;
        }

        if (args.Action == PoolAction.Rack)
        {
            if (table.Moving || !table.Players.Contains(args.Actor) || (table.Players.Count == 2 && !table.Finished))
                return;
            Reset(table);
            Publish(uid, table);
            return;
        }

        if (table.Moving || table.Finished || table.Players.Count == 0 ||
            table.Players[table.Turn] != args.Actor ||
            !float.IsFinite(args.Point.X) || !float.IsFinite(args.Point.Y))
            return;

        if (args.Action == PoolAction.PlaceCue)
        {
            if (!table.BallInHand || !CanPlaceCue(table, args.Point))
                return;
            table.Balls[0] = args.Point;
            table.BallInHand = false;
            table.Result = "";
            Publish(uid, table);
            return;
        }
        if (args.Action != PoolAction.Shoot || table.BallInHand || args.Power is < 1 or > 100)
            return;
        var direction = Vector2.Clamp(args.Point, Vector2.Zero, new Vector2(480,240)) - table.Balls[0];
        if (direction.LengthSquared() < 1)
            return;

        table.PocketedSolids = table.PocketedStripes = table.Scratch = false;
        table.FirstHit = table.FirstPocketed = -1;
        table.LegalEight = table.Players.Count < 2
            ? GroupCleared(table, true) && GroupCleared(table, false)
            : table.FirstPlayerSolids is {} solids && GroupCleared(table, solids == (table.Turn == 0));
        table.Elapsed = table.BroadcastElapsed = 0;
        table.Result = "";
        table.Velocity[0] = Vector2.Normalize(direction) * (40 + args.Power * 6);
        table.Moving = true;
        Publish(uid, table);
    }

    private static void Reset(PoolTableComponent table)
    {
        table.Turn = 0;
        table.Moving = table.Finished = table.Scratch = table.BallInHand = false;
        table.PocketedSolids = table.PocketedStripes = table.LegalEight = false;
        table.FirstHit = table.FirstPocketed = -1;
        table.FirstPlayerSolids = null;
        table.Elapsed = table.BroadcastElapsed = 0;
        table.Result = "";
        Array.Clear(table.Velocity);
        table.Balls[0] = new(115,120);
        var index = 0;
        for (var row = 0; row < 5; row++)
        for (var col = 0; col <= row; col++)
            table.Balls[RackOrder[index++]] = new(335 + row * 11, 120 + (col - row * 0.5f) * 13);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<PoolTableComponent>();
        while (query.MoveNext(out var uid, out var table))
        {
            if (!table.Moving)
                continue;

            // Preserve fractional simulation time; bound catch-up after a server stall.
            table.Elapsed += Math.Clamp(frameTime, 0, 0.25f);
            table.BroadcastElapsed += frameTime;
            while (table.Elapsed + 0.000001f >= Step && table.Moving)
            {
                table.Elapsed = Math.Max(0, table.Elapsed - Step);
                Advance(table);
                if (table.Velocity.All(v => v == Vector2.Zero))
                    FinishShot(table);
            }
            if (!table.Moving || table.BroadcastElapsed >= 0.05f)
            {
                table.BroadcastElapsed %= 0.05f;
                Publish(uid, table);
            }
        }
    }

    private void FinishShot(PoolTableComponent table)
    {
        table.Moving = false;
        table.Elapsed = 0;
        var wrongFirst = table.FirstHit < 0 || (table.LegalEight
            ? table.FirstHit != 8
            : table.FirstHit == 8 || (table.Players.Count == 2 && table.FirstPlayerSolids is {} firstSolids &&
                (table.FirstHit < 8) != (firstSolids == (table.Turn == 0))));
        var foul = table.Scratch || wrongFirst;
        if (table.Balls[8].X < 0)
        {
            table.Finished = true;
            var won = table.LegalEight && !foul;
            table.Result = table.Players.Count == 2
                ? $"{Name(table.Players[won ? table.Turn : 1 - table.Turn])} wins! Rack to play again."
                : won ? "Table cleared! Rack to play again." : "Eight ball foul. Rack to try again.";
            return;
        }
        if (!foul && table.Players.Count == 2 && table.FirstPocketed > 0)
            table.FirstPlayerSolids ??= (table.FirstPocketed < 8) == (table.Turn == 0);
        var scored = table.FirstPlayerSolids is {} solids
            ? (solids == (table.Turn == 0) ? table.PocketedSolids : table.PocketedStripes)
            : table.PocketedSolids || table.PocketedStripes;
        if (table.Players.Count == 2 && (foul || !scored))
            table.Turn = 1 - table.Turn;
        if (!foul)
            return;
        table.BallInHand = true;
        table.Result = table.Scratch ? "Scratch. " : "Foul: hit your own group first. ";
        if (table.Balls[0].X >= 0)
            return;
        // Show a safe provisional spot until the next player places the cue ball.
        for (var x = 24; x < 480; x += 24)
        for (var y = 24; y < 240; y += 24)
        {
            var point = new Vector2(x, y);
            if (!CanPlaceCue(table, point))
                continue;
            table.Balls[0] = point;
            return;
        }
    }

    private static bool CanPlaceCue(PoolTableComponent table, Vector2 point)
        => point.X is >= 14 and <= 466 && point.Y is >= 14 and <= 226 &&
           table.Balls.Skip(1).All(ball => ball.X < 0 || Vector2.DistanceSquared(ball, point) >= 169) &&
           Pockets.All(pocket => Vector2.DistanceSquared(pocket, point) >= 225);

    private static void Advance(PoolTableComponent table)
    {
        for (var i = 0; i < 16; i++)
        {
            var p = table.Balls[i];
            if (p.X < 0)
                continue;
            var v = table.Velocity[i] * 0.992f;
            p += v * Step;
            if (Pockets.Any(pocket => Vector2.DistanceSquared(p, pocket) < 196))
            {
                table.Balls[i] = new(-1,-1);
                table.Velocity[i] = Vector2.Zero;
                table.Scratch |= i == 0;
                if (i is not (0 or 8))
                {
                    if (table.FirstPocketed < 0)
                        table.FirstPocketed = i;
                    table.PocketedSolids |= i < 8;
                    table.PocketedStripes |= i > 8;
                }
                continue;
            }

            if ((p.X < 6 && v.X < 0) || (p.X > 474 && v.X > 0))
                v.X *= -0.88f;
            if ((p.Y < 6 && v.Y < 0) || (p.Y > 234 && v.Y > 0))
                v.Y *= -0.88f;
            table.Balls[i] = Vector2.Clamp(p, new(6,6), new(474,234));
            table.Velocity[i] = v.LengthSquared() > 16 ? v : Vector2.Zero;
        }

        for (var i = 0; i < 16; i++)
        for (var j = i + 1; j < 16; j++)
        {
            if (table.Balls[i].X < 0 || table.Balls[j].X < 0)
                continue;
            var delta = table.Balls[j] - table.Balls[i];
            var length = delta.Length();
            if (length >= 12)
                continue;
            var normal = length < 0.001f ? Vector2.UnitX : delta / length;
            var correction = normal * ((12 - length) / 2);
            table.Balls[i] -= correction;
            table.Balls[j] += correction;
            var speed = Vector2.Dot(table.Velocity[i] - table.Velocity[j], normal);
            if (speed <= 0)
                continue;
            if (i == 0 && table.FirstHit < 0)
                table.FirstHit = j;
            table.Velocity[i] -= normal * speed;
            table.Velocity[j] += normal * speed;
        }
    }

    private static bool GroupCleared(PoolTableComponent table, bool solids)
        => (solids ? table.Balls[1..8] : table.Balls[9..]).All(ball => ball.X < 0);

    private void Publish(EntityUid uid, PoolTableComponent table)
    {
        var status = table.Players.Count == 0 ? "Join to start solo practice." :
            table.Moving ? "Balls are rolling..." : $"Turn: {Name(table.Players[table.Turn])}";
        if (!table.Moving && table.Players.Count == 2 && table.FirstPlayerSolids is {} firstSolids)
            status += firstSolids == (table.Turn == 0) ? " (solids)" : " (stripes)";
        if (table.BallInHand)
            status = table.Result + status + " — ball in hand.";
        if (table.Finished)
            status = table.Result;
        _ui.SetUiState(uid, PoolKey.Key, new PoolState((Vector2[]) table.Balls.Clone(), status,
            table.Players.Select(player => GetNetEntity(player)).ToArray(), table.Turn,
            table.Moving, table.Finished, table.BallInHand));
    }
}
