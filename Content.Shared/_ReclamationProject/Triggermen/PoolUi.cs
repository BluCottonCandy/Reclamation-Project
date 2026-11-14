using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Triggermen;

[Serializable, NetSerializable]
public enum PoolKey { Key }

[Serializable, NetSerializable]
public sealed class PoolMove(Vector2 point, int power, bool join) : BoundUserInterfaceMessage
{
    public Vector2 Point => point;
    public int Power => power;
    public bool Join => join;
}

[Serializable, NetSerializable]
public sealed class PoolState(Vector2[] balls, string status) : BoundUserInterfaceState
{
    public Vector2[] Balls => balls;
    public string Status => status;
}
