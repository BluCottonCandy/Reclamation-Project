using System;
using System.Numerics;
using Content.Server._ReclamationProject.Hallucinations;
using Content.Server.Body.Components;
using Content.Server.Mobs;
using Content.Server.GameTicking;
using Content.Shared.Mind;
using Content.Shared._ReclamationProject.Hallucinations;
using Content.Shared._ReclamationProject.Endurance;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Console;
using System.Threading.Tasks;
namespace Content.IntegrationTests.Tests._ReclamationProject;
[TestFixture]
public sealed class HallucinationPainTests
{
    [Test]
    public async Task CollapseIsReversibleWithoutHealingRealWoundsOrBleeding()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() => Verify(pair.Server.ResolveDependency<IEntityManager>()));
        await pair.CleanReturnAsync();
    }
    public static void Verify(IEntityManager em)
    {
  var map=em.System<SharedMapSystem>().CreateMap(out _,runMapInit:true);
  var painSystem=em.System<HallucinationPainSystem>();var mob=em.System<MobStateSystem>();var damage=em.System<DamageableSystem>();var thresholds=em.System<MobThresholdSystem>();
  void Check(bool ok,string why){if(!ok)throw new Exception(why);}
  EntityUid Patient(){var body=em.SpawnEntity("MobHuman",new EntityCoordinates(map,Vector2.Zero));em.EnsureComponent<UnstableComponent>(body);return body;}
  void Fresh(EntityUid body){if(em.TryGetComponent<HallucinationPainComponent>(body,out var p))p.NextBeginAt=TimeSpan.Zero;}
  void Collapse(EntityUid body,Guid id)
  {
   Fresh(body);Check(painSystem.Begin(body,id),"Could not begin legitimate encounter");
   for(var i=0;i<100&&!mob.IsCritical(body);i++)
   {
    var p=em.GetComponent<HallucinationPainComponent>(body);p.NextHitAt=TimeSpan.Zero;
    Check(painSystem.Hit(body,id,false),"Hit unexpectedly refused");
   }
   Check(mob.IsCritical(body),"Virtual damage failed to cause actual critical state");
  }
  void Recover(EntityUid body){em.GetComponent<HallucinationPainComponent>(body).CollapseUntil=TimeSpan.Zero;painSystem.Update(0);}
  var minds=em.System<SharedMindSystem>();
  var mind=minds.CreateMind(null);
  try
  {
   var user=Patient();damage.TryChangeDamage(user,new DamageSpecifier {DamageDict=new(){["Blunt"]=20}},ignoreResistances:true,doPartDamage:false);
   var before=em.GetComponent<DamageableComponent>(user).TotalDamage;
   var blood=em.GetComponent<BloodstreamComponent>(user);var bleed=blood.BleedAmount;
   var id=Guid.NewGuid();Check(painSystem.Begin(user,id),"Eligible begin refused");
   Check(!painSystem.Hit(user,Guid.NewGuid(),false),"Unrelated encounter id accepted");
   Check(painSystem.Hit(user,id,false),"First hit refused");
   Check(!painSystem.Hit(user,id,false),"Unbounded same-tick hit spam accepted");
   Check(thresholds.GetDisplayedDamage(user,before)==before+25,"Apparent damage did not reduce displayed health");
   Check(em.GetComponent<DamageableComponent>(user).TotalDamage==before&&blood.BleedAmount==bleed,"Hallucination hit changed real damage or bleeding");
   painSystem.Finish(user,id);Check(em.GetComponent<HallucinationPainComponent>(user).Amount==0,"Clearing enemies did not clear perceived damage");
   Check(!painSystem.Hit(user,id,false),"Late hit survived encounter end");
   Check(thresholds.GetDisplayedDamage(user,before)==before,"Displayed health was not restored after clearing enemies");
   var special=em.EnsureComponent<SpecialComponent>(user);special.BaseEndurance=10;
   Collapse(user,Guid.NewGuid());
   Check(em.GetComponent<MobStateActionsComponent>(user).GrantedActions.Count==0,"Fake crit granted critical actions");
   minds.TransferTo(mind.Owner,user);
   var critActions=em.System<CritMobActionsSystem>();
   for(var attempt=0;attempt<100;attempt++)
   {
    Check(!critActions.CanUseCriticalActions(user),"Fake crit allowed critical actions");
    var ghostAttempt=new GhostAttemptHandleEvent(mind.Comp,true);
    em.EventBus.RaiseEvent(EventSource.Local,ghostAttempt);
    Check(ghostAttempt.Handled&&!ghostAttempt.Result,"Ghost command bypassed fake-crit protection");
    em.EventBus.RaiseLocalEvent(user,new CritSuccumbEvent());
    em.EventBus.RaiseLocalEvent(user,new CritLastWordsEvent());
    em.EventBus.RaiseLocalEvent(user,new CritFakeDeathEvent());
   }
   Check(mob.IsCritical(user),"Critical action spam killed the hallucinating character");
   Check(!em.TryGetComponent<EnduranceEnragedComponent>(user,out var rage)||!rage.Active,"Fake crit consumed Enraged");
   Check(em.GetComponent<DamageableComponent>(user).TotalDamage==before&&blood.BleedAmount==bleed,"Collapse changed real wounds or bleeding");
   Recover(user);Check(mob.IsAlive(user),"Healthy-enough body did not recover from fake crit");
   Check(em.GetComponent<DamageableComponent>(user).TotalDamage==before,"Recovery healed pre-existing injuries");
   var injured=Patient();Collapse(injured,Guid.NewGuid());
   var limit=thresholds.GetThresholdForState(injured,MobState.Critical);
   damage.TryChangeDamage(injured,new DamageSpecifier {DamageDict=new(){["Blunt"]=limit+1}},ignoreResistances:true,doPartDamage:false);
   var real=em.GetComponent<DamageableComponent>(injured).TotalDamage;
   Recover(injured);Check(mob.IsCritical(injured)&&em.GetComponent<DamageableComponent>(injured).TotalDamage==real,"Real critical injuries were reversed");
   Check(critActions.CanUseCriticalActions(injured)&&em.GetComponent<MobStateActionsComponent>(injured).GrantedActions.Count>0,"Real crit actions were not restored after hallucination collapse");
   mob.UpdateMobState(injured);Check(mob.IsCritical(injured),"Later state refresh revived real critical damage from stale threshold cache");
   var dead=Patient();Collapse(dead,Guid.NewGuid());mob.ChangeMobState(dead,MobState.Dead);Recover(dead);Check(mob.IsDead(dead),"Recovery revived a real death");
   var sober=em.SpawnEntity("MobHuman",new EntityCoordinates(map,Vector2.Zero));Check(!painSystem.Begin(sober,Guid.NewGuid()),"Unaffected sober character accepted encounter");
   var immune=Patient();em.EnsureComponent<EnduranceEnragedComponent>(immune).Active=true;var other=Guid.NewGuid();Check(painSystem.Begin(immune,other)&&!painSystem.Hit(immune,other,false),"Active Enraged immunity was bypassed");

  }
  finally{minds.TransferTo(mind.Owner,null,createGhost:false);em.DeleteEntity(mind.Owner);em.DeleteEntity(map);}
    }
}
