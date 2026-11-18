// #Reclamation Project - medieval weapons: crossbow arrows hit harder than bow arrows.
using Content.Shared._ReclamationProject.Medieval;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server._ReclamationProject.Medieval;

public sealed class CrossbowBoltSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrossbowComponent, AmmoShotEvent>(OnShot);
        SubscribeLocalEvent<CrossbowBoltComponent, ProjectileHitEvent>(OnHit);
        SubscribeLocalEvent<CrossbowBoltComponent, LandEvent>(OnLand);
    }

    private void OnShot(EntityUid uid, CrossbowComponent comp, AmmoShotEvent args)
    {
        foreach (var shot in args.FiredProjectiles)
            EnsureComp<CrossbowBoltComponent>(shot).BonusDamage = comp.BonusDamage;
    }

    private void OnLand(EntityUid uid, CrossbowBoltComponent comp, ref LandEvent args)
    {
        RemCompDeferred<CrossbowBoltComponent>(uid);
    }

    private void OnHit(EntityUid uid, CrossbowBoltComponent comp, ref ProjectileHitEvent args)
    {
        args.Damage = args.Damage + comp.BonusDamage;
        RemCompDeferred<CrossbowBoltComponent>(uid);
    }
}
