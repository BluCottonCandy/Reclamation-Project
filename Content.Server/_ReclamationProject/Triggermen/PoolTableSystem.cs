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
    public bool Pocketed;
    public bool Scratch;
    public bool? FirstPlayerSolids;
    public bool LegalEight;
    public float Elapsed;
}

public sealed class PoolTableSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    private static readonly Vector2[] Pockets = [new(0,0), new(240,0), new(480,0), new(0,240), new(240,240), new(480,240)];

    public override void Initialize()
    {
        Subs.BuiEvents<PoolTableComponent>(PoolKey.Key, subs =>
        {
            subs.Event<PoolMove>(OnMove);
            subs.Event<BoundUIClosedEvent>(OnClose);
        });
    }

    private void OnClose(EntityUid uid, PoolTableComponent table, BoundUIClosedEvent args)
    {
        if (!table.Players.Remove(args.Actor))
            return;

        table.Turn = 0;
        table.FirstPlayerSolids = null;
        if (table.Players.Count == 0)
            Reset(table);
        Publish(uid, table);
    }

    private void OnMove(EntityUid uid, PoolTableComponent table, PoolMove args)
    {
        if (args.Join)
        {
            if (table.Players.Count == 0 || (table.Finished && table.Players.Contains(args.Actor)))
                Reset(table);
            if (table.Players.Count < 2 && !table.Players.Contains(args.Actor))
                table.Players.Add(args.Actor);
            Publish(uid, table);
            return;
        }

        if (table.Moving || table.Finished || table.Players.Count == 0 ||
            table.Players[table.Turn] != args.Actor || args.Power is < 1 or > 100 ||
            !float.IsFinite(args.Point.X) || !float.IsFinite(args.Point.Y))
            return;

        var direction = Vector2.Clamp(args.Point, Vector2.Zero, new Vector2(480,240)) - table.Balls[0];
        if (direction.LengthSquared() < 1)
            return;

        table.Pocketed = table.Scratch = false;
        table.Velocity[0] = Vector2.Normalize(direction) * (40 + args.Power * 4);
        table.Moving = true;
        Publish(uid, table);
    }

    private static void Reset(PoolTableComponent table)
    {
        table.Turn = 0;
        table.Moving = table.Finished = false;
        table.FirstPlayerSolids = null;
        table.Elapsed = 0;
        Array.Clear(table.Velocity);
        table.Balls[0] = new(115,120);
        var index = 1;
        for (var row = 0; row < 5; row++)
        for (var col = 0; col <= row; col++)
            table.Balls[index++] = new(335 + row * 11, 120 + (col - row * 0.5f) * 13);
        (table.Balls[8], table.Balls[5]) = (table.Balls[5], table.Balls[8]);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<PoolTableComponent>();
        while (query.MoveNext(out var uid, out var table))
        {
            if (!table.Moving)
                continue;

            table.Elapsed += frameTime;
            if (table.Elapsed < 0.05f)
                continue;

            table.Elapsed = 0;
            for (var step = 0; step < 5; step++)
                Advance(table);

            if (table.Velocity.All(v => v == Vector2.Zero))
            {
                table.Moving = false;
                if (table.Scratch)
                {
                    table.Balls[0] = new(115,120);
                    for (var i = 0; i < 12 && table.Balls.Skip(1).Any(ball =>
                             Vector2.DistanceSquared(ball, table.Balls[0]) < 144); i++)
                        table.Balls[0].Y += 8;
                }
                if (table.Balls[8].X >= 0 && table.Players.Count == 2 && (!table.Pocketed || table.Scratch))
                    table.Turn = 1 - table.Turn;
            }
            Publish(uid, table);
        }
    }

    private static void Advance(PoolTableComponent table)
    {
        for (var i = 0; i < 16; i++)
        {
            var p = table.Balls[i];
            if (p.X < 0)
                continue;
            var v = table.Velocity[i] * 0.987f;
            p += v * 0.01f;
            if (Pockets.Any(pocket => Vector2.DistanceSquared(p, pocket) < 160))
            {
                table.Balls[i] = new(-1,-1);
                table.Velocity[i] = Vector2.Zero;
                table.Scratch |= i == 0;
                if (i == 8)
                {
                    table.LegalEight = table.Players.Count < 2
                        ? table.Balls.Skip(1).All(ball => ball.X < 0)
                        : table.FirstPlayerSolids is {} solids &&
                          GroupCleared(table, solids == (table.Turn == 0));
                }
                else if (i != 0)
                {
                    if (table.Players.Count == 2)
                        table.FirstPlayerSolids ??= (i < 8) == (table.Turn == 0);

                    table.Pocketed |= table.FirstPlayerSolids is not {} firstSolids ||
                        (i < 8) == (firstSolids == (table.Turn == 0));
                }
                continue;
            }

            if ((p.X < 6 && v.X < 0) || (p.X > 474 && v.X > 0))
                v.X *= -0.8f;
            if ((p.Y < 6 && v.Y < 0) || (p.Y > 234 && v.Y > 0))
                v.Y *= -0.8f;
            table.Balls[i] = Vector2.Clamp(p, new(6,6), new(474,234));
            table.Velocity[i] = v.LengthSquared() > 9 ? v : Vector2.Zero;
        }

        for (var i = 0; i < 16; i++)
        for (var j = i + 1; j < 16; j++)
        {
            if (table.Balls[i].X < 0 || table.Balls[j].X < 0)
                continue;
            var delta = table.Balls[j] - table.Balls[i];
            var length = delta.Length();
            if (length >= 12 || length < 0.01f)
                continue;
            var normal = delta / length;
            var correction = normal * ((12 - length) / 2);
            table.Balls[i] -= correction;
            table.Balls[j] += correction;
            var speed = Vector2.Dot(table.Velocity[i] - table.Velocity[j], normal);
            if (speed <= 0)
                continue;
            table.Velocity[i] -= normal * speed;
            table.Velocity[j] += normal * speed;
        }
    }

    private static bool GroupCleared(PoolTableComponent table, bool solids)
        => (solids ? table.Balls[1..8] : table.Balls[9..]).All(ball => ball.X < 0);

    private void Publish(EntityUid uid, PoolTableComponent table)
    {
        var status = table.Players.Count == 0 ? "Join to start solo practice." :
            table.Moving ? "Balls are rolling…" : $"Turn: {Name(table.Players[table.Turn])}";
        if (!table.Moving && table.Players.Count == 2 && table.FirstPlayerSolids is {} firstSolids)
            status += firstSolids == (table.Turn == 0) ? " (solids)" : " (stripes)";
        if (!table.Moving && table.Players.Count > 0 && table.Balls[8].X < 0)
        {
            table.Finished = true;
            var won = table.LegalEight && !table.Scratch;
            status = table.Players.Count == 2
                ? $"{Name(table.Players[won ? table.Turn : 1 - table.Turn])} wins! Join / rack to play again."
                : won ? "Table cleared! Join / rack to play again."
                    : "Eight ball foul. Join / rack to play again.";
        }

        _ui.SetUiState(uid, PoolKey.Key, new PoolState((Vector2[]) table.Balls.Clone(), status));
    }
}
