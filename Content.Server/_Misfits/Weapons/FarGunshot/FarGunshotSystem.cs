// #Misfits Add - Far gunshot system. Plays a distant "boom" for players beyond PVS range of the shooter.
// Inspired by a similar directional far-sound concept from stalker-14-EN, built independently for N14.
using Content.Shared.Weapons.Ranged.Events;
using Robust.Server.Audio;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Weapons.FarGunshot;

/// <summary>
/// Listens for <see cref="AmmoShotEvent"/> on any entity with <see cref="FarGunshotComponent"/>
/// and plays a distant gunshot sound to all players beyond <see cref="FarGunshotComponent.MinDistance"/> tiles.
/// This gives the wasteland a sense of distant combat without spamming the nearby PVS audio.
/// </summary>
public sealed class FarGunshotSystem : EntitySystem
{
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    // Default distant sound: uses the existing "small explosion far" which has the right muffled boom quality.
    private static readonly SoundPathSpecifier DefaultFarSound =
        new("/Audio/Effects/explosionsmallfar.ogg");

    // Misfits Fix: rate-limit per gun so rapid-fire weapons (full-auto, burst) don't iterate all
    // player sessions on every single bullet. 0.15 s gives one far-sound per ~3 bullets at 1200 RPM.
    private const double FarSoundMinIntervalSeconds = 0.15;
    private readonly Dictionary<EntityUid, TimeSpan> _lastFarSoundTime = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FarGunshotComponent, AmmoShotEvent>(OnAmmoShot);
        // Misfits Fix: clean up per-gun tracking when the weapon is deleted.
        SubscribeLocalEvent<FarGunshotComponent, ComponentShutdown>(OnFarGunshotShutdown);
    }

    private void OnFarGunshotShutdown(EntityUid uid, FarGunshotComponent _, ComponentShutdown args)
    {
        _lastFarSoundTime.Remove(uid);
    }

    private void OnAmmoShot(EntityUid gunUid, FarGunshotComponent comp, AmmoShotEvent args)
    {
        // Suppressed guns (silencers) skip the distant echo entirely.
        if (comp.Suppressed)
            return;

        // Misfits Fix: rate-limit so rapid-fire weapons don't iterate all player sessions every bullet.
        // One far-sound per gun per FarSoundMinIntervalSeconds is perceptually identical to per-bullet.
        var now = _timing.CurTime;
        if (_lastFarSoundTime.TryGetValue(gunUid, out var lastTime) &&
            (now - lastTime).TotalSeconds < FarSoundMinIntervalSeconds)
            return;
        _lastFarSoundTime[gunUid] = now;

        var sound = comp.FarSound ?? DefaultFarSound;

        var gunXform = Transform(gunUid);
        var gunMapId = gunXform.MapID;

        // Guard against invalid map (e.g. gun in hyperspace/nullspace).
        if (gunMapId == MapId.Nullspace)
            return;

        var gunPos = _transform.GetWorldPosition(gunXform);

        // Build a filter of all players whose pawns are:
        //   - on the same map as the gun
        //   - farther than MinDistance tiles (so close players use normal PVS audio)
        //   - within MaxDistance tiles (so nobody across the whole world hears it)
        var filter = Filter.Empty();

        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { } playerEnt)
                continue;

            var playerXform = Transform(playerEnt);
            if (playerXform.MapID != gunMapId)
                continue;

            var dist = (_transform.GetWorldPosition(playerXform) - gunPos).Length();
            if (dist >= comp.MinDistance && dist <= comp.MaxDistance)
                filter.AddPlayer(session);
        }

        if (filter.Count == 0)
            return;

        // Play the far sound at the gun's world position so the client renders it directionally.
        // Low volume + no rolloff (static distance) so it sounds like it carries across the wasteland.
        var audioParams = AudioParams.Default
            .WithVariation(0.08f)     // slight pitch variation per shot
            .WithVolume(-4f)          // quieter than normal gunshot
            .WithMaxDistance(comp.MaxDistance)
            .WithRolloffFactor(0.3f); // gentle falloff across the large distance range

        _audio.PlayStatic(sound, filter, gunXform.Coordinates, true, audioParams);
    }
}
