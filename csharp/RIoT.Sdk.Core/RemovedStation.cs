namespace RIoT.Sdk.Core;

/// <summary>
/// A station currently removed from a Map — runtime state of the route graph (BC-MAP-003).
/// </summary>
/// <remarks>
/// Same evidence status as <see cref="RemovedEdge"/>: <c>result: []</c> on map25, shape taken
/// from the <c>MapRemovedStation对象</c> component, never seen populated, and read fail-closed.
/// </remarks>
/// <param name="Id">Wire <c>id</c> — the removal record's own id, not the station's.</param>
/// <param name="MapId">Wire <c>mapId</c>.</param>
/// <param name="StationId">Wire <c>stationId</c>, matching <see cref="Station.StationId"/>.</param>
/// <param name="StationName">Wire <c>stationName</c>; null when absent.</param>
public sealed record RemovedStation(int Id, int MapId, int StationId, string? StationName);
