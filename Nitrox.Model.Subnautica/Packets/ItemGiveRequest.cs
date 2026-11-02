using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Handing an item over (COOP_FEATURES_SPEC T3). The giver's game asks the server to move an item from the giver's
///     inventory into the target's; nothing moves on any game until the server agrees. If it does, it moves the item and
///     sends <see cref="EntityReparented" /> to everyone. Sent to the server only.
/// </summary>
[Serializable]
public sealed class ItemGiveRequest : Packet
{
    public NitroxId ItemId { get; }

    public SessionId TargetId { get; }

    public ItemGiveRequest(NitroxId itemId, SessionId targetId)
    {
        ItemId = itemId;
        TargetId = targetId;
    }
}
