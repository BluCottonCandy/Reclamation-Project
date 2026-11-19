using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Triggermen;

[Serializable, NetSerializable]
public enum PoolKey { Key }

[Serializable, NetSerializable]
public enum PoolAction : byte { Join, Shoot, Rack, PlaceCue }

[Serializable, NetSerializable]
public sealed class PoolMove(Vector2 point, int power, PoolAction action) : BoundUserInterfaceMessage
{
    public Vector2 Point => point;
    public int Power => power;
    public PoolAction Action => action;
}

[Serializable, NetSerializable]
public sealed class PoolState(Vector2[] balls, string status, NetEntity[] players, int turn,
    bool moving, bool finished, bool ballInHand) : BoundUserInterfaceState
{
    public Vector2[] Balls => balls;
    public string Status => status;
    public NetEntity[] Players => players;
    public int Turn => turn;
    public bool Moving => moving;
    public bool Finished => finished;
    public bool BallInHand => ballInHand;
}
