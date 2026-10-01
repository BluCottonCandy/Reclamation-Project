using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.Animations;
using Robust.Shared.Maths; // #Misfits Change/Add: required for Color in deny flash animation
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Client.Doors;

public sealed class DoorSystem : SharedDoorSystem
{
    [Dependency] private readonly AnimationPlayerSystem _animationSystem = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DoorComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    protected override void OnComponentInit(Entity<DoorComponent> ent, ref ComponentInit args)
    {
        var comp = ent.Comp;
        comp.OpenSpriteStates = new(2);
        comp.ClosedSpriteStates = new(2);

        comp.OpenSpriteStates.Add((DoorVisualLayers.Base, comp.OpenSpriteState));
        comp.ClosedSpriteStates.Add((DoorVisualLayers.Base, comp.ClosedSpriteState));

        comp.OpeningAnimation = new Animation()
        {
            Length = TimeSpan.FromSeconds(comp.OpeningAnimationTime),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick()
                {
                    LayerKey = DoorVisualLayers.Base,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(comp.OpeningSpriteState, 0f) }
                }
            },
        };

        comp.ClosingAnimation = new Animation()
        {
            Length = TimeSpan.FromSeconds(comp.ClosingAnimationTime),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick()
                {
                    LayerKey = DoorVisualLayers.Base,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(comp.ClosingSpriteState, 0f) }
                }
            },
        };

        comp.EmaggingAnimation = new Animation ()
        {
            Length = TimeSpan.FromSeconds(comp.EmaggingAnimationTime),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick()
                {
                    LayerKey = DoorVisualLayers.BaseUnlit,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(comp.EmaggingSpriteState, 0f) }
                }
            },
        };

        // #Misfits Change/Add: Initialize a fallback deny animation for basic (non-airlock) doors.
        // These doors have no dedicated deny sprite states, so we flash the sprite red twice over 0.4s.
        // Airlock doors will overwrite this in AirlockSystem.OnComponentInit with their own unlit-layer flick.
        comp.DenyingAnimation = new Animation()
        {
            Length = TimeSpan.FromSeconds(0.4f),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Nearest,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Color.Red, 0f),
                        new AnimationTrackProperty.KeyFrame(Color.White, 0.1f),
                        new AnimationTrackProperty.KeyFrame(Color.Red, 0.2f),
                        new AnimationTrackProperty.KeyFrame(Color.White, 0.4f),
                    }
                }
            },
        };
    }

    private void OnAppearanceChange(EntityUid uid, DoorComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if(!AppearanceSystem.TryGetData<DoorState>(uid, DoorVisuals.State, out var state, args.Component))
            state = DoorState.Closed;

        if (AppearanceSystem.TryGetData<string>(uid, DoorVisuals.BaseRSI, out var baseRsi, args.Component))
        {
            if (!_resourceCache.TryGetResource<RSIResource>(SpriteSpecifierSerializer.TextureRoot / baseRsi, out var res))
            {
                Log.Error("Unable to load RSI '{0}'. Trace:\n{1}", baseRsi, Environment.StackTrace);
            }
            foreach (var layer in args.Sprite.AllLayers)
            {
                layer.Rsi = res?.RSI;
            }
        }

        TryComp<AnimationPlayerComponent>(uid, out var animPlayer);
        if (_animationSystem.HasRunningAnimation(uid, animPlayer, DoorComponent.AnimationKey))
            _animationSystem.Stop(uid, animPlayer, DoorComponent.AnimationKey); // Halt all running anomations.

        args.Sprite.DrawDepth = comp.ClosedDrawDepth;
        switch(state)
        {
            case DoorState.Open:
                args.Sprite.DrawDepth = comp.OpenDrawDepth;
                foreach(var (layer, layerState) in comp.OpenSpriteStates)
                {
                    args.Sprite.LayerSetState(layer, layerState);
                }
                break;
            case DoorState.Closed:
                foreach(var (layer, layerState) in comp.ClosedSpriteStates)
                {
                    args.Sprite.LayerSetState(layer, layerState);
                }
                break;
            case DoorState.Opening:
                if (animPlayer != null && comp.OpeningAnimationTime != 0.0)
                    _animationSystem.Play((uid, animPlayer), (Animation)comp.OpeningAnimation, DoorComponent.AnimationKey);
                break;
            case DoorState.Closing:
                if (animPlayer != null && comp.ClosingAnimationTime != 0.0 && comp.CurrentlyCrushing.Count == 0)
                    _animationSystem.Play((uid, animPlayer), (Animation)comp.ClosingAnimation, DoorComponent.AnimationKey);
                break;
            case DoorState.Denying:
                // #Misfits Change/Fix: Guard against null DenyingAnimation to prevent NullReferenceException.
                if (animPlayer != null && comp.DenyingAnimation != null)
                    _animationSystem.Play((uid, animPlayer), (Animation)comp.DenyingAnimation, DoorComponent.AnimationKey);
                break;
            case DoorState.Welded:
                break;
            case DoorState.Emagging:
                if (animPlayer != null)
                    _animationSystem.Play((uid, animPlayer), (Animation)comp.EmaggingAnimation, DoorComponent.AnimationKey);
                break;
            default:
                throw new ArgumentOutOfRangeException($"Invalid door visual state {state}");
        }
    }
}
