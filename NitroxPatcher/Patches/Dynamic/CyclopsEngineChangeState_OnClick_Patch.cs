using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class CyclopsEngineChangeState_OnClick_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((CyclopsEngineChangeState t) => t.OnClick());

    /// <summary>
    ///     Everyone in the sub can use the helm screen (Cyclops crew, COOP_FEATURES_SPEC T4), but only the pilot starts or
    ///     stops the engine. <see cref="NitroxClient.GameLogic.Spawning.Metadata.Processor.CyclopsMetadataProcessor" />
    ///     applies the partner's engine change through this method too, under the metadata suppressor.
    /// </summary>
    public static bool Prefix(out bool __state)
    {
        __state = Player.main.isPiloting || PacketSuppressor<EntityMetadataUpdate>.IsSuppressed;
        return __state;
    }

    public static void Postfix(CyclopsEngineChangeState __instance, bool __state)
    {
        if (__state && __instance.subRoot.TryGetIdOrWarn(out NitroxId id))
        {
            Resolve<Cyclops>().BroadcastMetadataChange(id);
        }
    }
}
