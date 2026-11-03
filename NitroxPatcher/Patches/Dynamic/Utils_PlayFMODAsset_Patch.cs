using System.Reflection;
using NitroxClient.GameLogic.FMOD;
using Nitrox.Model.GameLogic.FMOD;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class Utils_PlayFMODAsset_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => Utils.PlayFMODAsset(default(FMODAsset)));

    public static bool Prefix()
    {
        return !FMODSoundSuppressor.SuppressFMODEvents;
    }

    public static void Postfix(FMODAsset asset)
    {
#if DEBUG
        // Dev bridge `sounds`: these 2D one-shots start with EventInstance.start() (e.g. the hatching music, COOP_FEATURES_SPEC G3b)
        if (!FMODSoundSuppressor.SuppressFMODEvents && asset && !string.IsNullOrEmpty(asset.path))
        {
            NitroxClient.Debuggers.Bridge.BridgeSounds.Record("Utils.PlayFMODAsset", asset.path, Player.main ? Player.main.transform.position : (UnityEngine.Vector3?)null);
        }
#endif
        if (Resolve<FMODWhitelist>().IsWhitelisted(asset.path))
        {
            Resolve<FMODSystem>().SendAssetPlay(asset.path, Player.main.transform.position.ToDto(), 1f);
        }
    }
}
