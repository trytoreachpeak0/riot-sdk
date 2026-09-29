from __future__ import annotations

import json

import httpx
import pytest

from riot_sdk import OrderLookupStatus, OrderMissionAction, RiotApiException, RiotOptions, RiotSession

_DEVICE_KEY = "BROKERX-aee2f93d717546cf9510c98c854fe83e"

_CREATE_SUCCESS_BODY = (
    '{"code":"0","message":"成功","result":{"id":488650,'
    '"orderId":"order-2079374239101747200","upperId":"UPPER-CHARGE","orderState":1}}'
)

# The single-segment request body the SDK sent before action_after_move existed.
# Pinned: the charging form must not change it.
_SINGLE_SEGMENT_REQUEST = {
    "appointVehicleKey": _DEVICE_KEY,
    "isAppointEnable": 1,
    "lockStatus": 0,
    "mission": [{"destination": 211, "mapId": 26, "type": "move"}],
    "orderName": "UPPER-CHARGE",
    "upperId": "UPPER-CHARGE",
}

# Trimmed from program repo rcs/riot-behavior-lab/evidence/rounds/2026-07-21-round-24/
# runs/S1b-hang-detail.json: move(6)+act(78,1,0) expanded to move(8) -> move(6) -> act,
# the act failed with 407802 and the order hung (9).
_ROUND24_HANG_DETAIL_BODY = (
    '{"code":"0","message":"成功","result":{"id":488650,'
    '"orderId":"order-2079374239101747200",'
    '"upperId":"riot-behavior-lab-R24-fail-m6-20260721-091202","orderState":9,'
    '"appointVehicleKey":"BROKERX-aee2f93d717546cf9510c98c854fe83e",'
    '"executeVehicleKey":"BROKERX-aee2f93d717546cf9510c98c854fe83e","endStationNo":6,'
    '"missions":['
    '{"actionId":0,"actionParam1":0,"actionParam2":0,"destination":8,"mapId":30,'
    '"resultCode":900,"resultStr":"订单完成","type":"move"},'
    '{"actionId":0,"actionParam1":0,"actionParam2":0,"destination":6,"mapId":30,'
    '"resultCode":900,"resultStr":"订单完成","type":"move"},'
    '{"actionId":78,"actionName":"charge ;action: 78;  1;  0;  null","actionParam1":1,'
    '"actionParam2":0,"destination":0,"mapId":0,"resultCode":407802,'
    '"resultStr":"未知类型错误,导致订单挂起:错误编码为:407802","type":"act"}]}}'
)


async def _create(response_body: str = _CREATE_SUCCESS_BODY, **kwargs):
    requests: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        requests.append(request)
        return httpx.Response(
            200,
            text=response_body,
            headers={"Content-Type": "application/json"},
        )

    async with httpx.AsyncClient(
        transport=httpx.MockTransport(handler),
        base_url="http://riot.test/",
    ) as client:
        options = RiotOptions(base_url="http://riot.test", call_api_key="test-call-api-key")
        async with RiotSession(options, client=client) as session:
            try:
                result = await session.order.create_move_order(**kwargs)
            except Exception as error:  # noqa: BLE001 - returned for assertion
                result = error
    return result, requests


async def _find(upper_id: str, body: str):
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, text=body, headers={"Content-Type": "application/json"})

    async with httpx.AsyncClient(
        transport=httpx.MockTransport(handler),
        base_url="http://riot.test/",
    ) as client:
        options = RiotOptions(base_url="http://riot.test", call_api_key="test-call-api-key")
        async with RiotSession(options, client=client) as session:
            return await session.order.find_order_by_upper_id(upper_id)


@pytest.mark.asyncio
async def test_create_move_order_with_action_sends_move_then_act_with_same_other_fields() -> None:
    order, requests = await _create(
        upper_id="UPPER-CHARGE",
        appoint_vehicle_key=_DEVICE_KEY,
        map_id=26,
        destination_station_id=211,
        action_after_move=OrderMissionAction(78, 1, 0),
    )

    assert order.id == 488650
    assert len(requests) == 1
    assert requests[0].method == "POST"
    assert requests[0].url.path == "/api/order/v1/add/byDefaultMissions"
    body = json.loads(requests[0].content)
    assert body == {
        "appointVehicleKey": _DEVICE_KEY,
        "isAppointEnable": 1,
        "lockStatus": 0,
        "mission": [
            {"destination": 211, "mapId": 26, "type": "move"},
            {"actionId": 78, "actionParam1": 1, "actionParam2": 0, "type": "act"},
        ],
        "orderName": "UPPER-CHARGE",
        "upperId": "UPPER-CHARGE",
    }


@pytest.mark.asyncio
async def test_create_move_order_without_action_keeps_the_single_segment_body() -> None:
    _, requests = await _create(
        upper_id="UPPER-CHARGE",
        appoint_vehicle_key=_DEVICE_KEY,
        map_id=26,
        destination_station_id=211,
    )

    assert len(requests) == 1
    assert json.loads(requests[0].content) == _SINGLE_SEGMENT_REQUEST


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("map_id", "destination_station_id"),
    [(0, 211), (26, 0), (-1, 211), (26, -1)],
)
async def test_create_move_order_with_action_rejects_non_positive_map_or_destination_without_http(
    map_id: int,
    destination_station_id: int,
) -> None:
    error, requests = await _create(
        upper_id="UPPER-INVALID",
        appoint_vehicle_key=_DEVICE_KEY,
        map_id=map_id,
        destination_station_id=destination_station_id,
        action_after_move=OrderMissionAction(78, 1, 0),
    )

    assert isinstance(error, ValueError)
    assert requests == []


@pytest.mark.asyncio
async def test_create_move_order_with_wrong_action_type_is_rejected_without_http() -> None:
    error, requests = await _create(
        upper_id="UPPER-BAD-ACTION",
        appoint_vehicle_key=_DEVICE_KEY,
        map_id=26,
        destination_station_id=211,
        action_after_move=(78, 1, 0),
    )

    assert isinstance(error, TypeError)
    assert requests == []


@pytest.mark.parametrize(
    ("action_id", "param1", "param2"),
    [(0, 1, 0), (-78, 1, 0), (78, -1, 0), (78, 1, -1)],
)
def test_order_mission_action_rejects_invalid_values(action_id: int, param1: int, param2: int) -> None:
    with pytest.raises(ValueError):
        OrderMissionAction(action_id, param1, param2)


@pytest.mark.asyncio
async def test_create_move_order_with_action_business_failure_raises_without_retry() -> None:
    error, requests = await _create(
        '{"code":"0610008","message":"upperId exists","result":null}',
        upper_id="UPPER-EXISTS",
        appoint_vehicle_key=_DEVICE_KEY,
        map_id=26,
        destination_station_id=211,
        action_after_move=OrderMissionAction(78, 1, 0),
    )

    assert isinstance(error, RiotApiException)
    assert error.business_code == "0610008"
    assert len(requests) == 1


@pytest.mark.asyncio
async def test_find_order_by_upper_id_reads_back_act_mission_facts_from_round24_hang() -> None:
    result = await _find("riot-behavior-lab-R24-fail-m6-20260721-091202", _ROUND24_HANG_DETAIL_BODY)

    assert result.status is OrderLookupStatus.Found
    order = result.order
    assert order is not None
    assert order.order_state == 9
    assert order.end_station_no == 6
    assert [mission.type for mission in order.missions] == ["move", "move", "act"]

    act = order.missions[2]
    assert (act.action_id, act.action_param1, act.action_param2, act.result_code) == (78, 1, 0, 407802)
    assert (act.map_id, act.destination) == (0, 0)

    last_move = order.missions[1]
    assert last_move.destination == 6
    assert last_move.result_code == 900
    assert last_move.action_id == 0


@pytest.mark.asyncio
async def test_find_order_by_upper_id_reads_missing_act_facts_as_none_not_zero() -> None:
    body = (
        '{"code":"0","result":{"id":7,"orderId":"ORDER-7","upperId":"UPPER-7","orderState":3,'
        '"endStationNo":211,"missions":[{"type":"move","mapId":26,"destination":211},'
        '{"type":"act","actionId":78,"resultCode":null}]}}'
    )
    result = await _find("UPPER-7", body)

    order = result.order
    assert order is not None
    move, act = order.missions
    assert (move.action_id, move.action_param1, move.action_param2, move.result_code) == (
        None,
        None,
        None,
        None,
    )
    assert act.action_id == 78
    assert (act.action_param1, act.action_param2, act.result_code) == (None, None, None)
    assert (act.map_id, act.destination) == (None, None)
