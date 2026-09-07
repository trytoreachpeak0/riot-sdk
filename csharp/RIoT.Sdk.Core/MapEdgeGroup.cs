namespace RIoT.Sdk.Core;

/// <summary>
/// One edge's membership in a named edge group — a traffic constraint over the route graph
/// (BC-MAP-003).
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /api/imap/v1/mapEdgeGroup/all</c> returns every Map's groups keyed by group name, with
/// one entry per member edge. The facade flattens that map, copying the outer key into
/// <see cref="GroupName"/>, and returns entries for every Map — filter by <see cref="MapId"/>.
/// </para>
/// <para>
/// Two wire quirks: this endpoint is camelCase while <c>edges</c> and <c>stations</c> on the same
/// service are snake_case, and its timestamps are <c>"2024-06-26 10:57:58"</c>, which is not
/// ISO 8601 and so is carried as the raw string rather than a parsed instant.
/// </para>
/// <para>
/// Whether these constraints change RIoT's own routing result is untested (Round 43).
/// </para>
/// </remarks>
/// <param name="GroupName">The outer key of the response map, e.g. <c>老厂电梯</c>.</param>
/// <param name="Id">Wire <c>id</c> — the membership record's id.</param>
/// <param name="MapId">Wire <c>mapId</c>.</param>
/// <param name="MapName">Wire <c>mapName</c>.</param>
/// <param name="EdgeId">Wire <c>edgeId</c>, matching <see cref="MapEdge.Id"/> on that Map.</param>
/// <param name="Type">
/// Wire <c>type</c>, e.g. <c>SINGLE_VEHICLE_ONLY</c>, <c>SAME_DIRECTION_ONLY</c>. Carried as the
/// raw string: an unknown value must reach the consumer intact, not collapse into an enum default.
/// </param>
/// <param name="IsDeleted">Wire <c>isDelete</c>, an int on the wire (0 or 1).</param>
/// <param name="GmtCreate">Wire <c>gmtCreate</c>, raw string.</param>
/// <param name="GmtUpdate">Wire <c>gmtUpdate</c>, raw string. Usable as a change fingerprint.</param>
public sealed record MapEdgeGroup(
    string GroupName,
    int Id,
    int MapId,
    string MapName,
    int EdgeId,
    string Type,
    bool IsDeleted,
    string? GmtCreate,
    string? GmtUpdate);
