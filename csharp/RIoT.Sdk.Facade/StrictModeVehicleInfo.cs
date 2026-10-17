using Microsoft.Kiota.Abstractions.Serialization;
using RIoT.Sdk.Generated.TaskApi.Models;

namespace RIoT.Sdk.Facade;

/// <summary>
/// <see cref="VehicleAllInfo"/> whose <c>vehicle</c> is read as a <see cref="StrictModeVehicle"/>; everything else is the
/// generated parse.
/// </summary>
internal sealed class StrictModeVehicleAllInfo : VehicleAllInfo
{
    internal static StrictModeVehicleAllInfo CreateFromParseNode(IParseNode parseNode)
    {
        ArgumentNullException.ThrowIfNull(parseNode);
        return new StrictModeVehicleAllInfo();
    }

    public override IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
    {
        IDictionary<string, Action<IParseNode>> fields = base.GetFieldDeserializers();
        fields["vehicle"] = node => Vehicle = node.GetObjectValue(StrictModeVehicle.CreateFromParseNode);
        return fields;
    }
}

/// <summary>
/// <see cref="Vehicle"/> with <c>mode</c> kept as the raw string RIoT sent (<see cref="RawMode"/>) rather than parsed into
/// <see cref="Vehicle_mode"/>; every other field is the generated parse.
/// </summary>
/// <remarks>
/// <para>
/// Kiota 1.17.4's <c>EnumHelpers.GetEnumValue</c> ends in a case-insensitive <c>Enum.TryParse</c> that also takes numbers, so the
/// generated enum read <c>"0"</c>, <c>" 0"</c>, <c>"mode_auto"</c> and <c>" MODE_AUTO"</c> as MODE_AUTO, <c>"3"</c> as
/// <c>"3"</c> and <c>"MODE_AUTO,MODE_MANUAL"</c> as MODE_MANUAL (riot-sdk#3 review S1). A consumer proves standstill on
/// MODE_AUTO (control-server#639), so <see cref="ExactMode"/> lets through only the declared spellings, the same as the
/// Python facade's <c>strict_mode_vehicle_info</c>.
/// </para>
/// <para>
/// Only the parse is replaced, never the request: the facade sends through the same <c>SendAsync</c> the generated
/// <c>GetAsync</c> uses, so timeouts, disposal and failures stay the generated read's (riot-sdk#3 second review M1, S1).
/// </para>
/// </remarks>
internal sealed class StrictModeVehicle : Vehicle
{
    /// <summary>The three <c>mode</c> values RIoT's task API declares.</summary>
    private static readonly string[] KnownModes = ["MODE_AUTO", "MODE_MANUAL", "UNKNOWN"];

    /// <summary>The <c>mode</c> member as sent; null when absent, null, or not a JSON string.</summary>
    internal string? RawMode { get; private set; }

    internal static StrictModeVehicle CreateFromParseNode(IParseNode parseNode)
    {
        ArgumentNullException.ThrowIfNull(parseNode);
        return new StrictModeVehicle();
    }

    /// <summary><paramref name="raw"/> when it is exactly one of <see cref="KnownModes"/>, otherwise null.</summary>
    internal static string? ExactMode(string? raw) =>
        Array.Find(KnownModes, known => string.Equals(known, raw, StringComparison.Ordinal));

    public override IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
    {
        IDictionary<string, Action<IParseNode>> fields = base.GetFieldDeserializers();
        fields["mode"] = node => RawMode = node.GetStringValue();
        return fields;
    }
}
