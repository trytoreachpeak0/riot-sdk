namespace RIoT.Sdk.Core;

/// <summary>
/// An <c>act</c> mission appended after the move mission of a byDefaultMissions order
/// (for example charging: actionId 78, actionParam1 1, actionParam2 0).
/// Keeps callers off the Kiota-generated MissionDTO.
/// </summary>
public sealed record OrderMissionAction
{
    public OrderMissionAction(int actionId, int actionParam1, int actionParam2)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actionId);
        ArgumentOutOfRangeException.ThrowIfNegative(actionParam1);
        ArgumentOutOfRangeException.ThrowIfNegative(actionParam2);
        ActionId = actionId;
        ActionParam1 = actionParam1;
        ActionParam2 = actionParam2;
    }

    public int ActionId { get; }
    public int ActionParam1 { get; }
    public int ActionParam2 { get; }
}
