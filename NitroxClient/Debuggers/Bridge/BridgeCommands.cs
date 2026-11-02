#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DevBridge;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.Helper;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.Spawning.Metadata;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Debuggers.Bridge;

/// <summary>
///     Commands of the game's dev bridge. Runs on the Unity main thread (see <see cref="DevBridge" />).
/// </summary>
internal static class BridgeCommands
{
    public const string HELP = """
                               Game dev bridge. <target> = player | <NitroxId> | find:<ComponentType> (nearest loaded).
                                 state                          player position, sub, underwater, oxygen, health, inventory
                                 teleport <x> <y> <z>           move the player (like the warp console command)
                                 look <yaw> <pitch>             set the camera direction in degrees
                                 console <command...>           run a Subnautica dev console command (item, spawn, goal, ...)
                                 chat <text...>                 send a chat message; "/cmd" runs a server command as this player
                                 entity <target> [all]          GameObject, components and their simple fields ("all": every field)
                                 find <ComponentType> [max]     loaded objects with that component, nearest first
                                 get <target> [Comp[.member...]] | get <Type.static[.member...]>
                                 set <target> <Comp.member...> <value> | set <Type.static...member> <value>
                                 call <target> <Comp.method> [args...] | call <Type.static...method> [args...]
                                 use <target> [ComponentType]   OnHandClick on the target's IHandTarget, as the local player
                                 screenshot <path.png>          written at the end of the frame (poll for the file)
                                                                windowless games: the main camera only, written at once
                                 entities                       dump of all loaded entities for "bridge.ps1 diff"
                                 record start <name> <target> [relativeTo] | record stop <name> | record list
                                                                per-frame pose samples (wall clock, position, velocity, ...)
                                 input move <x> <y> <z> | input off   hold the movement input (strafe/turn, up/down, forward)
                                 hold <target|none> <seconds> [button] [noaim] | hold status | hold off
                                                                hold a game button (default LeftHand = interact) through the game's
                                                                input, aiming at the target; status: targeted frames, reticle progress
                                 sounds [seconds] [filter] | sounds clear   FMOD events started lately (path, position, owner)
                                 pdaqueue                       PDA voice line playing now and the ones queued (PDASounds.queue)
                                 animator <target> [all]        Animator parameters and playing clips (e.g. the partner's body)
                               Values: numbers, true/false, null, enum names, x,y,z vectors, NitroxIds or targets for objects.
                               """;

    public static object? Execute(string line, MonoBehaviour host)
    {
        List<string> args = BridgeText.SplitArgs(line);
        if (args.Count == 0)
        {
            return HELP;
        }
        string command = args[0].ToLowerInvariant();
        args.RemoveAt(0);
        switch (command)
        {
            case "help":
                return HELP;
            case "state":
                return State();
            case "teleport":
                RequireArgs(args, 3, "teleport <x> <y> <z>");
                return Teleport(new Vector3(ParseFloat(args[0]), ParseFloat(args[1]), ParseFloat(args[2])));
            case "look":
                RequireArgs(args, 2, "look <yaw> <pitch>");
                return Look(ParseFloat(args[0]), ParseFloat(args[1]));
            case "console":
                return Console(BridgeText.Rest(line, 1));
            case "chat":
                return Chat(BridgeText.Rest(line, 1));
            case "entity":
                RequireArgs(args, 1, "entity <target> [all]");
                return Entity(BridgeReflection.ResolveTarget(args[0]), args.Count > 1 && args[1] == "all");
            case "find":
                RequireArgs(args, 1, "find <ComponentType> [max]");
                return Find(BridgeReflection.FindType(args[0]), args.Count > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 10);
            case "get":
                return Get(args);
            case "set":
                return Set(args);
            case "call":
                return Call(args, host);
            case "use":
                RequireArgs(args, 1, "use <target> [ComponentType]");
                return Use(BridgeReflection.ResolveTarget(args[0]), args.Count > 1 ? BridgeReflection.FindType(args[1]) : null);
            case "screenshot":
                RequireArgs(args, 1, "screenshot <path.png>");
                return Screenshot(args[0]);
            case "entities":
                return DumpEntities();
            case "record":
                return BridgeRecorder.Command(args, host);
            case "input":
                return BridgeInput.Command(args);
            case "hold":
                return BridgeHold.Command(args);
            case "sounds":
                return BridgeSounds.Command(args);
            case "pdaqueue":
                return BridgeSounds.PdaQueue();
            case "animator":
                return BridgeAnimator.Command(args);
            default:
                throw new ArgumentException($"Unknown command '{command}'. Try help.");
        }
    }

    private static Dictionary<string, object?> State()
    {
        IMultiplayerSession session = NitroxServiceLocator.LocateService<IMultiplayerSession>();
        Dictionary<string, object?> state = new()
        {
            ["name"] = session.AuthenticationContext?.Username,
            ["connection"] = session.CurrentState?.CurrentStage.ToString(),
            ["joined"] = Multiplayer.Joined,
            ["initialSyncCompleted"] = Multiplayer.Main && Multiplayer.Main.InitialSyncCompleted
        };
        Player player = Player.main;
        if (!player)
        {
            return state;
        }
        state["playerId"] = BridgeReflection.IdOf(player.gameObject);
        state["position"] = BridgeReflection.Round(player.transform.position);
        if (MainCameraControl.main)
        {
            state["look"] = new[] { Math.Round(MainCameraControl.main.rotationX, 1), Math.Round(MainCameraControl.main.rotationY, 1) };
        }
        SubRoot currentSub = player.currentSub;
        state["currentSub"] = currentSub ? BridgeReflection.Summary(currentSub.gameObject) : null;
        state["vehicle"] = player.GetVehicle() ? BridgeReflection.Summary(player.GetVehicle().gameObject) : null;
        state["isUnderwater"] = player.IsUnderwater();
        state["precursorOutOfWater"] = player.precursorOutOfWater;
        state["oxygen"] = Math.Round(player.GetOxygenAvailable(), 1);
        state["health"] = player.liveMixin ? Math.Round(player.liveMixin.health, 1) : (object?)null;
        if (player.TryGetComponent(out Survival survival))
        {
            state["food"] = Math.Round(survival.food, 1);
            state["water"] = Math.Round(survival.water, 1);
        }
        if (Inventory.main)
        {
            state["held"] = Inventory.main.GetHeld() ? BridgeReflection.IdOf(Inventory.main.GetHeld().gameObject) : null;
            state["inventory"] = Inventory.main.container.Select(DescribeItem).ToList();
            state["equipment"] = Inventory.main.equipment.equipment.Where(slot => slot.Value != null).ToDictionary(slot => slot.Key, slot => (object?)DescribeItem(slot.Value));
        }
        return state;
    }

    private static Dictionary<string, object?> DescribeItem(InventoryItem item)
    {
        Pickupable pickupable = item.item;
        Dictionary<string, object?> description = new()
        {
            ["techType"] = item.techType.ToString(),
            ["id"] = pickupable ? BridgeReflection.IdOf(pickupable.gameObject) : null
        };
        if (pickupable && pickupable.TryGetComponent(out EnergyMixin energyMixin))
        {
            InventoryItem battery = energyMixin.batterySlot?.storedItem;
            description["battery"] = battery != null && battery.item
                ? new Dictionary<string, object?> { ["id"] = BridgeReflection.IdOf(battery.item.gameObject), ["techType"] = battery.techType.ToString(), ["charge"] = Math.Round(energyMixin.charge, 1) }
                : null;
        }
        return description;
    }

    private static object Teleport(Vector3 position)
    {
        // Same as the game's "warp x y z" console command, without the fly-over.
        GotoConsoleCommand.main.GotoPosition(position, true);
        return new Dictionary<string, object?> { ["position"] = BridgeReflection.Round(Player.main.transform.position) };
    }

    private static object Look(float yaw, float pitch)
    {
        MainCameraControl camera = MainCameraControl.main;
        if (!camera)
        {
            throw new InvalidOperationException("No camera (not in game yet)");
        }
        camera.rotationX = yaw;
        camera.rotationY = Mathf.Clamp(pitch, camera.minimumY, camera.maximumY);
        return new[] { camera.rotationX, camera.rotationY };
    }

    private static object Console(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("console <command...>");
        }
        return new Dictionary<string, object?> { ["command"] = command, ["handled"] = DevConsole.SendConsoleCommand(command) };
    }

    private static object Chat(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("chat <text...>");
        }
        IMultiplayerSession session = NitroxServiceLocator.LocateService<IMultiplayerSession>();
        if (text.StartsWith("/"))
        {
            session.Send(new ServerCommand(text.Substring(1)));
            return "sent server command (its output goes to this player's chat)";
        }
        session.Send(new ChatMessage(session.Reservation.SessionId, text));
        return "sent";
    }

    private static object Entity(GameObject gameObject, bool allFields)
    {
        Dictionary<string, object?> description = BridgeReflection.Summary(gameObject);
        description["active"] = gameObject.activeInHierarchy;
        description["rotation"] = BridgeReflection.Round(gameObject.transform.rotation.eulerAngles);
        description["parentId"] = BridgeReflection.AncestorIdOf(gameObject.transform);
        description["metadata"] = ExtractMetadata(gameObject);
        description["childEntities"] = gameObject.GetComponentsInChildren<NitroxEntity>(true)
                                                 .Where(entity => entity.gameObject != gameObject)
                                                 .Select(entity => BridgeReflection.Summary(entity.gameObject))
                                                 .ToList();
        description["components"] = gameObject.GetComponents<Component>()
                                              .Where(component => component)
                                              .Select(component => IsGameLogic(component) ? BridgeReflection.DescribeComponent(component, !allFields) : (object)component.GetType().Name)
                                              .ToList();
        return BridgeReflection.ToJson(description, allFields ? 6 : 5);
    }

    private static object Find(Type type, int max)
    {
        Vector3 origin = Player.main ? Player.main.transform.position : Vector3.zero;
        return BridgeReflection.FindLoaded(type)
                               .Take(max)
                               .Select(component =>
                               {
                                   Dictionary<string, object?> summary = BridgeReflection.Summary(component.gameObject);
                                   summary["distance"] = Math.Round((component.transform.position - origin).magnitude, 1);
                                   return summary;
                               })
                               .ToList();
    }

    private static object Get(List<string> args)
    {
        object? value = BridgeReflection.ResolvePath(args, false, out _, out _);
        return value switch
        {
            Component component when component => BridgeReflection.ToJson(BridgeReflection.DescribeComponent(component, false)),
            GameObject gameObject when gameObject => Entity(gameObject, false),
            _ => BridgeReflection.ToJson(value)
        };
    }

    private static object Set(List<string> args)
    {
        object? instance = BridgeReflection.ResolvePath(args, true, out Type type, out string? member);
        if (args.Count != 1)
        {
            throw new ArgumentException("set <path> <value>: expected exactly one value");
        }
        BridgeReflection.SetMemberValue(instance, type, member!, args[0]);
        return BridgeReflection.ToJson(BridgeReflection.GetMemberValue(instance, type, member!, out _));
    }

    private static object Call(List<string> args, MonoBehaviour host)
    {
        object? instance = BridgeReflection.ResolvePath(args, true, out Type type, out string? method);
        object? result = BridgeReflection.Call(instance, type, method!, args, out MethodInfo called);
        if (result is IEnumerator coroutine && typeof(IEnumerator).IsAssignableFrom(called.ReturnType))
        {
            host.StartCoroutine(coroutine);
            return $"started coroutine {type.Name}.{called.Name}";
        }
        return called.ReturnType == typeof(void) ? $"called {type.Name}.{called.Name}" : BridgeReflection.ToJson(result);
    }

    private static object Use(GameObject gameObject, Type? componentType)
    {
        if (!Player.main || !Player.main.guiHand)
        {
            throw new InvalidOperationException("No local player (not in game yet)");
        }
        Component target = componentType != null
            ? BridgeReflection.GetComponentOn(gameObject, componentType)
            : BridgeReflection.GetComponentOn(gameObject, typeof(IHandTarget));
        if (target is not IHandTarget handTarget)
        {
            throw new InvalidOperationException($"{target.GetType().Name} isn't an IHandTarget");
        }
        handTarget.OnHandClick(Player.main.guiHand);
        return $"clicked {target.GetType().Name} on {target.gameObject.name}";
    }

    private static object Screenshot(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        if (Application.isBatchMode)
        {
            // Windowless games (dual-client.ps1 -Windowless) present no frame, so ScreenCapture writes nothing.
            File.WriteAllBytes(path, RenderMainCamera(1280, 720));
            return new Dictionary<string, object?> { ["path"] = path, ["note"] = "batch mode: the main camera only, without the HUD" };
        }
        ScreenCapture.CaptureScreenshot(path);
        return new Dictionary<string, object?> { ["path"] = path, ["note"] = "written at the end of this frame" };
    }

    private static byte[] RenderMainCamera(int width, int height)
    {
        Camera camera = MainCamera.camera;
        RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        Texture2D image = new(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            return image.EncodeToPNG();
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.Destroy(image);
        }
    }

    /// <summary>
    ///     One line per loaded entity, in <see cref="BridgeText.ENTITY_DUMP_HEADER" /> format. Items in a container get
    ///     the container owner's id as parent (what the server stores); other objects get the nearest ancestor entity.
    /// </summary>
    private static string DumpEntities()
    {
        StringBuilder builder = new();
        builder.AppendLine(BridgeText.ENTITY_DUMP_HEADER);
        builder.AppendLine($"#player\t{(Player.main ? BridgeReflection.IdOf(Player.main.gameObject) : "-")}");
        foreach (KeyValuePair<NitroxId, GameObject> pair in NitroxEntity.GetGameObjects().ToList())
        {
            GameObject gameObject = pair.Value;
            if (!gameObject)
            {
                continue;
            }
            string? parentId = GetContainerOwnerId(gameObject);
            string parentKind = "container";
            if (parentId == null)
            {
                parentId = BridgeReflection.AncestorIdOf(gameObject.transform);
                parentKind = parentId == null ? "-" : "ancestor";
            }
            Vector3 position = gameObject.transform.position;
            string kind = gameObject.activeInHierarchy ? "active" : "inactive";
            if (gameObject.GetComponent<Creature>())
            {
                kind += ",creature";
            }
            else if (IsMover(gameObject))
            {
                kind += ",mover";
            }
            builder.AppendLine(BridgeText.EntityDumpLine(pair.Key.ToString(), CraftData.GetTechType(gameObject).ToString(), kind, parentId, parentKind,
                                                         [position.x, position.y, position.z], ExtractMetadata(gameObject)));
        }
        return builder.ToString();
    }

    /// <summary>
    ///     Things besides creatures whose position legitimately changes all the time (the server only hears it now and
    ///     then): vehicles, subs, players and loose physics objects.
    /// </summary>
    private static bool IsMover(GameObject gameObject)
    {
        if (gameObject.GetComponent<Vehicle>() || gameObject.GetComponent<SubRoot>() || gameObject.GetComponent<Player>() ||
            gameObject.GetComponent<RemotePlayerIdentifier>())
        {
            return true;
        }
        return gameObject.TryGetComponent(out Rigidbody rigidbody) && !rigidbody.isKinematic;
    }

    private static string? GetContainerOwnerId(GameObject gameObject)
    {
        if (!gameObject.TryGetComponent(out Pickupable pickupable) || pickupable.inventoryItem == null)
        {
            return null;
        }
        IItemsContainer container = pickupable.inventoryItem.container;
        if (container == null)
        {
            return null;
        }
        if (Inventory.main && (ReferenceEquals(container, Inventory.main.container) || ReferenceEquals(container, Inventory.main.equipment)))
        {
            return BridgeReflection.IdOf(Player.mainObject);
        }
        if (container is Equipment equipment)
        {
            return BridgeReflection.IdOf(equipment.owner);
        }
        if (container is ItemsContainer itemsContainer && itemsContainer.tr && InventoryContainerHelper.TryGetOwnerId(itemsContainer.tr, out NitroxId ownerId))
        {
            return ownerId.ToString();
        }
        // Battery and other single-item slots: the slot's owner is the nearest ancestor entity.
        return null;
    }

    private static string ExtractMetadata(GameObject gameObject)
    {
        try
        {
            Optional<EntityMetadata> metadata = NitroxServiceLocator.LocateService<EntityMetadataManager>().Extract(gameObject);
            return metadata.HasValue ? BridgeText.DescribeMetadata(metadata.Value) : "-";
        }
        catch (Exception ex)
        {
            return $"<{ex.GetType().Name}: {ex.Message}>";
        }
    }

    private static bool IsGameLogic(Component component)
    {
        string assembly = component.GetType().Assembly.GetName().Name;
        return assembly is "Assembly-CSharp" or "Assembly-CSharp-firstpass" or "NitroxClient";
    }

    private static void RequireArgs(List<string> args, int count, string usage)
    {
        if (args.Count < count)
        {
            throw new ArgumentException($"Usage: {usage}");
        }
    }

    // NumberStyles.Float has no thousands separator: "-53,74" from a German-formatted script must fail, not teleport to -5374
    private static float ParseFloat(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
#endif
