using System;
using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
///     The game side of the story gates (COOP_FEATURES_SPEC G0). A patch that gates a moment calls
///     <see cref="Hold" /> and returns false from its Prefix; when the server sends <see cref="StoryGateStart" />, the held
///     moment runs again with <see cref="IsPassing" /> true, so the Prefix lets it through:
///     <code>
///     public static bool Prefix(Foo __instance)
///     {
///         if (Resolve&lt;StoryGates&gt;().IsPassing(StoryGateKind.X)) { return true; }
///         Resolve&lt;StoryGates&gt;().Hold(StoryGateKind.X, () =&gt; __instance.TheMethod());
///         return false;
///     }
///     </code>
///     A player who leaves the place of the moment (e.g. a terminal's area) calls <see cref="Cancel" />.
/// </summary>
public class StoryGates(IPacketSender packetSender, LocalPlayer localPlayer)
{
    /// <summary>While a held attempt goes on (e.g. each weld), the ready is sent again at most this often.</summary>
    private const float READY_RENEW_SECONDS = 1f;

    /// <summary>A held attempt shows the last status again when it's older than this, so a player who keeps trying sees why.</summary>
    private const float STATUS_REPEAT_SECONDS = 4f;

    private readonly IPacketSender packetSender = packetSender;
    private readonly LocalPlayer localPlayer = localPlayer;
    private readonly Dictionary<StoryGateKind, Action> held = [];
    private readonly Dictionary<StoryGateKind, float> readySentAt = [];
    private readonly Dictionary<StoryGateKind, (string Text, float ShownAt)> lastMessage = [];
    private StoryGateKind? passing;

    /// <summary>True while a started moment runs: the gate's Prefix must let it through.</summary>
    public bool IsPassing(StoryGateKind kind) => passing == kind;

    public bool IsHeld(StoryGateKind kind) => held.ContainsKey(kind);

    /// <summary>
    ///     Holds the moment back and asks the server for it. <paramref name="resume" /> runs it when the server says it
    ///     may start; a later call replaces it.
    /// </summary>
    /// <param name="repeatStatus">
    ///     False for a hold that is renewed without the player doing anything (standing on a platform), so the last status
    ///     isn't shown again every few seconds.
    /// </param>
    public void Hold(StoryGateKind kind, Action resume, bool repeatStatus = true)
    {
        held[kind] = resume;
        float now = Time.realtimeSinceStartup;
        if (!readySentAt.TryGetValue(kind, out float sentAt) || now - sentAt >= READY_RENEW_SECONDS)
        {
            readySentAt[kind] = now;
            packetSender.Send(new StoryGateReady(kind, true));
        }
        if (repeatStatus && lastMessage.TryGetValue(kind, out (string Text, float ShownAt) message) && now - message.ShownAt >= STATUS_REPEAT_SECONDS)
        {
            Show(kind, message.Text);
        }
    }

    /// <summary>Drops a held moment (the player walked away from it) and withdraws the ready.</summary>
    public void Cancel(StoryGateKind kind)
    {
        readySentAt.Remove(kind);
        lastMessage.Remove(kind);
        if (held.Remove(kind))
        {
            packetSender.Send(new StoryGateReady(kind, false));
        }
    }

    public void OnStart(StoryGateKind kind)
    {
        readySentAt.Remove(kind);
        lastMessage.Remove(kind);
        if (!held.TryGetValue(kind, out Action resume))
        {
            return;
        }
        held.Remove(kind);
        Log.Info($"[{nameof(StoryGates)}] {kind} starts");
        passing = kind;
        try
        {
            resume();
        }
        catch (Exception e)
        {
            Log.Error(e, $"[{nameof(StoryGates)}] Couldn't start {kind}");
        }
        finally
        {
            passing = null;
        }
    }

    public void OnStatus(StoryGateStatus status)
    {
        string text = FormatStatus(status, localPlayer.PlayerName, Language.main.Get);
        if (!string.IsNullOrEmpty(text))
        {
            Show(status.Kind, text);
        }
    }

    private void Show(StoryGateKind kind, string text)
    {
        lastMessage[kind] = (text, Time.realtimeSinceStartup);
        Log.InGame(text);
    }

    /// <summary>
    ///     The message for this player: who is offline (locked), or for a player who asked who they wait for, or for a
    ///     player who is missing who waits for them. Empty for anyone else.
    /// </summary>
    internal static string FormatStatus(StoryGateStatus status, string localName, Func<string, string> translate)
    {
        StringComparer comparer = StringComparer.OrdinalIgnoreCase;
        string missing = string.Join(", ", status.MissingNames ?? []);
        if (status.Locked)
        {
            return translate("Nitrox_StoryGateOffline").Replace("{PLAYERS}", missing);
        }
        if (status.ReadyNames?.Contains(localName, comparer) == true)
        {
            string template = translate(status.Kind switch
            {
                StoryGateKind.DRIVE_CORE => "Nitrox_StoryGateDriveCoreWait",
                StoryGateKind.DISABLE_GUN => "Nitrox_StoryGateGunWait",
                StoryGateKind.ROCKET_LAUNCH => "Nitrox_StoryGateRocketWait",
                StoryGateKind.SEA_EMPEROR => "Nitrox_StoryGateSeaEmperorWait",
                StoryGateKind.HATCHING => "Nitrox_StoryGateHatchingWait",
                _ => "Nitrox_StoryGateWaitingFor"
            });
            return template.Replace("{PLAYERS}", missing);
        }
        if (status.MissingNames?.Contains(localName, comparer) == true)
        {
            string ready = string.Join(", ", status.ReadyNames ?? []);
            string template = translate(status.Kind switch
            {
                StoryGateKind.DRIVE_CORE => "Nitrox_StoryGateDriveCoreJoin",
                StoryGateKind.DISABLE_GUN => "Nitrox_StoryGateGunJoin",
                StoryGateKind.ROCKET_LAUNCH => "Nitrox_StoryGateRocketJoin",
                StoryGateKind.SEA_EMPEROR => "Nitrox_StoryGateSeaEmperorJoin",
                StoryGateKind.HATCHING => "Nitrox_StoryGateHatchingJoin",
                _ => "Nitrox_StoryGatePartnerReady"
            });
            return template.Replace("{PLAYERS}", ready);
        }
        return "";
    }
}
