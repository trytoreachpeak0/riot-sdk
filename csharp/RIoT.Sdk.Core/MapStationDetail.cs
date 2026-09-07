namespace RIoT.Sdk.Core;

/// <summary>
/// A station with its placement on the Map's edge graph (BC-MAP-003).
/// Richer than <see cref="Station"/>, which carries only id and name.
/// </summary>
/// <remarks>
/// The wire format spells several keys with a literal dot — <c>pos.x</c>, not a nested object —
/// which no generated model reads, so the facade deserializes this shape itself. The four
/// navigation pose groups on the wire (<c>check_pos.*</c>, <c>enter_pos.*</c>, <c>exit_pos.*</c>,
/// <c>pgv_offset.*</c>) are deliberately not carried: they are vehicle motion parameters, not
/// route graph facts.
/// </remarks>
/// <param name="MapId">The Map this station was read from; not on the wire.</param>
/// <param name="StationId">Wire <c>id</c>.</param>
/// <param name="Name">Wire <c>name</c>.</param>
/// <param name="EdgeId">
/// Wire <c>edge_id</c> — the edge this station sits on. Which of that edge's two nodes it
/// occupies is decided by comparing <see cref="PosX"/>/<see cref="PosY"/> against the edge's
/// endpoints; on map25 that gave 206 stations on 206 distinct nodes with zero collisions.
/// </param>
/// <param name="PosX">Wire <c>pos.x</c>, mm.</param>
/// <param name="PosY">Wire <c>pos.y</c>, mm.</param>
/// <param name="PosYaw">Wire <c>pos.yaw</c>.</param>
/// <param name="StationOffset">
/// Wire <c>station_offset</c>. Zero on all 206 map25 stations, so it cannot be used to place
/// a station along its edge — carried for completeness, not for use.
/// </param>
/// <param name="Type">Wire <c>type</c>.</param>
/// <param name="Description">Wire <c>desc</c>.</param>
public sealed record MapStationDetail(
    int MapId,
    int StationId,
    string Name,
    int EdgeId,
    double PosX,
    double PosY,
    double PosYaw,
    int StationOffset,
    int Type,
    string Description);
