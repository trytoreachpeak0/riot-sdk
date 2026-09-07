using System.Text.Json;
using RIoT.Sdk.Core;

namespace RIoT.Sdk.Facade;

/// <summary>
/// Thin imap Map facade (BC-MAP-001 / ADR-sdk-0006).
/// </summary>
public sealed class MapClient
{
    private readonly RiotSession _session;

    internal MapClient(RiotSession session) => _session = session;

    /// <summary>
    /// GET /api/imap/v1/mapInfo/getALLMapInfoExcludeMapJson — list Maps without mapJson (BC-MAP-001).
    /// Throws <see cref="RiotApiException"/> on business failure (ADR-sdk-0005).
    /// </summary>
    public async Task<IReadOnlyList<Map>> ListMapsAsync(CancellationToken cancellationToken = default)
    {
        var client = _session.CreateGeneratedImapClient();
        var response = RiotBusinessResponse.RequireResponse(
            await client.Api.Imap.V1.MapInfo.GetALLMapInfoExcludeMapJson
                .GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            "getALLMapInfoExcludeMapJson");

        RiotBusinessResponse.EnsureSuccess(response.Code, response.Message);

        var result = response.Result ?? [];
        return result
            .Where(m => m.Id is not null && !string.IsNullOrWhiteSpace(m.Name))
            .Select(m => new Map(m.Id!.Value, m.Name!))
            .ToList();
    }

    /// <summary>
    /// GET /api/imap/v1/mapInfo/stations/{mapId} — list Stations on a Map (BC-MAP-002).
    /// Throws <see cref="RiotApiException"/> on business failure (ADR-sdk-0005).
    /// Empty list on success is a valid domain result (e.g. invalid mapId=0).
    /// </summary>
    public async Task<IReadOnlyList<Station>> ListStationsAsync(
        int mapId,
        CancellationToken cancellationToken = default)
    {
        var client = _session.CreateGeneratedImapClient();
        var response = RiotBusinessResponse.RequireResponse(
            await client.Api.Imap.V1.MapInfo.Stations[mapId]
                .GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            $"stations/{mapId}");

        RiotBusinessResponse.EnsureSuccess(response.Code, response.Message);

        var result = response.Result ?? [];
        return result
            .Where(s => s.Id is not null && !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => new Station(mapId, s.Id!.Value, s.Name!))
            .ToList();
    }

    /// <summary>
    /// Strict station-catalog read for safety-sensitive consumers. Unlike
    /// <see cref="ListStationsAsync"/>, malformed and duplicate rows are rejected rather
    /// than silently filtered.
    /// </summary>
    public async Task<IReadOnlyList<Station>> ListStationsStrictAsync(
        int mapId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapId);

        var client = _session.CreateGeneratedImapClient();
        var response = RiotBusinessResponse.RequireResponse(
            await client.Api.Imap.V1.MapInfo.Stations[mapId]
                .GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            $"stations/{mapId}");

        RiotBusinessResponse.EnsureSuccess(response.Code, response.Message);
        if (response.Result is null)
        {
            throw new RiotApiException(
                $"stations/{mapId} returned null result.",
                businessCode: "station-catalog-missing");
        }
        if (response.Result.Count == 0)
        {
            throw new RiotApiException(
                $"stations/{mapId} returned an empty station catalog.",
                businessCode: "station-catalog-empty");
        }

        List<Station> stations = new(response.Result.Count);
        HashSet<int> stationIds = [];
        foreach (var row in response.Result)
        {
            if (row.Id is null or <= 0 || string.IsNullOrWhiteSpace(row.Name))
            {
                throw new RiotApiException(
                    $"stations/{mapId} returned a malformed station row.",
                    businessCode: "station-catalog-entry-invalid");
            }
            if (!stationIds.Add(row.Id.Value))
            {
                throw new RiotApiException(
                    $"stations/{mapId} returned duplicate station id {row.Id.Value}.",
                    businessCode: "station-catalog-duplicate");
            }

            stations.Add(new Station(mapId, row.Id.Value, row.Name));
        }

        return stations;
    }

    /// <summary>
    /// GET /api/imap/v1/mapInfo/edges/{mapId} — the Map's full directed edge table, the design-time
    /// half of its route graph (BC-MAP-003).
    /// Throws <see cref="RiotApiException"/> on business failure (ADR-sdk-0005).
    /// </summary>
    /// <remarks>
    /// The graph is directed: on map25, only 148 of 403 edges have a reverse twin, so a shortest
    /// path computed as if it were undirected is wrong.
    /// </remarks>
    public async Task<IReadOnlyList<MapEdge>> ListEdgesAsync(
        int mapId,
        CancellationToken cancellationToken = default)
    {
        var operation = $"mapInfo/edges/{mapId}";
        var client = _session.CreateGeneratedImapClient();
        var request = client.Api.Imap.V1.MapInfo.Edges[mapId].ToGetRequestInformation();

        return await ImapWire.ReadAsync(
            _session.Adapter,
            request,
            operation,
            result => ImapWire.MapArray(result, operation, edge => new MapEdge(
                Id: ImapWire.RequireInt(edge, operation, "id"),
                StartNode: ImapWire.RequireInt(edge, operation, "s_node", "snode"),
                EndNode: ImapWire.RequireInt(edge, operation, "e_node", "enode"),
                CostMm: ImapWire.RequireDouble(edge, operation, "cost"),
                StartX: ImapWire.RequireInt(edge, operation, "sx"),
                StartY: ImapWire.RequireInt(edge, operation, "sy"),
                EndX: ImapWire.RequireInt(edge, operation, "ex"),
                EndY: ImapWire.RequireInt(edge, operation, "ey"),
                StartFacing: ImapWire.OptionalDouble(edge, 0, "s_facing", "sfacing"),
                EndFacing: ImapWire.OptionalDouble(edge, 0, "e_facing", "efacing"),
                Direction: ImapWire.OptionalInt(edge, 0, "direction"),
                IsBackEdge: ImapWire.OptionalBool(edge, false, "is_back_edge", "isBackEdge"),
                Type: ImapWire.OptionalInt(edge, 0, "type"),
                Description: ImapWire.OptionalString(edge, "desc") ?? string.Empty)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// GET /api/imap/v1/mapInfo/stations/{mapId} — stations with their placement on the edge graph
    /// (BC-MAP-003). Use <see cref="ListStationsAsync"/> when only id and name are needed.
    /// Throws <see cref="RiotApiException"/> on business failure (ADR-sdk-0005).
    /// </summary>
    public async Task<IReadOnlyList<MapStationDetail>> ListStationDetailsAsync(
        int mapId,
        CancellationToken cancellationToken = default)
    {
        var operation = $"mapInfo/stations/{mapId}";
        var client = _session.CreateGeneratedImapClient();
        var request = client.Api.Imap.V1.MapInfo.Stations[mapId].ToGetRequestInformation();

        return await ImapWire.ReadAsync(
            _session.Adapter,
            request,
            operation,
            result => ImapWire.MapArray(result, operation, station => new MapStationDetail(
                MapId: mapId,
                StationId: ImapWire.RequireInt(station, operation, "id"),
                Name: ImapWire.RequireString(station, operation, "name"),
                EdgeId: ImapWire.RequireInt(station, operation, "edge_id", "edgeId"),
                PosX: ImapWire.RequireDouble(station, operation, "pos.x"),
                PosY: ImapWire.RequireDouble(station, operation, "pos.y"),
                PosYaw: ImapWire.OptionalDouble(station, 0, "pos.yaw"),
                StationOffset: ImapWire.OptionalInt(station, 0, "station_offset", "stationOffset"),
                Type: ImapWire.OptionalInt(station, 0, "type"),
                Description: ImapWire.OptionalString(station, "desc") ?? string.Empty)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// GET /api/imap/v1/mapResource/removedEdge/{mapId} — edges currently removed from the Map, the
    /// runtime half of its route graph (BC-MAP-003).
    /// Throws <see cref="RiotApiException"/> on business failure (ADR-sdk-0005).
    /// </summary>
    /// <remarks>
    /// An empty list means "nothing removed" and is the normal case — map25 returned exactly that
    /// in Round 43. An element whose edge id cannot be read raises rather than being skipped: a
    /// dropped removal would leave the caller routing over an edge that no longer exists.
    /// </remarks>
    public async Task<IReadOnlyList<RemovedEdge>> ListRemovedEdgesAsync(
        int mapId,
        CancellationToken cancellationToken = default)
    {
        var operation = $"mapResource/removedEdge/{mapId}";
        var client = _session.CreateGeneratedImapClient();
        var request = client.Api.Imap.V1.MapResource.RemovedEdge[mapId].ToGetRequestInformation();

        return await ImapWire.ReadAsync(
            _session.Adapter,
            request,
            operation,
            result => ImapWire.MapArray(result, operation, removed => new RemovedEdge(
                Id: ImapWire.OptionalInt(removed, 0, "id"),
                MapId: ImapWire.OptionalInt(removed, mapId, "mapId", "map_id"),
                EdgeId: ImapWire.RequireInt(removed, operation, "edgeId", "edge_id"))),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// GET /api/imap/v1/mapResource/removedStation/{mapId} — stations currently removed from the
    /// Map (BC-MAP-003). Same fail-closed reading as <see cref="ListRemovedEdgesAsync"/>.
    /// Throws <see cref="RiotApiException"/> on business failure (ADR-sdk-0005).
    /// </summary>
    public async Task<IReadOnlyList<RemovedStation>> ListRemovedStationsAsync(
        int mapId,
        CancellationToken cancellationToken = default)
    {
        var operation = $"mapResource/removedStation/{mapId}";
        var client = _session.CreateGeneratedImapClient();
        var request = client.Api.Imap.V1.MapResource.RemovedStation[mapId].ToGetRequestInformation();

        return await ImapWire.ReadAsync(
            _session.Adapter,
            request,
            operation,
            result => ImapWire.MapArray(result, operation, removed => new RemovedStation(
                Id: ImapWire.OptionalInt(removed, 0, "id"),
                MapId: ImapWire.OptionalInt(removed, mapId, "mapId", "map_id"),
                StationId: ImapWire.RequireInt(removed, operation, "stationId", "station_id"),
                StationName: ImapWire.OptionalString(removed, "stationName", "station_name"))),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// GET /api/imap/v1/mapEdgeGroup/all — every Map's edge groups, flattened to one entry per
    /// member edge with the group name copied in (BC-MAP-003).
    /// Throws <see cref="RiotApiException"/> on business failure (ADR-sdk-0005).
    /// </summary>
    /// <remarks>
    /// This endpoint covers all Maps, not one — filter by <see cref="MapEdgeGroup.MapId"/>.
    /// </remarks>
    public async Task<IReadOnlyList<MapEdgeGroup>> ListEdgeGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        const string operation = "mapEdgeGroup/all";
        var client = _session.CreateGeneratedImapClient();
        var request = client.Api.Imap.V1.MapEdgeGroup.All.ToGetRequestInformation();

        return await ImapWire.ReadAsync(
            _session.Adapter,
            request,
            operation,
            result => MapEdgeGroups(result, operation),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Underlying Kiota imap client for endpoints not yet wrapped.</summary>
    public RIoT.Sdk.Generated.Imap.ImapClient Raw => _session.CreateGeneratedImapClient();

    /// <summary>
    /// Flattens the <c>{ "群组名": [ ... ] }</c> result of mapEdgeGroup/all. The outer key is the
    /// group name and is not repeated inside the elements, so it is copied into each one.
    /// </summary>
    private static IReadOnlyList<MapEdgeGroup> MapEdgeGroups(JsonElement result, string operation)
    {
        if (result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return [];
        }

        // Round 43 saw this result populated, as an object keyed by group name — but map25, the
        // 8005 working Map, has no groups at all, so what an empty one looks like was never
        // observed. An empty array is the other plausible spelling of "none" and can only mean
        // that; a populated array would be a shape nobody has seen, and fails closed below.
        if (result.ValueKind == JsonValueKind.Array && result.GetArrayLength() == 0)
        {
            return [];
        }

        if (result.ValueKind != JsonValueKind.Object)
        {
            throw new RiotApiException(
                $"{operation} returned result of kind {result.ValueKind}, expected an object keyed by group name.");
        }

        var groups = new List<MapEdgeGroup>();
        foreach (var group in result.EnumerateObject())
        {
            groups.AddRange(ImapWire.MapArray(group.Value, operation, member => new MapEdgeGroup(
                GroupName: group.Name,
                Id: ImapWire.OptionalInt(member, 0, "id"),
                MapId: ImapWire.RequireInt(member, operation, "mapId", "map_id"),
                MapName: ImapWire.OptionalString(member, "mapName", "map_name") ?? string.Empty,
                EdgeId: ImapWire.RequireInt(member, operation, "edgeId", "edge_id"),
                Type: ImapWire.OptionalString(member, "type") ?? string.Empty,
                IsDeleted: ImapWire.OptionalBool(member, false, "isDelete", "is_delete"),
                GmtCreate: ImapWire.OptionalString(member, "gmtCreate", "gmt_create"),
                GmtUpdate: ImapWire.OptionalString(member, "gmtUpdate", "gmt_update"))));
        }

        return groups;
    }
}
