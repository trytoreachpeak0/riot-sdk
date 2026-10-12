from __future__ import annotations

import httpx
import pytest

from riot_sdk import RiotApiException, RiotOptions, RiotSession


async def _read(path: str, body: str, operation):
    requests: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        requests.append(request)
        assert request.url.path == path
        return httpx.Response(
            200,
            text=body,
            headers={"Content-Type": "application/json"},
        )

    async with httpx.AsyncClient(
        transport=httpx.MockTransport(handler),
        base_url="http://riot.test/",
    ) as client:
        async with RiotSession(
            RiotOptions(base_url="http://riot.test", call_api_key="test-key"),
            client=client,
        ) as session:
            result = await operation(session)
    return result, requests


@pytest.mark.asyncio
async def test_get_vehicle_card_returns_exact_facts_for_matching_device_key() -> None:
    body = (
        '{"code":"0","result":{"deviceKey":"VEHICLE-1","enable":true,"status":1,'
        '"procState":"IDLE","currentMap":"MAP-25","currentPosition":12,"battery":80,'
        '"batteryState":"NO_CHARGE","speed":0,"lockStatus":0,"orderTaskId":null}}'
    )
    card, requests = await _read(
        "/api/task/vehicles/getVehicleInfoByDeviceKey",
        body,
        lambda session: session.tasks.get_vehicle_card("VEHICLE-1"),
    )

    assert requests[0].url.params["key"] == "VEHICLE-1"
    assert card.device_key == "VEHICLE-1"
    assert card.enable is True
    assert card.status == 1
    assert card.proc_state == "IDLE"
    assert card.current_map == "MAP-25"
    assert card.current_position == 12
    assert card.battery_percent == 80
    assert card.battery_state == "NO_CHARGE"
    assert card.speed == 0
    assert card.lock_status == 0
    assert card.order_task_id is None
    assert len(requests) == 1


@pytest.mark.asyncio
async def test_get_vehicle_card_rejects_mismatched_device_key() -> None:
    with pytest.raises(RiotApiException) as caught:
        await _read(
            "/api/task/vehicles/getVehicleInfoByDeviceKey",
            '{"code":"0","result":{"deviceKey":"OTHER","enable":true,"status":1}}',
            lambda session: session.tasks.get_vehicle_card("VEHICLE-1"),
        )
    assert caught.value.business_code == "vehicle-key-mismatch"


@pytest.mark.asyncio
async def test_get_vehicle_card_rejects_missing_result() -> None:
    with pytest.raises(RiotApiException) as caught:
        await _read(
            "/api/task/vehicles/getVehicleInfoByDeviceKey",
            '{"code":"0","result":null}',
            lambda session: session.tasks.get_vehicle_card("VEHICLE-1"),
        )
    assert caught.value.business_code == "vehicle-card-missing"


@pytest.mark.asyncio
async def test_get_vehicle_execution_facts_returns_vehicle_and_task_facts() -> None:
    body = """
    {
      "vehicle":{"movementState":"MT_FINISHED","controlState":"CONTROL_STATE_OK",
        "emergencyState":"OK","breakSwitchState":"MOVABLE",
        "locationState":"LOCATION_STATE_RUNNING","speed":0,"mode":"MODE_MANUAL"},
      "vehicleTaskInfo":{"key":"VEHICLE-1","procState":"IDLE",
        "processingOrder":false,"enable":true,"integrationLevel":"ON_LINE"}
    }
    """
    facts, requests = await _read(
        "/api/task/v1/task/getVehicleInfo/VEHICLE-1",
        body,
        lambda session: session.tasks.get_vehicle_execution_facts("VEHICLE-1"),
    )

    assert facts.device_key == "VEHICLE-1"
    assert facts.movement_state == "MT_FINISHED"
    assert facts.control_state == "CONTROL_STATE_OK"
    assert facts.emergency_state == "OK"
    assert facts.break_switch_state == "MOVABLE"
    assert facts.location_state == "LOCATION_STATE_RUNNING"
    assert facts.speed == 0
    assert facts.proc_state == "IDLE"
    assert facts.processing_order is False
    assert facts.enable is True
    assert facts.integration_level == "ON_LINE"
    assert facts.mode == "MODE_MANUAL"
    assert len(requests) == 1


# Behaviour lab Round 6 B2-getVehicleInfo-full.json, trimmed (identity replaced, the large nested objects dropped).
ROUND6_VEHICLE_INFO = (
    '{"vehicle":{"actionState":"AT_NA","actionTaskNo":0,"agvInfoState":"NORMAL","aliveState":true,"battery":35,"batteryState":"NO_CHARGE","breakSwitchState":"MOVABLE","confidence":92,"connected":true,"connectedTimestamp":1784507855064,"controlState":"CONTROL_STATE_OK","curCheckPointNo":0,"curPathNo":0,"currentNode":1,"currentStation":1,"emergencyState":"OK","faultCodes":"11322,11324,212416,211416","faultCodesList":[],"firstReportData":true,"fleetMode":"FLEET_MODE_OFFLINE","freshState":1,"hardWareErrorCode":0,"hardwareState":"H_STATE_OK","key":"VEHICLE-1","lastErrorCode":0,"lastReportTime":1784514379092,"length":925,"loadHandlingDevices":[],"loadState":0,"locationState":"LOCATION_STATE_RUNNING","mode":"MODE_AUTO","motorExceptionState":"NORMAL","moveTaskNo":2,"movementState":"MT_FINISHED","noNode":false,"noStation":false,"orientationAngle":-1.578,"originalLength":925,"originalWidth":622,"paused":false,"powerMode":"POWER_MODE_FULLER","precisePosition":{"x":128.0,"y":-539.0},"productKey":"standard.oasis.300ul","routingGroup":"EXECUTE_FREE","running":false,"speed":0.0,"startPauseTime":0,"state":"IDLE","status":1,"vehicleId":40,"width":622},"vehicleTaskInfo":{"batteryMaintenanceTime":1762328282203,"enable":false,"handlePauseTime":0,"integrationLevel":"OFF_LINE","integrationLevelTime":0,"key":"VEHICLE-1","lastExecuteOrderTimestamp":0,"procState":"IDLE","processingOrder":false,"routeProgressIndex":-1}}'
)


@pytest.mark.asyncio
async def test_get_vehicle_execution_facts_reads_mode_auto_off_a_real_response() -> None:
    # riot-sdk#3 review M1: control-server#639 proves standstill on MODE_AUTO, so it must come through as spelled.
    # Without the UTF-8 BOM the lab file carries: that came from writing the file, and Python's reader refuses it.
    facts, requests = await _read(
        "/api/task/v1/task/getVehicleInfo/VEHICLE-1",
        ROUND6_VEHICLE_INFO,
        lambda session: session.tasks.get_vehicle_execution_facts("VEHICLE-1"),
    )
    assert facts.mode == "MODE_AUTO"
    assert facts.movement_state == "MT_FINISHED"
    assert facts.break_switch_state == "MOVABLE"
    assert facts.speed == 0
    assert facts.integration_level == "OFF_LINE"
    assert len(requests) == 1


@pytest.mark.asyncio
@pytest.mark.parametrize("mode", ["MODE_AUTO", "MODE_MANUAL", "UNKNOWN"])
async def test_get_vehicle_execution_facts_passes_each_declared_mode_through(mode: str) -> None:
    body = (
        '{"vehicle":{"movementState":"MT_NA","mode":"' + mode + '"},'
        '"vehicleTaskInfo":{"key":"VEHICLE-1","procState":"IDLE"}}'
    )
    facts, _ = await _read(
        "/api/task/v1/task/getVehicleInfo/VEHICLE-1",
        body,
        lambda session: session.tasks.get_vehicle_execution_facts("VEHICLE-1"),
    )
    assert facts.mode == mode


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "mode",
    [
        '0',
        ' 0',
        'mode_auto',
        ' MODE_AUTO',
        'MODE_AUTO ',
        'Mode_Auto',
        '3',
        '-1',
        'MODE_AUTO,MODE_MANUAL',
        'mODE_AUTO',
        'mODE_MANUAL',
        'uNKNOWN',
        '',
    ],
)
async def test_get_vehicle_execution_facts_reads_a_mode_not_spelled_as_declared_as_none(mode: str) -> None:
    # riot-sdk#3 review S1: the same table as the C# test; both languages give None for every one of them.
    body = (
        '{"vehicle":{"movementState":"MT_NA","mode":"' + mode + '"},'
        '"vehicleTaskInfo":{"key":"VEHICLE-1","procState":"IDLE"}}'
    )
    facts, _ = await _read(
        "/api/task/v1/task/getVehicleInfo/VEHICLE-1",
        body,
        lambda session: session.tasks.get_vehicle_execution_facts("VEHICLE-1"),
    )
    assert facts.movement_state == "MT_NA"
    assert facts.mode is None


@pytest.mark.asyncio
@pytest.mark.parametrize("mode_json", ["0", "true", "{}", '["MODE_AUTO"]'])
async def test_get_vehicle_execution_facts_reads_a_mode_that_is_not_a_string_as_none(mode_json: str) -> None:
    # Same table as the C# test: a mode that is not a JSON string is no mode.
    body = (
        '{"vehicle":{"movementState":"MT_NA","mode":' + mode_json + "},"
        '"vehicleTaskInfo":{"key":"VEHICLE-1","procState":"IDLE"}}'
    )
    facts, _ = await _read(
        "/api/task/v1/task/getVehicleInfo/VEHICLE-1",
        body,
        lambda session: session.tasks.get_vehicle_execution_facts("VEHICLE-1"),
    )
    assert facts.movement_state == "MT_NA"
    assert facts.mode is None


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("mode_member", "expected"),
    [
        ("", None),
        (',"mode":null', None),
        (',"mode":"MODE_REMOTE"', None),
    ],
)
async def test_get_vehicle_execution_facts_reads_absent_or_unknown_mode(
    mode_member: str, expected: str | None
) -> None:
    # control-server#639: a consumer requires MODE_AUTO, so anything else must not read like a real mode.
    body = (
        '{"vehicle":{"movementState":"MT_NA"' + mode_member + "},"
        '"vehicleTaskInfo":{"key":"VEHICLE-1","procState":"IDLE"}}'
    )
    facts, _ = await _read(
        "/api/task/v1/task/getVehicleInfo/VEHICLE-1",
        body,
        lambda session: session.tasks.get_vehicle_execution_facts("VEHICLE-1"),
    )
    assert facts.movement_state == "MT_NA"
    assert facts.mode == expected


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "body",
    [
        '{"vehicleTaskInfo":{"key":"VEHICLE-1"}}',
        '{"vehicle":{"movementState":"MT_FINISHED"}}',
    ],
)
async def test_get_vehicle_execution_facts_rejects_missing_vehicle_or_task_half(
    body: str,
) -> None:
    with pytest.raises(RiotApiException) as caught:
        await _read(
            "/api/task/v1/task/getVehicleInfo/VEHICLE-1",
            body,
            lambda session: session.tasks.get_vehicle_execution_facts("VEHICLE-1"),
        )
    assert caught.value.business_code == "vehicle-execution-facts-missing"


@pytest.mark.asyncio
async def test_get_vehicle_execution_facts_rejects_mismatched_task_key() -> None:
    body = (
        '{"vehicle":{"movementState":"MT_FINISHED"},'
        '"vehicleTaskInfo":{"key":"OTHER","procState":"IDLE"}}'
    )
    with pytest.raises(RiotApiException) as caught:
        await _read(
            "/api/task/v1/task/getVehicleInfo/VEHICLE-1",
            body,
            lambda session: session.tasks.get_vehicle_execution_facts("VEHICLE-1"),
        )
    assert caught.value.business_code == "vehicle-key-mismatch"
