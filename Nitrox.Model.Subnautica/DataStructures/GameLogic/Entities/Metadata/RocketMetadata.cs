using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BinaryPack.Attributes;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

[Serializable]
[DataContract]
public class RocketMetadata : EntityMetadata
{
    [DataMember(Order = 1)]
    public int CurrentStage { get; set; }

    [DataMember(Order = 2)]
    public float LastStageTransitionTime { get; set; }

    [DataMember(Order = 3)]
    public int ElevatorState { get; set; }

    [DataMember(Order = 4)]
    public float ElevatorPosition { get; set; }

    [DataMember(Order = 5)]
    public List<int> PreflightChecks { get; set; } = [];

    [IgnoreConstructor]
    protected RocketMetadata()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public RocketMetadata(int currentStage, float lastStageTransitionTime, int elevatorState, float elevatorPosition, List<int> preflightChecks)
    {
        CurrentStage = currentStage;
        LastStageTransitionTime = lastStageTransitionTime;
        ElevatorState = elevatorState;
        ElevatorPosition = elevatorPosition;
        PreflightChecks = preflightChecks;
    }

    /// <summary>
    ///     What the server keeps when a player reports the rocket (MULTINAUGHTICA_SPEC M2). A report is the sender's whole view
    ///     of the rocket, and two players acting within one round trip each report a view that lacks the other's action.
    ///     Stages only go up and a thrown preflight check stays thrown, so both are merged; the elevator is what the reporter
    ///     sees (the last one to use it wins); the time of the last stage transition only moves with the stage, so a later
    ///     report of the same stage doesn't make a joiner replay the build of the last stage with construction bots.
    /// </summary>
    public static RocketMetadata Merge(RocketMetadata? current, RocketMetadata incoming)
    {
        if (current == null)
        {
            return incoming;
        }

        bool stageAdvanced = incoming.CurrentStage > current.CurrentStage;
        List<int> checks = current.PreflightChecks.Union(incoming.PreflightChecks).OrderBy(check => check).ToList();
        return new RocketMetadata(Math.Max(current.CurrentStage, incoming.CurrentStage),
                                  stageAdvanced ? incoming.LastStageTransitionTime : current.LastStageTransitionTime,
                                  incoming.ElevatorState,
                                  incoming.ElevatorPosition,
                                  checks);
    }

    public override string ToString()
    {
        return $"[{nameof(RocketMetadata)} CurrentStage: {CurrentStage}, LastStageTransitionTime: {LastStageTransitionTime}, ElevatorState: {ElevatorState}, ElevatorPosition: {ElevatorPosition}, PreflightChecks: {string.Join(",", PreflightChecks)}]";
    }
}
