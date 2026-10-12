"""riot-sdk#4: the six reads that go through ``riot_wire.read_async`` time out and release their
response as the generated ``get()`` of the same endpoint does. Mirrors
``csharp/RIoT.Sdk.Tests/RiotWireTransportTests.cs``.

The C# side had both faults and was fixed in #4; this side never had them, because
``send_primitive_async(..., "bytes", ...)`` lets httpx read the whole body before it returns.
These cases pin that, against a real socket: httpx's mock transport neither stalls nor pools, so
it could not tell a hang or a leaked connection from a healthy read.
"""

from __future__ import annotations

import asyncio
from contextlib import asynccontextmanager

import httpx
import pytest

from riot_sdk import RiotOptions, RiotSession

_GUARD_SECONDS = 6.0

_READS = {
    "edges": (
        lambda s: s.maps.list_edges(25),
        lambda s: s.maps.raw.api.imap.v1.map_info.edges.by_map_id(25).get(),
    ),
    "stations": (
        lambda s: s.maps.list_station_details(25),
        lambda s: s.maps.raw.api.imap.v1.map_info.stations.by_map_id(25).get(),
    ),
    "removedEdge": (
        lambda s: s.maps.list_removed_edges(25),
        lambda s: s.maps.raw.api.imap.v1.map_resource.removed_edge.by_map_id(25).get(),
    ),
    "removedStation": (
        lambda s: s.maps.list_removed_stations(25),
        lambda s: s.maps.raw.api.imap.v1.map_resource.removed_station.by_map_id(25).get(),
    ),
    "mapEdgeGroup/all": (
        lambda s: s.maps.list_edge_groups(),
        lambda s: s.maps.raw.api.imap.v1.map_edge_group.all.get(),
    ),
    "route/": (
        lambda s: s.tasks.read_dynamic_route_cost_presence(),
        lambda s: s.tasks.raw.api.task.v1.route.empty_path_segment.get(),
    ),
}


@asynccontextmanager
async def _server(mode: str):
    """A one-request-at-a-time HTTP/1.1 server on loopback.

    ``stall`` sends headers and the first bytes of a JSON body, then nothing. A status code
    sends that status with a complete ``{}`` body and keeps the connection open.
    """

    async def handle(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        try:
            while True:
                await reader.readuntil(b"\r\n\r\n")
                if mode == "stall":
                    writer.write(
                        b"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                        b"Content-Length: 100\r\n\r\n{\"code\":\"0\",\"result\":"
                    )
                    await writer.drain()
                    await asyncio.sleep(3600)
                body = b"{}"
                writer.write(
                    f"HTTP/1.1 {mode} X\r\nContent-Type: application/json\r\n"
                    f"Content-Length: {len(body)}\r\n\r\n".encode()
                    + body
                )
                await writer.drain()
        except (asyncio.IncompleteReadError, ConnectionError, asyncio.CancelledError):
            pass
        finally:
            writer.close()

    server = await asyncio.start_server(handle, "127.0.0.1", 0)
    port = server.sockets[0].getsockname()[1]
    try:
        yield f"http://127.0.0.1:{port}"
    finally:
        server.close()


async def _raised(call) -> BaseException:
    try:
        await asyncio.wait_for(call, _GUARD_SECONDS)
    except asyncio.TimeoutError:
        pytest.fail(f"The read hung past the {_GUARD_SECONDS}-second guard.")
    except Exception as error:  # noqa: BLE001 - the type is what is compared
        return error
    pytest.fail("The read did not fail.")


def _idle_connections(session: RiotSession) -> list[bool]:
    pool = session._http_client._transport._pool
    return [connection.is_idle() for connection in pool.connections]


@pytest.mark.parametrize("read", list(_READS))
async def test_a_stalled_body_times_out_as_the_generated_read_does(read: str) -> None:
    facade_read, generated_read = _READS[read]
    async with _server("stall") as base_url:
        session = RiotSession(RiotOptions(base_url=base_url, call_api_key="k", timeout=1.0))
        try:
            generated = await _raised(generated_read(session))
            facade = await _raised(facade_read(session))
        finally:
            await session.aclose()

    assert isinstance(facade, httpx.ReadTimeout)
    assert type(facade) is type(generated)


@pytest.mark.parametrize("status", [404, 500, 503])
@pytest.mark.parametrize("read", list(_READS))
async def test_a_failed_response_is_released_to_the_pool(read: str, status: int) -> None:
    facade_read, generated_read = _READS[read]
    async with _server(str(status)) as base_url:
        session = RiotSession(RiotOptions(base_url=base_url, call_api_key="k", timeout=1.0))
        try:
            generated = await _raised(generated_read(session))
            facade = await _raised(facade_read(session))
            # Two reads on one keep-alive connection: the second could only reuse it because the
            # first gave it back, and it must be idle again now.
            idle = _idle_connections(session)
        finally:
            await session.aclose()

    assert type(facade) is type(generated)
    assert idle == [True]
