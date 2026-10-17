namespace RIoT.Sdk.Core;

/// <summary>
/// Stable projection of the RIoT vehicle-card response.
/// </summary>
public sealed record VehicleCard(
    string DeviceKey,
    bool? Enable,
    int? Status,
    string? ProcState,
    string? CurrentMap,
    int? CurrentPosition,
    int? BatteryPercent,
    string? BatteryState,
    double? Speed,
    int? LockStatus,
    string? OrderTaskId);

/// <summary>
/// Stable projection of the diagnostic vehicle and vehicle-task response.
/// </summary>
/// <remarks>
/// <see cref="Mode"/> is the vehicle's <c>mode</c> when RIoT sent exactly <c>MODE_AUTO</c>, <c>MODE_MANUAL</c> or <c>UNKNOWN</c>
/// (case-sensitive, no surrounding space, no number); null when it left the field out, sent null, or sent anything else. A manually driven vehicle can move with no movement task at all
/// (control-server#639), so a consumer proving standstill should require <c>MODE_AUTO</c> rather than reject <c>MODE_MANUAL</c>.
/// </remarks>
public sealed record VehicleExecutionFacts(
    string DeviceKey,
    string? MovementState,
    string? ControlState,
    string? EmergencyState,
    string? BreakSwitchState,
    string? LocationState,
    double? Speed,
    string? ProcState,
    bool? ProcessingOrder,
    bool? Enable,
    string? IntegrationLevel,
    string? Mode);
