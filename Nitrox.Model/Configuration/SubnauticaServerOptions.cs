using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Nitrox.Model.Constants;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Serialization;

namespace Nitrox.Model.Configuration;

/// <summary>
///     Options that are tied to a hosted Subnautica world.
/// </summary>
[SerializableFileName("server.cfg")]
[PropertyDescription("Server settings can be changed here")]
public sealed partial class SubnauticaServerOptions
{
    public const string CONFIG_SECTION_PATH = "GameServer";

    [Range(1, byte.MaxValue)]
    public byte MaxConnections { get; set; } = 100;

    [Range(1, ushort.MaxValue)]
    public ushort ServerPort { get; set; } = SubnauticaServerConstants.DEFAULT_PORT;

    [PropertyDescription("If not empty, users will be asked to enter a password to join the server")]
    [RegularExpression(@"\w+")]
    public string ServerPassword { get; set; } = "";

    public SubnauticaGameMode GameMode { get; set; }

    [PropertyDescription("Measured in milliseconds. Values less than 1 second will disable auto saving.")]
    public int SaveInterval { get; set; } = (int)TimeSpan.FromMinutes(2).TotalMilliseconds;

    [Range(0, int.MaxValue)]
    public int MaxBackups { get; set; } = 10;

    [Range(30001, int.MaxValue)]
    public int InitialSyncTimeout { get; set; } = 300000;

    [PropertyDescription("Possible values:", typeof(Perms))]
    public Perms DefaultPlayerPerm { get; set; } = Perms.DEFAULT;

    [PropertyDescription("If true, players using localhost get admin by default - disable if you're using a proxy server")]
    public bool LocalhostIsAdmin { get; set; } = true;

    public float DefaultOxygenValue { get; set; } = 45;

    public float DefaultMaxOxygenValue { get; set; } = 45;
    public float DefaultHealthValue { get; set; } = 80;
    public float DefaultHungerValue { get; set; } = 50.5f;
    public float DefaultThirstValue { get; set; } = 90.5f;

    [PropertyDescription("Recommended to keep at 0.1 which is the default starting value. If set to 0, new players are cured by default.")]
    public float DefaultInfectionValue { get; set; } = 0.1f;

    [PropertyDescription("If set to true, the server will try to open ports on your router via UPnP")]
    public bool PortForward { get; set; } = true;

    [PropertyDescription("Determines whether the server will listen for and reply to LAN discovery requests.")]
    public bool LanDiscovery { get; set; } = true;

    [PropertyDescription("Set to true to Cache entities for the whole map on next run. \nWARNING! Will make server load take longer on the cache run but players will gain a performance boost when entering new areas.")]
    public bool CreateFullEntityCache { get; set; } = false;

    [Required]
    [RegularExpression(@"\w+")]
    public string Seed { get; set; } = "";

    public bool DisableConsole { get; set; }
    public string? AdminPassword { get; set; } = "";
    public bool KeepInventoryOnDeath { get; set; }

    [PropertyDescription("Places a beacon where players die")]
    public bool MarkDeathPointsWithBeacon { get; set; }
    public bool PvpEnabled { get; set; } = true;
    public bool AutoSave { get; set; } = true;

    [PropertyDescription("When true, will reject any build actions detected as desynced")]
    public bool SafeBuilding { get; set; } = true;

    [PropertyDescription("Command to run following a successful world save (e.g. .exe, .bat, or PowerShell script). ")]
    public string PostSaveCommandPath { get; set; } = "";

    [PropertyDescription("Major story beats wait until every player on the roster is there and has confirmed")]
    public bool StoryGatesEnabled { get; set; } = true;

    [PropertyDescription("Comma-separated player names that story gates wait for (e.g. \"Name1,Name2\"). If empty, all saved players count.")]
    public string StoryGatePlayers { get; set; } = "";

    [PropertyDescription("Seconds a player stays down at 0 health before dying, so another player can revive them. 0 disables it.")]
    [Range(0, int.MaxValue)]
    public int RescueWindowSeconds { get; set; } = 60;

    [PropertyDescription("If true, the rescue window halves on the 2nd down since the player last rested and is gone on the 3rd")]
    public bool RescueEscalation { get; set; } = true;

    [PropertyDescription("Share of the oxygen given by buddy breathing that the receiver gets (0.5 = half of what the donor pays)")]
    [Range(0.0, 1.0)]
    public float BuddyBreathingEfficiency { get; set; } = 0.5f;

    [PropertyDescription("If true, the partner's marker drops to a last contact in caves and the deep unless a beacon or scanner room relays it")]
    public bool PartnerSignalInterference { get; set; } = true;

    [PropertyDescription("Depth in meters below which the partner's signal fades (see PartnerSignalInterference)")]
    [Range(0, int.MaxValue)]
    public int SignalInterferenceDepth { get; set; } = 300;

    [PropertyDescription("If true, a player hears the PDA's biome and location lines only when they get there themselves")]
    public bool PartnerDiscoveriesSilent { get; set; } = true;

    [PropertyDescription("If true, a player can hold on to the outside of a Seamoth the other player pilots and ride along; their air keeps draining")]
    public bool SeamothHitch { get; set; } = true;
    [PropertyDescription("If true, the fabricators and modification stations of a base or Cyclops also use the items in its lockers, after the player's inventory")]
    public bool CraftFromLockers { get; set; } = true;

    [OptionsValidator]
    public partial class Validator : IValidateOptions<SubnauticaServerOptions>;
}
