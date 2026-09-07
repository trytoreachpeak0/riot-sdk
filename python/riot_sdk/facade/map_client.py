from __future__ import annotations

from typing import TYPE_CHECKING

from riot_sdk.core.business_response import ensure_success, require_response
from riot_sdk.core.exceptions import RiotApiException
from riot_sdk.core.maps import Map
from riot_sdk.core.route_graph import (
    MapEdge,
    MapEdgeGroup,
    MapStationDetail,
    RemovedEdge,
    RemovedStation,
)
from riot_sdk.core.stations import Station
from riot_sdk.facade import riot_wire

if TYPE_CHECKING:
    from riot_sdk.facade.session import RiotSession


class MapClient:
    """Thin imap Map facade (BC-MAP-001 / ADR-sdk-0006)."""

    def __init__(self, session: RiotSession) -> None:
        self._session = session

    async def list_maps(self) -> list[Map]:
        """GET /api/imap/v1/mapInfo/getALLMapInfoExcludeMapJson (BC-MAP-001 / ADR-sdk-0005)."""
        client = self._session.create_generated_imap_client()
        response = require_response(
            await client.api.imap.v1.map_info.get_a_l_l_map_info_exclude_map_json.get(),
            "getALLMapInfoExcludeMapJson",
        )
        ensure_success(response.code, response.message)
        result = response.result or []
        return [
            Map(map_id=m.id, name=m.name)
            for m in result
            if m.id is not None and m.name
        ]

    async def list_stations(self, map_id: int) -> list[Station]:
        """GET /api/imap/v1/mapInfo/stations/{mapId} (BC-MAP-002 / ADR-sdk-0005)."""
        client = self._session.create_generated_imap_client()
        response = require_response(
            await client.api.imap.v1.map_info.stations.by_map_id(map_id).get(),
            f"stations/{map_id}",
        )
        ensure_success(response.code, response.message)
        result = response.result or []
        return [
            Station(map_id=map_id, station_id=s.id, name=s.name)
            for s in result
            if s.id is not None and s.name
        ]

    async def list_stations_strict(self, map_id: int) -> list[Station]:
        """Reject malformed or duplicate station rows instead of filtering them."""
        if map_id <= 0:
            raise ValueError("map_id must be positive")

        client = self._session.create_generated_imap_client()
        response = require_response(
            await client.api.imap.v1.map_info.stations.by_map_id(map_id).get(),
            f"stations/{map_id}",
        )
        ensure_success(response.code, response.message)
        if response.result is None:
            raise RiotApiException(
                f"stations/{map_id} returned null result.",
                business_code="station-catalog-missing",
            )
        if not response.result:
            raise RiotApiException(
                f"stations/{map_id} returned an empty station catalog.",
                business_code="station-catalog-empty",
            )

        station_ids: set[int] = set()
        stations: list[Station] = []
        for row in response.result:
            if row.id is None or row.id <= 0 or not row.name or not row.name.strip():
                raise RiotApiException(
                    f"stations/{map_id} returned a malformed station row.",
                    business_code="station-catalog-entry-invalid",
                )
            if row.id in station_ids:
                raise RiotApiException(
                    f"stations/{map_id} returned duplicate station id {row.id}.",
                    business_code="station-catalog-duplicate",
                )
            station_ids.add(row.id)
            stations.append(Station(map_id=map_id, station_id=row.id, name=row.name))
        return stations

    async def list_edges(self, map_id: int) -> list[MapEdge]:
        """GET /api/imap/v1/mapInfo/edges/{mapId} (BC-MAP-003 / ADR-sdk-0005).

        The Map's full directed edge table, the design-time half of its route graph. The graph
        is directed: on map25, only 148 of 403 edges have a reverse twin, so a shortest path
        computed as if it were undirected is wrong.
        """
        operation = f"mapInfo/edges/{map_id}"
        client = self._session.create_generated_imap_client()
        request = client.api.imap.v1.map_info.edges.by_map_id(map_id).to_get_request_information()
        result = await riot_wire.read_async(
            self._session.request_adapter, request, operation
        )

        return riot_wire.map_array(
            result,
            operation,
            lambda edge: MapEdge(
                id=riot_wire.require_int(edge, operation, "id"),
                start_node=riot_wire.require_int(edge, operation, "s_node", "snode"),
                end_node=riot_wire.require_int(edge, operation, "e_node", "enode"),
                cost_mm=riot_wire.require_float(edge, operation, "cost"),
                start_x=riot_wire.require_int(edge, operation, "sx"),
                start_y=riot_wire.require_int(edge, operation, "sy"),
                end_x=riot_wire.require_int(edge, operation, "ex"),
                end_y=riot_wire.require_int(edge, operation, "ey"),
                start_facing=riot_wire.optional_float(edge, 0.0, "s_facing", "sfacing"),
                end_facing=riot_wire.optional_float(edge, 0.0, "e_facing", "efacing"),
                direction=riot_wire.optional_int(edge, 0, "direction"),
                is_back_edge=riot_wire.optional_bool(edge, False, "is_back_edge", "isBackEdge"),
                type=riot_wire.optional_int(edge, 0, "type"),
                description=riot_wire.optional_str(edge, "desc") or "",
            ),
        )

    async def list_station_details(self, map_id: int) -> list[MapStationDetail]:
        """GET /api/imap/v1/mapInfo/stations/{mapId} (BC-MAP-003 / ADR-sdk-0005).

        Stations with their placement on the edge graph. Use ``list_stations`` when only id and
        name are needed.
        """
        operation = f"mapInfo/stations/{map_id}"
        client = self._session.create_generated_imap_client()
        request = (
            client.api.imap.v1.map_info.stations.by_map_id(map_id).to_get_request_information()
        )
        result = await riot_wire.read_async(
            self._session.request_adapter, request, operation
        )

        return riot_wire.map_array(
            result,
            operation,
            lambda station: MapStationDetail(
                map_id=map_id,
                station_id=riot_wire.require_int(station, operation, "id"),
                name=riot_wire.require_str(station, operation, "name"),
                edge_id=riot_wire.require_int(station, operation, "edge_id", "edgeId"),
                pos_x=riot_wire.require_float(station, operation, "pos.x"),
                pos_y=riot_wire.require_float(station, operation, "pos.y"),
                pos_yaw=riot_wire.optional_float(station, 0.0, "pos.yaw"),
                station_offset=riot_wire.optional_int(
                    station, 0, "station_offset", "stationOffset"
                ),
                type=riot_wire.optional_int(station, 0, "type"),
                description=riot_wire.optional_str(station, "desc") or "",
            ),
        )

    async def list_removed_edges(self, map_id: int) -> list[RemovedEdge]:
        """GET /api/imap/v1/mapResource/removedEdge/{mapId} (BC-MAP-003 / ADR-sdk-0005).

        Edges currently removed from the Map — the runtime half of its route graph. An empty
        list means "nothing removed" and is the normal case; map25 returned exactly that in
        Round 43. An element whose edge id cannot be read raises rather than being skipped: a
        dropped removal would leave the caller routing over an edge RIoT has taken out.
        """
        operation = f"mapResource/removedEdge/{map_id}"
        client = self._session.create_generated_imap_client()
        request = (
            client.api.imap.v1.map_resource.removed_edge.by_map_id(map_id)
            .to_get_request_information()
        )
        result = await riot_wire.read_async(
            self._session.request_adapter, request, operation
        )

        return riot_wire.map_array(
            result,
            operation,
            lambda removed: RemovedEdge(
                id=riot_wire.optional_int(removed, 0, "id"),
                map_id=riot_wire.optional_int(removed, map_id, "mapId", "map_id"),
                edge_id=riot_wire.require_int(removed, operation, "edgeId", "edge_id"),
            ),
        )

    async def list_removed_stations(self, map_id: int) -> list[RemovedStation]:
        """GET /api/imap/v1/mapResource/removedStation/{mapId} (BC-MAP-003 / ADR-sdk-0005).

        Stations currently removed from the Map. Same fail-closed reading as
        ``list_removed_edges``.
        """
        operation = f"mapResource/removedStation/{map_id}"
        client = self._session.create_generated_imap_client()
        request = (
            client.api.imap.v1.map_resource.removed_station.by_map_id(map_id)
            .to_get_request_information()
        )
        result = await riot_wire.read_async(
            self._session.request_adapter, request, operation
        )

        return riot_wire.map_array(
            result,
            operation,
            lambda removed: RemovedStation(
                id=riot_wire.optional_int(removed, 0, "id"),
                map_id=riot_wire.optional_int(removed, map_id, "mapId", "map_id"),
                station_id=riot_wire.require_int(removed, operation, "stationId", "station_id"),
                station_name=riot_wire.optional_str(removed, "stationName", "station_name"),
            ),
        )

    async def list_edge_groups(self) -> list[MapEdgeGroup]:
        """GET /api/imap/v1/mapEdgeGroup/all (BC-MAP-003 / ADR-sdk-0005).

        Every Map's edge groups, flattened to one entry per member edge with the group name
        copied in. This endpoint covers all Maps, not one — filter by ``map_id``.
        """
        operation = "mapEdgeGroup/all"
        client = self._session.create_generated_imap_client()
        request = client.api.imap.v1.map_edge_group.all.to_get_request_information()
        result = await riot_wire.read_async(
            self._session.request_adapter, request, operation
        )

        return self._map_edge_groups(result, operation)

    @property
    def raw(self):
        """Underlying Kiota imap client for endpoints not yet wrapped."""
        return self._session.create_generated_imap_client()

    @staticmethod
    def _map_edge_groups(result: object, operation: str) -> list[MapEdgeGroup]:
        """Flatten the ``{"群组名": [ ... ]}`` result of mapEdgeGroup/all.

        The outer key is the group name and is not repeated inside the elements, so it is
        copied into each one.
        """
        if result is None:
            return []

        # Round 43 saw this result populated, as an object keyed by group name — but map25, the
        # 8005 working Map, has no groups at all, so what an empty one looks like was never
        # observed. An empty array is the other plausible spelling of "none" and can only mean
        # that; a populated array would be a shape nobody has seen, and fails closed below.
        if isinstance(result, list) and not result:
            return []

        if not isinstance(result, dict):
            raise RiotApiException(
                f"{operation} returned result of type {type(result).__name__}, "
                "expected an object keyed by group name."
            )

        groups: list[MapEdgeGroup] = []
        for group_name, members in result.items():
            groups.extend(
                riot_wire.map_array(
                    members,
                    operation,
                    lambda member, name=group_name: MapEdgeGroup(
                        group_name=name,
                        id=riot_wire.optional_int(member, 0, "id"),
                        map_id=riot_wire.require_int(member, operation, "mapId", "map_id"),
                        map_name=riot_wire.optional_str(member, "mapName", "map_name") or "",
                        edge_id=riot_wire.require_int(member, operation, "edgeId", "edge_id"),
                        type=riot_wire.optional_str(member, "type") or "",
                        is_deleted=riot_wire.optional_bool(
                            member, False, "isDelete", "is_delete"
                        ),
                        gmt_create=riot_wire.optional_str(member, "gmtCreate", "gmt_create"),
                        gmt_update=riot_wire.optional_str(member, "gmtUpdate", "gmt_update"),
                    ),
                )
            )

        return groups
