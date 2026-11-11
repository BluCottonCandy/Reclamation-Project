using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Body.Systems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
namespace Content.IntegrationTests.Tests._ReclamationProject;
[TestFixture]
public sealed class SmokeMaskTests
{
 [Test]
 public async Task WornMasksBlockSmokeChemicalsWithoutAnAirTank()
 {
  await using var pair=await PoolManager.GetServerClient();
  await pair.Server.WaitAssertion(()=>Verify(pair.Server.ResolveDependency<IEntityManager>()));
  await pair.CleanReturnAsync();
 }
 public static void Verify(IEntityManager em)
 {
  var map=em.System<SharedMapSystem>().CreateMap(out _,runMapInit:true);
  void Check(bool value,string why){if(!value)throw new Exception(why);}
  try
  {
   var user=em.SpawnEntity("MobHuman",new EntityCoordinates(map,Vector2.Zero));
   var cloud=em.SpawnEntity("Smoke",new EntityCoordinates(map,new Vector2(3,0)));
   var smoke=em.System<SmokeSystem>();var inventory=em.System<InventorySystem>();var solutions=em.System<SharedSolutionContainerSystem>();
   var mixture=new Solution();mixture.AddReagent("Water",20);smoke.StartSmoke(cloud,mixture,10,0);
   FixedPoint2 Volume(){Check(solutions.TryGetSolution(user,"chemicals",out _,out var solution),"Body chemical solution missing");return solution!.Volume;}
   void Expose(bool blocked,string scenario)
   {
    Check(smoke.IsInhalationBlocked(user)==blocked,"Wrong inhalation protection: "+scenario);
    var before=Volume();smoke.SmokeReact(user,cloud);var after=Volume();
    Check(blocked?after==before:after>before,"Wrong chemical smoke transfer: "+scenario);
   }
   Expose(false,"no mask");
   var gas=em.SpawnEntity("ClothingMaskGas",new EntityCoordinates(map,Vector2.Zero));
   Expose(false,"gas mask carried but not worn");
   Check(inventory.TryEquip(user,gas,"mask",silent:true,force:true),"Could not equip gas mask");
   Check(!em.System<InternalsSystem>().AreInternalsWorking(user),"Test unexpectedly has active internals");
   Expose(true,"gas mask without air tank");
   Check(inventory.TryUnequip(user,"mask",silent:true,force:true),"Could not remove gas mask");
   Expose(false,"gas mask removed");
   var sterile=em.SpawnEntity("ClothingMaskSterile",new EntityCoordinates(map,Vector2.Zero));
   Check(inventory.TryEquip(user,sterile,"mask",silent:true,force:true),"Could not equip surgical mask");
   Expose(true,"surgical mask");
   var down=new ToggleMaskEvent {Performer=user};em.EventBus.RaiseLocalEvent(sterile,down);
   Check(em.GetComponent<MaskComponent>(sterile).IsToggled,"Mask toggle did not lower it");
   Expose(false,"surgical mask pulled down");
   var up=new ToggleMaskEvent {Performer=user};em.EventBus.RaiseLocalEvent(sterile,up);
   Expose(true,"surgical mask pulled up again");
   Check(inventory.TryUnequip(user,"mask",silent:true,force:true),"Could not remove surgical mask");
   var plain=em.SpawnEntity("ClothingMaskMuzzle",new EntityCoordinates(map,Vector2.Zero));
   Check(inventory.TryEquip(user,plain,"mask",silent:true,force:true),"Could not equip ordinary mask");
   Expose(true,"ordinary mask");
   Check(inventory.TryUnequip(user,"mask",silent:true,force:true),"Could not remove ordinary mask");
   var cigarette=em.SpawnEntity("Cigarette",new EntityCoordinates(map,Vector2.Zero));
   Check(inventory.TryEquip(user,cigarette,"mask",silent:true,force:true),"Could not equip cigarette");
   Expose(false,"cigarette in mouth is not a mask");
   Check(inventory.TryUnequip(user,"mask",silent:true,force:true),"Could not remove cigarette");
   em.EnsureComponent<Content.Server.Body.Components.RespiratorImmuneComponent>(user);
   Expose(true,"non-breathing character without mask");
  }
  finally{em.DeleteEntity(map);}
 }
}
