namespace NitroxClient.GameLogic.Simulation;

/// <summary>
///     The Bleeder attack that waits for the exclusive simulation lock of its creature (<see cref="BleederClaims" />).
/// </summary>
public class BleederClaim : LockRequestContext
{
    public AttachAndSuck Attack { get; }

    public BleederClaim(AttachAndSuck attack)
    {
        Attack = attack;
    }
}
