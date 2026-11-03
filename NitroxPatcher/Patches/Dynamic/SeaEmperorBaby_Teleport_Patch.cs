using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     A Sea Emperor baby that reaches the teleporter destroys itself (<c>SeaEmperorBaby.Teleport</c>, called by
///     <c>SeaEmperorTeleporterTrigger</c>). The real babies are server entities (<see cref="IncubatorEgg_HatchNow_Patch" />)
///     and the server was never told, so they came back at every load and swam to the mother again (MULTINAUGHTICA_SPEC M2).
///     Every game that has the baby reports it; the server relays only the first report, for an entity it still has.
/// </summary>
public sealed partial class SeaEmperorBaby_Teleport_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SeaEmperorBaby t) => t.Teleport());

    public static void Prefix(SeaEmperorBaby __instance)
    {
        // The temporary babies of the hatch animation have no id
        if (__instance.TryGetNitroxId(out NitroxId id))
        {
            Resolve<IPacketSender>().Send(new EntityDestroyed(id));
        }
    }
}
