using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Triggermen;

[Serializable, NetSerializable]
public enum SlotsKey { Key }

[Serializable, NetSerializable]
public sealed class SlotsBet(int amount) : BoundUserInterfaceMessage
{
    public int Amount { get; } = amount;
}

[Serializable, NetSerializable]
public sealed class SlotsResult(int[] reels, int prize) : BoundUserInterfaceState
{
    public int[] Reels { get; } = reels;
    public int Prize { get; } = prize;
}

[Serializable, NetSerializable]
public enum BlackjackKey { Key }

[Serializable, NetSerializable]
public sealed class BlackjackMove(int amount, bool hold = false) : BoundUserInterfaceMessage
{
    public int Amount { get; } = amount;
    public bool Hold { get; } = hold;
}

[Serializable, NetSerializable]
public sealed class BlackjackState(int[] player, int[] dealer, bool active, string result) : BoundUserInterfaceState
{
    public int[] Player { get; } = player;
    public int[] Dealer { get; } = dealer;
    public bool Active { get; } = active;
    public string Result { get; } = result;
}
