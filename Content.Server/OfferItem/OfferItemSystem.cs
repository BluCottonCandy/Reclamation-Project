using Content.Server.Chat.Systems; // #Misfits Add: chat emote on item handoff
using Content.Server.Popups;
using Content.Shared.Chat; // #Misfits Add: InGameICChatType
using Content.Shared.Hands.Components;
using Content.Shared.Alert;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.OfferItem;
using Content.Shared.IdentityManagement;
using Robust.Shared.Player;

namespace Content.Server.OfferItem;

public sealed class OfferItemSystem : SharedOfferItemSystem
{
    [Dependency] private readonly AlertsSystem _alertsSystem = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly ChatSystem _chat = default!; // #Misfits Add: for emote chat on handoff

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<OfferItemComponent>();
        while (query.MoveNext(out var uid, out var offerItem))
        {
            if (!TryComp<HandsComponent>(uid, out var hands) || hands.ActiveHand == null)
                continue;

            if (offerItem.Hand != null &&
                hands.Hands[offerItem.Hand].HeldEntity == null)
            {
                if (offerItem.Target != null)
                {
                    UnReceive(offerItem.Target.Value, offerItem: offerItem);
                    offerItem.IsInOfferMode = false;
                    Dirty(uid, offerItem);
                }
                else
                    UnOffer(uid, offerItem);
            }

            if (!offerItem.IsInReceiveMode)
            {
                _alertsSystem.ClearAlert(uid, offerItem.OfferAlert);
                continue;
            }

            _alertsSystem.ShowAlert(uid, offerItem.OfferAlert);
        }
    }

    /// <summary>
    /// Accepting the offer and receive item
    /// </summary>
    public void Receive(EntityUid uid, OfferItemComponent? component = null)
    {
        if (!Resolve(uid, ref component) ||
            !TryComp<OfferItemComponent>(component.Target, out var offerItem) ||
            offerItem.Hand == null ||
            component.Target == null ||
            !TryComp<HandsComponent>(uid, out var hands))
            return;

        if (offerItem.Item != null)
        {
            if (!_hands.TryPickup(uid, offerItem.Item.Value, handsComp: hands))
            {
                _popup.PopupEntity(Loc.GetString("offer-item-full-hand"), uid, uid);
                return;
            }

            _popup.PopupEntity(Loc.GetString("offer-item-give",
                ("item", Identity.Entity(offerItem.Item.Value, EntityManager)),
                ("target", Identity.Entity(uid, EntityManager))), component.Target.Value, component.Target.Value);
            _popup.PopupEntity(Loc.GetString("offer-item-give-other",
                    ("user", Identity.Entity(component.Target.Value, EntityManager)),
                    ("item", Identity.Entity(offerItem.Item.Value, EntityManager)),
                    ("target", Identity.Entity(uid, EntityManager)))
                , component.Target.Value, Filter.PvsExcept(component.Target.Value, entityManager: EntityManager), true);

            // #Misfits Add: broadcast item handoff as a chat emote so nearby players see it in the chatbox
            var itemName = Identity.Entity(offerItem.Item.Value, EntityManager);
            var targetName = Identity.Entity(uid, EntityManager);
            var handoffMsg = Loc.GetString("misfits-chat-offer-handoff", ("item", itemName), ("target", targetName));
            _chat.TrySendInGameICMessage(component.Target.Value, handoffMsg, InGameICChatType.Emote,
                ChatTransmitRange.Normal, ignoreActionBlocker: true);
        }

        offerItem.Item = null;
        UnReceive(uid, component, offerItem);
    }
}
