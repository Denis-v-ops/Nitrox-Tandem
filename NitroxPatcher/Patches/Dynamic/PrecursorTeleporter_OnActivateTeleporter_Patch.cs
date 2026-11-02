using System.Reflection;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class PrecursorTeleporter_OnActivateTeleporter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorTeleporter t) => t.OnActivateTeleporter(default(string)));

    public static void Prefix(PrecursorTeleporter __instance, out bool __state)
    {
        __state = __instance.isOpen;
    }

    public static void Postfix(PrecursorTeleporter __instance, bool __state)
    {
        // Every loaded teleporter gets this call for every activation, and only the activated pair opens: the others sent
        // their state too (isOpen false), which the partner applied as a closing portal (MULTINAUGHTICA_SPEC M2)
        if (__state || !__instance.isOpen)
        {
            return;
        }

        if (__instance.TryGetIdOrWarn(out NitroxId id))
        {
            PrecursorTeleporterMetadata precursorTeleporterMetadata = new(__instance.isOpen);
            Resolve<Entities>().BroadcastMetadataUpdate(id, precursorTeleporterMetadata);
        }
    }
}
