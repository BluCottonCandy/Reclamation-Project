namespace Content.IntegrationTests.Tests.Destructible
{
    public static class DestructibleTestPrototypes
    {
        public const string SpawnedEntityId = "DestructibleTestsSpawnedEntity";
        public const string SpawnedStackEntityId = "DestructibleTestsSpawnedStackEntity";
        public const string SpawnedStackId = "DestructibleTestsSpawnedStack";
        public const string DestructibleEntityId = "DestructibleTestsDestructibleEntity";
        public const string DestructibleDestructionEntityId = "DestructibleTestsDestructibleDestructionEntity";
        public const string DestructibleStackEntityId = "DestructibleTestsDestructibleStackEntity";
        public const string DestructibleStackId = "DestructibleTestsDestructibleStack";
        public const string DestructibleDamageTypeEntityId = "DestructibleTestsDestructibleDamageTypeEntity";
        public const string DestructibleDamageGroupEntityId = "DestructibleTestsDestructibleDamageGroupEntity";
        public const string RcdConstructedTag = "RCDConstructed";

        [TestPrototypes]
        public const string DamagePrototypes = $@"
- type: damageType
  id: TestBlunt
  name: damage-type-blunt

- type: damageType
  id: TestSlash
  name: damage-type-slash

- type: damageType
  id: TestPiercing
  name: damage-type-piercing

- type: damageType
  id: TestHeat
  name: damage-type-heat

- type: damageType
  id: TestShock
  name: damage-type-shock

- type: damageType
  id: TestCold
  name: damage-type-cold

- type: damageGroup
  id: TestBrute
  name: damage-group-brute
  damageTypes:
    - TestBlunt
    - TestSlash
    - TestPiercing

- type: damageGroup
  id: TestBurn
  name: damage-group-burn
  damageTypes:
    - TestHeat
    - TestShock
    - TestCold

- type: entity
  id: {SpawnedEntityId}
  name: {SpawnedEntityId}

- type: stack
  id: {SpawnedStackId}
  name: {SpawnedStackId}
  spawn: {SpawnedStackEntityId}
  maxCount: 3

- type: stack
  id: {DestructibleStackId}
  name: {DestructibleStackId}
  spawn: {DestructibleStackEntityId}
  maxCount: 10

- type: entity
  id: {SpawnedStackEntityId}
  name: {SpawnedStackEntityId}
  components:
  - type: Stack
    stackType: {SpawnedStackId}
    count: 1

- type: entity
  id: {DestructibleEntityId}
  name: {DestructibleEntityId}
  components:
  - type: Damageable
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 20
        triggersOnce: false
    - trigger:
        !type:DamageTrigger
        damage: 50
        triggersOnce: false
      behaviors:
      - !type:PlaySoundBehavior
        sound:
            collection: WoodDestroy
      - !type:SpawnEntitiesBehavior
        spawn:
          {SpawnedEntityId}:
            min: 1
            max: 1
      - !type:DoActsBehavior
        acts: [""Breakage""]

- type: entity
  id: {DestructibleDestructionEntityId}
  name: {DestructibleDestructionEntityId}
  components:
  - type: Damageable
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 50
      behaviors:
      - !type:PlaySoundBehavior
        sound:
            collection: WoodDestroyHeavy
      - !type:SpawnEntitiesBehavior
        spawn:
          {SpawnedEntityId}:
            min: 1
            max: 1
      - !type:DoActsBehavior # This must come last as it destroys the entity.
        acts: [""Destruction""]

- type: entity
  id: {DestructibleStackEntityId}
  name: {DestructibleStackEntityId}
  components:
  - type: Damageable
  - type: Stack
    stackType: {DestructibleStackId}
    count: 5
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 50
      behaviors:
      - !type:SpawnEntitiesBehavior
        spawn:
          {SpawnedStackEntityId}:
            min: 1
            max: 1
      - !type:DoActsBehavior
        acts: [""Destruction""]

- type: entity
  id: {DestructibleDamageTypeEntityId}
  name: {DestructibleDamageTypeEntityId}
  components:
  - type: Damageable
  - type: Destructible
    thresholds:
    - trigger:
        !type:AndTrigger
        triggers:
        - !type:DamageTypeTrigger
          damageType: TestBlunt
          damage: 10
        - !type:DamageTypeTrigger
          damageType: TestSlash
          damage: 10

- type: entity
  id: {DestructibleDamageGroupEntityId}
  name: {DestructibleDamageGroupEntityId}
  components:
  - type: Damageable
  - type: Destructible
    thresholds:
    - trigger:
        !type:AndTrigger
        triggers:
        - !type:DamageGroupTrigger
          damageGroup: TestBrute
          damage: 10
        - !type:DamageGroupTrigger
          damageGroup: TestBurn
          damage: 10";
    }
}
