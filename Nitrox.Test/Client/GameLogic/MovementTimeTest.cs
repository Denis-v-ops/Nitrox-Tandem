using System.Reflection.Emit;
using HarmonyLib;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M6: movement snapshots are stamped with the world's active play time
///     (<see cref="TimeManager.RealTimeElapsed" />), which is saved with the world and grows with its age. In a world 40 h
///     old a float only holds it to 15.6 ms, so the times stay doubles from the broadcaster to the replicator.
/// </summary>
[TestClass]
public class MovementTimeTest
{
    /// <summary>40 h of play, past 2^17 s.</summary>
    private const double FORTY_HOURS = 144000;

    /// <summary>1 h, 9.2 h (the float step doubles to 3.9 ms), 18.3 h, 40 h and 100 h of play.</summary>
    private static readonly double[] worldAges = [3600, 33100, 65800, FORTY_HOURS, 360000];

    [TestMethod]
    public void AFloatStepsByMillisecondsInAFortyHourWorld()
    {
        // The premise: what the times were rounded to before
        float time = (float)FORTY_HOURS;
        (MathF.BitIncrement(time) - time).Should().Be(0.015625f);
    }

    [TestMethod]
    public void TheInterpolationFactorAdvancesEveryFrameInAFortyHourWorld()
    {
        // Two snapshots 0.05 s apart, shown at 120 frames per second: the factor goes up by 1/6 each frame
        double previous = FORTY_HOURS + 0.0123;
        double next = previous + 0.05;
        float worstOldError = 0;
        for (int frame = 0; frame <= 6; frame++)
        {
            double now = previous + frame / 120.0;
            float expected = frame / 6f;

            MovementTime.InterpolationFactor(now, previous, next).Should().BeApproximately(expected, 1e-4f, $"frame {frame}");

            // What MovementReplicator.Update computed with float times: the same factor on two frames in a row, then a skip
            float old = ((float)now - (float)previous) / ((float)next - (float)previous);
            worstOldError = Math.Max(worstOldError, Math.Abs(old - expected));
        }
        worstOldError.Should().BeGreaterThan(0.1f, "this test must be one that float times fail");
    }

    [TestMethod]
    public void TheInterpolationFactorIsExactAtEveryWorldAge()
    {
        foreach (double worldAge in worldAges)
        {
            double previous = worldAge + 10.0123;
            double next = previous + MovementBroadcaster.BROADCAST_PERIOD;

            MovementTime.InterpolationFactor(previous, previous, next).Should().Be(0f);
            MovementTime.InterpolationFactor(next, previous, next).Should().Be(1f);
            for (int i = 1; i < 10; i++)
            {
                double now = previous + i * 0.1 * MovementBroadcaster.BROADCAST_PERIOD;
                MovementTime.InterpolationFactor(now, previous, next).Should().BeApproximately(i * 0.1f, 1e-4f, $"world of {worldAge} s");
            }
        }
    }

    [TestMethod]
    public void TheAgeOfASnapshotIsExactAtEveryWorldAge()
    {
        // 200 snapshots 20 ms old, at uneven times (not on any float step), as the replicator's latency sample sees them
        const double AGE = 0.020;
        foreach (double worldAge in worldAges)
        {
            float worstError = 0;
            float worstOldError = 0;
            for (int i = 0; i < 200; i++)
            {
                double sent = worldAge + i * 0.0517;
                double now = sent + AGE;
                worstError = Math.Max(worstError, Math.Abs(MovementTime.Age(now, sent) - (float)AGE));
                worstOldError = Math.Max(worstOldError, Math.Abs((float)now - (float)sent - (float)AGE));
            }

            worstError.Should().BeLessThan(1e-6f, $"world of {worldAge} s");
            if (worldAge >= FORTY_HOURS)
            {
                worstOldError.Should().BeGreaterThan(0.004f, $"float times, world of {worldAge} s");
            }
        }
    }

    [TestMethod]
    public void AgeIsNegativeForATimeAhead()
    {
        // HandOverMotion clamps it: on two PCs the clocks can differ by more than the latency
        MovementTime.Age(FORTY_HOURS + 0.5, FORTY_HOURS + 0.75).Should().BeApproximately(-0.25f, 1e-6f);
    }

    [TestMethod]
    public void SnapshotTimesStayDoubleFromTheBroadcasterToTheReplicator()
    {
        const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Sender: the clock it reads and the time it stamps on VehicleMovements
        typeof(MovementBroadcaster).GetField("latestBroadcastTime", INSTANCE)!.FieldType.Should().Be<double>();
        typeof(MovementBroadcaster).GetMethod(nameof(MovementBroadcaster.BroadcastLocalData))!.GetParameters()[0].ParameterType.Should().Be<double>();
        typeof(VehicleMovements).GetProperty(nameof(VehicleMovements.RealTime))!.PropertyType.Should().Be<double>();

        // Receiver: the snapshot time, the clock it's compared with, the buffered times
        typeof(MovementReplicator).GetMethod(nameof(MovementReplicator.AddSnapshot))!.GetParameters()[1].ParameterType.Should().Be<double>();
        PropertyInfo realTime = typeof(MovementReplicator).GetProperty("RealTime", INSTANCE);
        realTime!.PropertyType.Should().Be<double>();
        typeof(MovementReplicator).GetField("latestLatencyBumpTime", INSTANCE)!.FieldType.Should().Be<double>();
        typeof(MovementReplicator.Snapshot).GetProperty(nameof(MovementReplicator.Snapshot.Time))!.PropertyType.Should().Be<double>();
        typeof(MovementReplicator.Snapshot).GetProperty(nameof(MovementReplicator.Snapshot.SentTime))!.PropertyType.Should().Be<double>();

        // No float cast left where the times pass through (a double parameter still accepts a float)
        MethodInfo[] passThrough =
        [
            typeof(MovementBroadcaster).GetMethod(nameof(MovementBroadcaster.Update))!,
            typeof(VehicleMovementsProcessor).GetMethod(nameof(VehicleMovementsProcessor.Process))!,
            realTime.GetMethod!
        ];
        foreach (MethodInfo method in passThrough)
        {
            PatchProcessor.ReadMethodBody(method).Select(instruction => instruction.Key).Should().NotContain(OpCodes.Conv_R4, $"{method.DeclaringType!.Name}.{method.Name} rounds a time to a float");
        }
    }
}
