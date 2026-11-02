using System;
using System.Collections.Generic;
using System.Linq;
using NitroxClient.GameLogic.InitialSync.Abstract;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Story;

namespace NitroxClient.GameLogic.InitialSync;

public sealed class StoryGoalInitialSyncProcessor : InitialSyncProcessor
{
    private readonly TimeManager timeManager;

    public StoryGoalInitialSyncProcessor(TimeManager timeManager)
    {
        this.timeManager = timeManager;

        AddStep(SetTimeData);
        AddStep(SetupStoryGoalManager);
        AddStep(SetupTrackers);
        AddStep(SetupAuroraAndSunbeam);
        AddStep(SetScheduledGoals);
    }

    private static void SetupStoryGoalManager(InitialPlayerSync packet)
    {
        List<string> completedGoals = packet.StoryGoalData.CompletedGoals;
        List<string> radioQueue = packet.StoryGoalData.RadioQueue;
        Dictionary<string, float> personalGoals = packet.StoryGoalData.PersonalCompletedGoalsWithTimestamp;
        StoryGoalManager storyGoalManager = StoryGoalManager.main;

        storyGoalManager.completedGoals.AddRange(completedGoals);

        storyGoalManager.pendingRadioMessages.AddRange(radioQueue);
        storyGoalManager.PulsePendingMessages();

        // Restore states of GoalManager and the (tutorial) arrow system
        foreach (KeyValuePair<string, float> entry in personalGoals)
        {
            Goal entryGoal = GoalManager.main.goals.Find(goal => goal.customGoalName.Equals(entry.Key));
            if (entryGoal != null)
            {
                entryGoal.SetTimeCompleted(entry.Value);
            }
        }
        GoalManager.main.completedGoalNames.AddRange(personalGoals.Keys);
        PlayerWorldArrows.main.completedCustomGoals.AddRange(personalGoals.Keys);

        // Deactivate the current arrow if it was completed
        if (personalGoals.Any(goal => goal.Key.Equals(WorldArrowManager.main.currentGoalText)))
        {
            WorldArrowManager.main.DeactivateArrow();
        }

        Log.Info($"""
        Received initial sync packet with:
        - Completed story goals : {completedGoals.Count}
        - Personal goals        : {personalGoals.Count}
        - Radio queue           : {radioQueue.Count}
        """);
    }

    private static void SetupTrackers(InitialPlayerSync packet)
    {
        List<string> completedGoals = packet.StoryGoalData.CompletedGoals;
        StoryGoalManager storyGoalManager = StoryGoalManager.main;
        OnGoalUnlockTracker onGoalUnlockTracker = storyGoalManager.onGoalUnlockTracker;
        CompoundGoalTracker compoundGoalTracker = storyGoalManager.compoundGoalTracker;

        // Initializing CompoundGoalTracker and OnGoalUnlockTracker again (with OnSceneObjectsLoaded) requires us to
        // we first clear what was done in the first iteration of OnSceneObjectsLoaded
        onGoalUnlockTracker.goalUnlocks.Clear();
        compoundGoalTracker.goals.Clear();
        // we force initialized to false so OnSceneObjectsLoaded actually does something
        storyGoalManager.initialized = false;
        storyGoalManager.OnSceneObjectsLoaded();
        
        // Clean LocationGoalTracker, BiomeGoalTracker and ItemGoalTracker already completed goals
        // D5b: except the partner's biome and location lines this player hasn't heard (the server left them out of the
        // PDA log), so this player's own visit still plays them. CoopSettings is read from the packet because the
        // player's initial sync, which stores it, may run after this one.
        bool partnerDiscoveriesSilent = packet.CoopSettings is { PartnerDiscoveriesSilent: true };
        ISet<string> experientialKeys = ExperientialGoals.Keys;
        HashSet<string> pdaLogKeys = new(packet.PDAData.PDALogEntries.Select(entry => entry.Key), StringComparer.OrdinalIgnoreCase);
        storyGoalManager.locationGoalTracker.goals.RemoveAll(goal => !ExperientialGoals.StaysInTracker(goal.key, goal.goalType == Story.GoalType.PDA, partnerDiscoveriesSilent, experientialKeys, completedGoals, pdaLogKeys));
        storyGoalManager.biomeGoalTracker.goals.RemoveAll(goal => !ExperientialGoals.StaysInTracker(goal.key, goal.goalType == Story.GoalType.PDA, partnerDiscoveriesSilent, experientialKeys, completedGoals, pdaLogKeys));

        List<TechType> techTypesToRemove = new();
        foreach (KeyValuePair<TechType, List<ItemGoal>> entry in storyGoalManager.itemGoalTracker.goals)
        {
            // Goals are all triggered at the same time but we don't know if some entries share certain goals
            if (entry.Value.All(goal => completedGoals.Contains(goal.key)))
            {
                techTypesToRemove.Add(entry.Key);
                continue;
            }
        }
        techTypesToRemove.ForEach(techType => storyGoalManager.itemGoalTracker.goals.Remove(techType));

        // OnGoalUnlock might trigger the creation of a signal which is later on set to invisible when getting close to it
        // the invisibility is managed by PingInstance_Set_Patches and is restored during PlayerPreferencesInitialSyncProcessor
        // So we still need to recreate the signals at every game launch

        // To avoid having the SignalPing play its sound we just make its notification null while triggering it
        // (the sound is something like "coordinates added to the gps" or something)
        SignalPing prefabSignalPing = onGoalUnlockTracker.signalPrefab.GetComponent<SignalPing>();
        PDANotification pdaNotification = prefabSignalPing.vo;
        prefabSignalPing.vo = null;

        foreach (OnGoalUnlock onGoalUnlock in onGoalUnlockTracker.unlockData.onGoalUnlocks)
        {
            // The game's own set (filled by SetupStoryGoalManager), which ignores case like OnGoalUnlockTracker does; the
            // packet's list compares case-sensitively (MULTINAUGHTICA_SPEC M2)
            if (storyGoalManager.completedGoals.Contains(onGoalUnlock.goal))
            {
                // Code adapted from OnGoalUnlock.Trigger
                foreach (UnlockSignalData unlockSignalData in onGoalUnlock.signals)
                {
                    unlockSignalData.Trigger(onGoalUnlockTracker);
                }
            }
        }
        
        // recover the notification sound
        prefabSignalPing.vo = pdaNotification;
    }

    // Must happen after CompletedGoals
    private static void SetupAuroraAndSunbeam(InitialPlayerSync packet)
    {
        TimeData timeData = packet.TimeData;

        AuroraWarnings auroraWarnings = Player.mainObject.GetComponentInChildren<AuroraWarnings>(true);
        auroraWarnings.timeSerialized = DayNightCycle.main.timePassedAsFloat;
        auroraWarnings.OnProtoDeserialize(null);

        CrashedShipExploder.main.version = 2;
        CrashedShipExploder.main.initialized = true;
        StoryManager.UpdateAuroraData(timeData.AuroraEventData);
        CrashedShipExploder.main.timeSerialized = DayNightCycle.main.timePassedAsFloat;
        CrashedShipExploder.main.OnProtoDeserialize(null);

        // Sunbeam countdown is deducted from the scheduled goal PrecursorGunAimCheck
        NitroxScheduledGoal sunbeamCountdownGoal = packet.StoryGoalData.ScheduledGoals.Find(goal => string.Equals(goal.GoalKey, "PrecursorGunAimCheck", StringComparison.OrdinalIgnoreCase));
        if (sunbeamCountdownGoal != null)
        {
            StoryGoalCustomEventHandler.main.countdownActive = true;
            StoryGoalCustomEventHandler.main.countdownStartingTime = sunbeamCountdownGoal.TimeExecute - 2370;
            // See StoryGoalCustomEventHandler.endTime for calculation (endTime - 30 seconds)
        }
    }

    // Must happen after CompletedGoals
    private static void SetScheduledGoals(InitialPlayerSync packet)
    {
        List<NitroxScheduledGoal> scheduledGoals = packet.StoryGoalData.ScheduledGoals;
        bool partnerDiscoveriesSilent = packet.CoopSettings is { PartnerDiscoveriesSilent: true };

        // We don't want any scheduled goal we add now to be executed before initial sync has finished, else they might not get broadcasted
        StoryGoalScheduler.main.paused = true;
        Multiplayer.OnLoadingComplete += () => StoryGoalScheduler.main.paused = false;

        foreach (NitroxScheduledGoal scheduledGoal in scheduledGoals)
        {
            // Clear duplicated goals that might have appeared during loading and before sync
            StoryGoalScheduler.main.schedule.RemoveAll(goal => goal.goalKey == scheduledGoal.GoalKey);

            ScheduledGoal goal = new()
            {
                goalKey = scheduledGoal.GoalKey,
                goalType = (Story.GoalType)scheduledGoal.GoalType,
                timeExecute = scheduledGoal.TimeExecute,
            };
            // D5a/D5b: a pending biome or location line is skipped, as in ScheduleProcessor. The server doesn't know whose
            // trigger scheduled it; this player's own tracker schedules it again when they are there.
            if (!StoryGoalManager.main.completedGoals.Contains(goal.goalKey) &&
                !ExperientialGoals.IsOwnFirst(goal.goalKey, goal.goalType == Story.GoalType.PDA, partnerDiscoveriesSilent, ExperientialGoals.Keys))
            {
                StoryGoalScheduler.main.schedule.Add(goal);
            }
        }

        RefreshStoryWithLatestData();
    }

    // Must happen after CompletedGoals
    private static void RefreshStoryWithLatestData()
    {
        // If those aren't set up yet, they'll initialize correctly in time
        // Else, we need to force them to acquire the right data
        if (StoryGoalCustomEventHandler.main)
        {
            StoryGoalCustomEventHandler.main.Awake();
        }
        if (PrecursorGunStoryEvents.main)
        {
            PrecursorGunStoryEvents.main.Start();
        }
    }

    private void SetTimeData(InitialPlayerSync packet)
    {
        timeManager.ProcessUpdate(packet.TimeData.TimePacket);
        timeManager.InitRealTimeElapsed(packet.TimeData.TimePacket.RealTimeElapsed, packet.TimeData.TimePacket.UpdateTime, packet.IsFirstPlayer);
        timeManager.AuroraRealExplosionTime = packet.TimeData.AuroraEventData.AuroraRealExplosionTime;
    }
}
