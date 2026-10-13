from __future__ import annotations

import httpx
import pytest

from riot_sdk import RiotOptions, RiotSession
from riot_sdk.core.exceptions import RiotApiException
from riot_sdk.core.order_ref import OrderRef

_ORDER_ID = "order-2079056586101358592"
_UPPER_ID = "riot-lab-I-20260720-120949"

# Known-good body from E1-order-success-detail-station2.body
_DETAIL_SUCCESS_BODY = (
    '{"code":"0","message":"成功","msgDetail":"","result":{"appointMapId":0,"appointStationId":0,'
    '"appointVehicleGroupId":0,"appointVehicleGroupName":"0",'
    '"appointVehicleKey":"BROKERX-aee2f93d717546cf9510c98c854fe83e","changeVehicle":0,'
    '"changeVehicleReason":"","changeVehicleRecord":"","createTime":"2026-07-20 12:11:32",'
    '"createdBy":"api-test","distance":0,"doneTime":"2026-07-20 12:11:40",'
    '"endStationName":"站点2","endStationNo":2,"eta":0,"executeTime":"2026-07-20 12:11:36",'
    '"executeVehicleKey":"BROKERX-aee2f93d717546cf9510c98c854fe83e",'
    '"executeVehicleName":"新基测试300c协作1","executingIndex":-1,"failReason":"",'
    '"finalState":true,"id":488003,"isAppointEnable":1,"isDeleted":0,"lockStatus":0,'
    '"missions":[{"actionId":0,"actionName":"","actionParam1":0,"actionParam2":0,'
    '"actionParamStr":"","createTime":"2026-07-20 12:11:35","createType":0,"destination":2,'
    '"destinationName":"站点2","executeTime":"2026-07-20 12:11:36","executingIndex":0,'
    '"failStrategy":"FAIL_STRATEGY_VOID","failValue":"0","finishTime":"2026-07-20 12:11:40",'
    '"functionKey":"","id":7416323,"isDeleted":0,"length":0,"mapId":29,"mapName":"api测试",'
    '"missionState":2,"orderId":488003,"orderUuid":"be8cb2d2-c50c-4b36-b04b-fb034a2ca3fb",'
    '"resultCode":900,"resultStr":"订单完成","speed":0.0,"successStrategy":"SUCCESS_STRATEGY_VOID",'
    '"type":"move","updateTime":"2026-07-20 12:11:40","width":0}],"modifiedBy":"internal",'
    '"orderId":"order-2079056586101358592","orderName":"riot-lab-I-20260720-120949",'
    '"orderState":5,"orderType":1,"priority":0,"priorityQueue":0,"progress":100,"source":"FMS",'
    '"startEndStationNameDetail":"api测试 站点2 > api测试 站点2","startStationName":"站点2",'
    '"startStationNo":2,"taskState":"1","totalCosts":0,"updateTime":"2026-07-20 12:11:40",'
    '"upperId":"riot-lab-I-20260720-120949","userId":0},"tid":""}'
)


@pytest.mark.asyncio
async def test_get_order_by_order_id_returns_order_identifiers() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            text=_DETAIL_SUCCESS_BODY,
            headers={"Content-Type": "application/json"},
        )

    transport = httpx.MockTransport(handler)
    async with httpx.AsyncClient(
        transport=transport,
        base_url="http://riot.test/",
    ) as client:
        options = RiotOptions(
            base_url="http://riot.test",
            call_api_key="test-call-api-key",
        )
        async with RiotSession(options, client=client) as session:
            order = await session.order.get_order_by_order_id(_ORDER_ID)

    assert order.id == 488003
    assert order.order_id == _ORDER_ID
    assert order.upper_id == _UPPER_ID
    assert order.order_state == 5


# control-server#617: an order dispatched by hand on the RIoT web page, read back on site on
# 2026-10-11 15:23:21 (control-server evidence/field/20261011-W3-S1-charging/
# 13-foreign-order-readback.txt): HTTP 200, code 0, a full result, orderState 2, upperId an
# empty string. The fields that record names are its own; the rest is the lab body's shape.
_WEB_PAGE_ORDER_ID = "order-2109181133127286784"
_WEB_PAGE_ORDER_BODY = (
    '{"code":"0","message":"成功","msgDetail":"","result":{"appointVehicleKey":"",'
    '"createTime":"2026-10-11 15:15:43","createdBy":"admin","doneTime":"2026-10-11 15:15:48",'
    '"executeVehicleKey":"BROKERX-0000000000000000000000000000a002","finalState":true,'
    '"id":1757698,"isAppointEnable":0,"isDeleted":0,"lockStatus":0,"missions":[],'
    '"orderId":"order-2109181133127286784","orderName":"iiii","orderState":2,"orderType":1,'
    '"source":"WEB","upperId":""},"tid":""}'
)


def _session(body: str, paths: list[str] | None = None):
    def handler(request: httpx.Request) -> httpx.Response:
        if paths is not None:
            paths.append(request.url.path)
        return httpx.Response(200, text=body, headers={"Content-Type": "application/json"})

    client = httpx.AsyncClient(transport=httpx.MockTransport(handler), base_url="http://riot.test/")
    options = RiotOptions(base_url="http://riot.test", call_api_key="test-call-api-key")
    return client, RiotSession(options, client=client)


@pytest.mark.asyncio
async def test_get_order_by_order_id_reads_an_order_without_an_upper_id() -> None:
    paths: list[str] = []
    client, session = _session(_WEB_PAGE_ORDER_BODY, paths)
    async with client, session:
        order = await session.order.get_order_by_order_id(_WEB_PAGE_ORDER_ID)

    assert order == OrderRef(id=1757698, order_id=_WEB_PAGE_ORDER_ID, upper_id="", order_state=2)
    assert paths == ["/api/order/v1/orderRecord/detailByOrderId/" + _WEB_PAGE_ORDER_ID]


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "body",
    [
        '{"code":"0","message":"成功","result":{"id":7,"orderId":"order-7","upperId":null,"orderState":5}}',
        '{"code":"0","message":"成功","result":{"id":7,"orderId":"order-7","orderState":5}}',
    ],
    ids=["null", "missing"],
)
async def test_get_order_by_order_id_reads_a_null_or_missing_upper_id_as_empty(body: str) -> None:
    client, session = _session(body)
    async with client, session:
        order = await session.order.get_order_by_order_id("order-7")

    assert order == OrderRef(id=7, order_id="order-7", upper_id="", order_state=5)


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "body",
    [
        '{"code":"0","message":"成功","msgDetail":"","tid":""}',
        '{"code":"0","message":"成功","msgDetail":"","result":null,"tid":""}',
        '{"code":"0","message":"成功","result":{"orderId":"order-1","upperId":"","orderState":2}}',
        '{"code":"0","message":"成功","result":{"id":1,"orderId":"","upperId":"","orderState":2}}',
        '{"code":"0","message":"成功","result":{"id":1,"orderId":"order-1","upperId":""}}',
    ],
    ids=["no-result", "null-result", "no-id", "no-order-id", "no-order-state"],
)
async def test_get_order_by_order_id_still_refuses_an_answer_without_identity_or_state(body: str) -> None:
    client, session = _session(body)
    async with client, session:
        with pytest.raises(RiotApiException) as error:
            await session.order.get_order_by_order_id("order-1")

    assert error.value.business_code == "order-ref-missing"


@pytest.mark.asyncio
async def test_get_order_by_upper_id_still_refuses_an_answer_without_an_upper_id() -> None:
    client, session = _session(_WEB_PAGE_ORDER_BODY)
    async with client, session:
        with pytest.raises(RiotApiException) as error:
            await session.order.get_order_by_upper_id("UPPER-1")

    assert error.value.business_code == "order-ref-missing"
