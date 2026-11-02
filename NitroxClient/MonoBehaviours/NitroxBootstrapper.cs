using NitroxClient.MonoBehaviours.Discord;
using NitroxClient.MonoBehaviours.Gui.MainMenu;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

public class NitroxBootstrapper : MonoBehaviour
{
    internal static NitroxBootstrapper Instance;

    // Awake is too early in Subnautica's lifecycle to access PlatformUtils
    // so we pick Start which will always happen after it's initialized
    private void Start()
    {
        DontDestroyOnLoad(gameObject);
        Instance = this;
        gameObject.AddComponent<SceneCleanerPreserve>();
        gameObject.AddComponent<NitroxMainMenuModifications>();
        gameObject.AddComponent<DiscordClient>();

#if DEBUG
        EnableDeveloperFeatures();
        CreateDebugger();
        MuteIfWindowless();
#endif

        // This is very important, see Application_runInBackground_Patch.cs
        Application.runInBackground = true;
        Log.Info($"Unity run in background set to \"{Application.runInBackground}\"");
        // Also very important for similar reasons
        MiscSettings.pdaPause = false;
    }

#if DEBUG
    private static void EnableDeveloperFeatures()
    {
        Log.Info("Enabling Subnautica developer console");
        PlatformUtils.SetDevToolsEnabled(true);
    }

    private void CreateDebugger()
    {
        Log.Info("Enabling Nitrox debugger");
        GameObject debugger = new();
        debugger.name = "Debug manager";
        debugger.AddComponent<NitroxDebugManager>();
        debugger.transform.SetParent(transform);
        // Lets agents drive this game from the shell (tools/bridge.ps1). Never in Release builds.
        debugger.AddComponent<Debuggers.Bridge.DevBridge>();
    }

    /// <summary>
    ///     Windowless test games (tools/dual-client.ps1 -Windowless) run while someone uses the PC, and FMOD plays sound
    ///     without a window too. Muting its master channel group, unlike the volume options, isn't saved in the game's
    ///     settings.
    /// </summary>
    private static void MuteIfWindowless()
    {
        if (!Application.isBatchMode)
        {
            return;
        }
        FMOD.RESULT result = FMODUnity.RuntimeManager.CoreSystem.getMasterChannelGroup(out FMOD.ChannelGroup master);
        if (result == FMOD.RESULT.OK)
        {
            result = master.setMute(true);
        }
        if (result != FMOD.RESULT.OK)
        {
            Log.Warn($"Batch mode: couldn't mute the game's sound ({result})");
            return;
        }
        Log.Info("Batch mode: game sound muted");
    }
#endif
}
