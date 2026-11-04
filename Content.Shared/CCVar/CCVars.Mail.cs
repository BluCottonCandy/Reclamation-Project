using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>Enable periodic random deliveries from all mail teleporters, including vault mail.</summary>
    public static readonly CVarDef<bool> MailAutomaticDeliveryEnabled =
        CVarDef.Create("mail.automatic_delivery_enabled", false, CVar.ARCHIVE | CVar.SERVERONLY);
}
