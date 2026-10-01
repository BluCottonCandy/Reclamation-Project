// #Misfits — MisfitsLastRealTickEvent is no longer used.
// The server now reads LastRealTick directly from RequestShootEvent and RequestPerformActionEvent,
// which are already sent as predictive events. A separate periodic heartbeat caused
// "Got late MsgEntity" spam because RaiseNetworkEvent sends tick-stamped MsgEntity messages
// that always arrive 1-4 ticks late relative to the server's current tick.
//
// This file is kept for history; do not use MisfitsLastRealTickEvent.

// using Robust.Shared.Serialization;
// using Robust.Shared.Timing;
//
// namespace Content.Shared._Misfits.Movement;
//
// [Serializable, NetSerializable]
// public sealed class MisfitsLastRealTickEvent : EntityEventArgs
// {
//     public GameTick Tick;
//     public MisfitsLastRealTickEvent() { }
//     public MisfitsLastRealTickEvent(GameTick tick) => Tick = tick;
// }
