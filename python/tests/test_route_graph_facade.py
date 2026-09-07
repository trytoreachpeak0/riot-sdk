"""Seam: the five imap route-graph endpoints and the custom deserialization they need
(BC-MAP-003 / ADR-sdk-0005). Mirrors ``csharp/RIoT.Sdk.Tests/RouteGraphFacadeTests.cs``.

Fixtures are verbatim response bodies from Round 43 against the production RIoT
(``172.19.206.222:8888``, mapId 25), trimmed to one or two elements. They are the reason this
facade exists: the generated Kiota models read ``snode``, the wire says ``s_node``.
"""

from __future__ import annotations

from contextlib import asynccontextmanager

import httpx
import pytest

from riot_sdk import DynamicRouteCostPresence, RiotApiException, RiotOptions, RiotSession

# runs/003-edges-map-25.json, first edge, every key as it came off the wire.
_EDGES_BODY = """{"code":"0","message":"成功","msgDetail":"","result":[
  {"cost":15450.0,"cx":0,"cy":0,"desc":"","direction":1,"dx":0,"dy":0,
   "e_facing":1570.7963267948965,"e_node":1,"ex":-18410,"ey":40340,"id":1,
   "is_back_edge":false,"limit_v":0,"limit_w":0,"param":255,"radius":0,
   "robot_direction":1,"rotate_direction":1,"s_facing":1570.7963267948965,
   "s_node":2,"sx":-18410,"sy":24890,"type":1,"user_define_properties":{}}
],"tid":""}"""

# runs/004-stations-map-25.json, the two stations that share edge 187 (Round 43 §2).
_STATIONS_BODY = """{"code":"0","message":"成功","msgDetail":"","result":[
  {"check_pos.x":0,"check_pos.y":0,"check_pos.yaw":0,"desc":"","dmcode_id":"",
   "edge_id":187,"enter_backward":false,"enter_pos.x":0,"enter_pos.y":0,
   "enter_pos.yaw":0,"exit_backward":false,"exit_pos.x":0,"exit_pos.y":0,
   "exit_pos.yaw":0,"id":1,"name":"N1-1","no_rotate":false,"param":0,
   "pgv_offset.x":0,"pgv_offset.y":0,"pgv_offset.yaw":0,
   "pos.x":760.0,"pos.y":27520.0,"pos.yaw":4731.3,"pos_dynamic":false,
   "station_offset":0,"type":1,"user_define_properties":{}},
  {"check_pos.x":0,"check_pos.y":0,"check_pos.yaw":0,"desc":"","dmcode_id":"",
   "edge_id":187,"enter_backward":false,"enter_pos.x":0,"enter_pos.y":0,
   "enter_pos.yaw":0,"exit_backward":false,"exit_pos.x":0,"exit_pos.y":0,
   "exit_pos.yaw":0,"id":2,"name":"N1-2","no_rotate":false,"param":0,
   "pgv_offset.x":0,"pgv_offset.y":0,"pgv_offset.yaw":0,
   "pos.x":730.0,"pos.y":29110.0,"pos.yaw":4725.0,"pos_dynamic":false,
   "station_offset":0,"type":1,"user_define_properties":{}}
],"tid":""}"""

# runs/005-removed-edge-map-25.json and 007 verbatim — map25 has nothing removed.
_EMPTY_RESULT_BODY = """{"code":"0","message":"成功","msgDetail":"","result":[],"tid":""}"""

# runs/008-edge-group-all.json, one member of one group. Note camelCase on this endpoint, and
# that map25 has no groups at all — every group on this RIoT belongs to another Map.
_EDGE_GROUPS_BODY = """{"code":"0","message":"成功","msgDetail":"","result":{
  "老厂电梯":[
    {"edgeId":8,"gmtCreate":"2024-06-26 10:57:58","gmtUpdate":"2024-06-26 10:57:58",
     "id":223,"isDelete":0,"mapId":14,"mapName":"新厂二楼","name":"老厂电梯",
     "type":"SINGLE_VEHICLE_ONLY"}
  ]
},"tid":""}"""


@asynccontextmanager
async def _session_over(body: str, urls: list[str] | None = None):
    def handler(request: httpx.Request) -> httpx.Response:
        if urls is not None:
            urls.append(str(request.url))
        return httpx.Response(
            200, text=body, headers={"Content-Type": "application/json"}
        )

    # No base_url on the transport: httpx would merge it with the absolute URL Kiota builds and
    # leave a doubled slash in the recorded URL. RiotSession pins the adapter's base_url itself,
    # so the host below comes from RiotOptions either way.
    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as client:
        options = RiotOptions(base_url="http://riot.test", call_api_key="test-call-api-key")
        async with RiotSession(options, client=client) as session:
            yield session


@pytest.mark.asyncio
async def test_list_edges_reads_the_snake_case_compound_keys_the_generated_model_misses() -> None:
    async with _session_over(_EDGES_BODY) as session:
        edges = await session.maps.list_edges(map_id=25)

    assert len(edges) == 1
    edge = edges[0]
    assert edge.id == 1
    # s_node/e_node: the generated Edge model reads snode/enode and would leave these None.
    assert (edge.start_node, edge.end_node) == (2, 1)
    assert edge.cost_mm == 15450.0
    assert (edge.start_x, edge.start_y) == (-18410, 24890)
    assert (edge.end_x, edge.end_y) == (-18410, 40340)
    assert edge.start_facing == 1570.7963267948965
    # is_back_edge, not isBackEdge.
    assert edge.is_back_edge is False
    assert edge.direction == 1
    assert edge.description == ""


@pytest.mark.asyncio
async def test_list_edges_also_accepts_the_openapi_spelling_of_the_node_keys() -> None:
    body = """{"code":"0","result":[
      {"id":1,"snode":2,"enode":1,"cost":15450.0,
       "sx":-18410,"sy":24890,"ex":-18410,"ey":40340}
    ]}"""
    async with _session_over(body) as session:
        edges = await session.maps.list_edges(map_id=25)

    assert (edges[0].start_node, edges[0].end_node) == (2, 1)


@pytest.mark.asyncio
async def test_list_edges_fails_closed_when_no_spelling_of_the_start_node_is_present() -> None:
    body = """{"code":"0","result":[
      {"id":1,"e_node":1,"cost":15450.0,"sx":-18410,"sy":24890,"ex":-18410,"ey":40340}
    ]}"""
    async with _session_over(body) as session:
        with pytest.raises(RiotApiException, match="s_node"):
            await session.maps.list_edges(map_id=25)


@pytest.mark.asyncio
async def test_list_station_details_reads_the_keys_that_literally_contain_a_dot() -> None:
    async with _session_over(_STATIONS_BODY) as session:
        stations = await session.maps.list_station_details(map_id=25)

    assert len(stations) == 2
    first = stations[0]
    assert (first.map_id, first.station_id, first.name) == (25, 1, "N1-1")
    # pos.x / pos.y / pos.yaw are property names containing a dot, not a nested object.
    assert (first.pos_x, first.pos_y, first.pos_yaw) == (760.0, 27520.0, 4731.3)
    assert first.station_offset == 0


@pytest.mark.asyncio
async def test_list_station_details_carries_the_edge_id_that_places_a_station_on_a_node() -> None:
    async with _session_over(_STATIONS_BODY) as session:
        stations = await session.maps.list_station_details(map_id=25)

    # Both stations sit on edge 187, at opposite ends — placement is decided downstream by
    # comparing their coordinates against that edge's endpoints, so the facade must carry
    # edge_id and pos through intact.
    assert all(s.edge_id == 187 for s in stations)
    assert stations[0].pos_y != stations[1].pos_y


@pytest.mark.asyncio
async def test_list_station_details_fails_closed_when_the_dotted_position_is_absent() -> None:
    body = """{"code":"0","result":[{"id":1,"name":"N1-1","edge_id":187}]}"""
    async with _session_over(body) as session:
        with pytest.raises(RiotApiException, match=r"pos\.x"):
            await session.maps.list_station_details(map_id=25)


@pytest.mark.asyncio
async def test_list_removed_edges_returns_empty_for_a_map_with_nothing_removed() -> None:
    async with _session_over(_EMPTY_RESULT_BODY) as session:
        assert await session.maps.list_removed_edges(map_id=25) == []


@pytest.mark.asyncio
async def test_list_removed_edges_reads_the_shape_the_openapi_description_declares() -> None:
    body = """{"code":"0","result":[
      {"edgeId":187,"gmtCreate":"2024-06-26 10:57:58","id":223,"mapId":25}
    ]}"""
    async with _session_over(body) as session:
        removed = await session.maps.list_removed_edges(map_id=25)

    assert len(removed) == 1
    assert (removed[0].edge_id, removed[0].id, removed[0].map_id) == (187, 223, 25)


@pytest.mark.asyncio
async def test_list_removed_edges_fails_closed_rather_than_dropping_an_unreadable_removal() -> None:
    # A removal whose edge id cannot be read must raise: silently skipping it would leave the
    # caller routing over an edge RIoT has taken out of the graph.
    body = """{"code":"0","result":[{"id":223,"mapId":25}]}"""
    async with _session_over(body) as session:
        with pytest.raises(RiotApiException, match="edgeId"):
            await session.maps.list_removed_edges(map_id=25)


@pytest.mark.asyncio
async def test_list_removed_stations_reads_the_shape_the_openapi_description_declares() -> None:
    body = """{"code":"0","result":[
      {"id":7,"mapId":25,"stationId":170,"stationName":"N9-3"}
    ]}"""
    async with _session_over(body) as session:
        removed = await session.maps.list_removed_stations(map_id=25)

    assert (removed[0].station_id, removed[0].station_name) == (170, "N9-3")


@pytest.mark.asyncio
async def test_list_edge_groups_flattens_the_group_name_keyed_map_and_reads_camel_case() -> None:
    async with _session_over(_EDGE_GROUPS_BODY) as session:
        groups = await session.maps.list_edge_groups()

    assert len(groups) == 1
    group = groups[0]
    # The group name is only the outer key of the response map; it is copied into each entry.
    assert group.group_name == "老厂电梯"
    assert (group.edge_id, group.map_id, group.map_name) == (8, 14, "新厂二楼")
    assert group.type == "SINGLE_VEHICLE_ONLY"
    # isDelete is 0/1 on the wire, not a JSON bool.
    assert group.is_deleted is False
    # "2024-06-26 10:57:58" is not ISO 8601; parsing it is the consumer's call, and the
    # generated model's get_datetime_value would have failed on it.
    assert group.gmt_update == "2024-06-26 10:57:58"


@pytest.mark.asyncio
async def test_list_edge_groups_treats_an_empty_array_as_no_groups() -> None:
    # map25 — the 8005 working Map — has no edge groups at all, and Round 43 never saw this
    # endpoint return an empty result, so both spellings of "none" have to be accepted.
    async with _session_over(_EMPTY_RESULT_BODY) as session:
        assert await session.maps.list_edge_groups() == []

    async with _session_over("""{"code":"0","result":{},"tid":""}""") as session:
        assert await session.maps.list_edge_groups() == []


@pytest.mark.asyncio
async def test_list_edge_groups_fails_closed_on_a_populated_shape_nobody_has_seen() -> None:
    async with _session_over("""{"code":"0","result":[{"edgeId":8}]}""") as session:
        with pytest.raises(RiotApiException, match="expected an object keyed by group name"):
            await session.maps.list_edge_groups()


@pytest.mark.asyncio
async def test_a_business_failure_code_raises_instead_of_returning_an_empty_graph() -> None:
    async with _session_over("""{"code":"00002","message":"失败","result":null}""") as session:
        with pytest.raises(RiotApiException) as raised:
            await session.maps.list_edges(map_id=25)

    assert raised.value.business_code == "00002"


@pytest.mark.asyncio
async def test_dynamic_route_cost_reports_absence_for_the_empty_object_every_observation_saw() -> None:
    # Round 15 on the test RCS and Round 43 on the production one both answered exactly this.
    async with _session_over('{"code":"0","message":"成功","result":{},"tid":""}') as session:
        presence = await session.tasks.read_dynamic_route_cost_presence()

    assert presence == DynamicRouteCostPresence(present=False, entry_count=0)


@pytest.mark.asyncio
async def test_dynamic_route_cost_counts_entries_without_inventing_a_shape() -> None:
    # Nobody has seen this populated, so the SDK counts entries rather than deserializing into a
    # type it made up. Both plausible spellings are counted the same way.
    async with _session_over('{"code":"0","result":{"12":1500.0,"25":900.0}}') as session:
        from_object = await session.tasks.read_dynamic_route_cost_presence()
    assert from_object == DynamicRouteCostPresence(present=True, entry_count=2)

    async with _session_over('{"code":"0","result":[{"edgeId":1},{"edgeId":2}]}') as session:
        from_array = await session.tasks.read_dynamic_route_cost_presence()
    assert from_array == DynamicRouteCostPresence(present=True, entry_count=2)


@pytest.mark.asyncio
async def test_each_method_calls_the_endpoint_the_whitelist_approved() -> None:
    # These five methods route by hand off the generated request builders rather than through a
    # generated model, so nothing else would notice a wrong URL: the fixed-body transport
    # answers any path. REQ-0146 approved exactly these five and no others.
    urls: list[str] = []
    async with _session_over(_EMPTY_RESULT_BODY, urls) as session:
        await session.maps.list_edges(map_id=25)
        await session.maps.list_station_details(map_id=25)
        await session.maps.list_removed_edges(map_id=25)
        await session.maps.list_removed_stations(map_id=25)
        await session.maps.list_edge_groups()
        # In REQ-0146's named list since before CP-0001; the engine reads it for presence only.
        await session.tasks.read_dynamic_route_cost_presence()

    assert urls == [
        "http://riot.test/api/imap/v1/mapInfo/edges/25",
        "http://riot.test/api/imap/v1/mapInfo/stations/25",
        "http://riot.test/api/imap/v1/mapResource/removedEdge/25",
        "http://riot.test/api/imap/v1/mapResource/removedStation/25",
        "http://riot.test/api/imap/v1/mapEdgeGroup/all",
        "http://riot.test/api/task/v1/route/",
    ]
