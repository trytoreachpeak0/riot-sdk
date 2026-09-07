"""Custom deserialization for the RIoT endpoints whose wire format no generated model can read.

Named for RIoT rather than for imap because the task module has one endpoint with the same
problem: ``GET /api/task/v1/route/`` answers with a shape nobody has ever seen populated, so the
SDK reports what is there rather than deserializing into a type it made up.


The generated Kiota models cannot read these responses. Round 43 measured why, on the
production RIoT: ``edges`` is snake_case and spells the compound keys differently from the
OpenAPI description (``s_node``, not ``snode``); ``stations`` spells keys with a literal dot
(``pos.x``); ``mapEdgeGroup/all`` is camelCase — a second style on the same service — nests its
result under group-name keys, and stamps timestamps that are not ISO 8601.

So these five endpoints reuse the generated layer for URL construction and auth (the
``RequestInformation`` straight off the generated request builders) and read the body here.
Everything in this module is internal to the SDK: the wire format must not reach product code.

This mirrors ``csharp/RIoT.Sdk.Facade/RiotWire.cs`` (ADR-sdk-0004: both languages carry the
same seam).
"""

from __future__ import annotations

import json
from typing import Any, Callable, TypeVar

from kiota_abstractions.request_information import RequestInformation

from riot_sdk.core.business_response import ensure_success
from riot_sdk.core.exceptions import RiotApiException

T = TypeVar("T")

_MISSING = object()


async def read_async(
    adapter: Any,
    request_info: RequestInformation,
    operation: str,
) -> Any:
    """Send a prepared GET and return its ``result`` after validating the envelope.

    Business failures raise ``RiotApiException`` (ADR-sdk-0005). A missing ``result`` comes
    back as ``None``, which every mapper below treats as "nothing here".
    """
    payload = await adapter.send_primitive_async(request_info, "bytes", None)
    if payload is None:
        raise RiotApiException(f"{operation} returned empty response.")

    envelope = json.loads(payload)
    if not isinstance(envelope, dict):
        raise RiotApiException(
            f"{operation} returned a {type(envelope).__name__} body, "
            "expected a ResponseMsg object."
        )

    ensure_success(envelope.get("code"), envelope.get("message"))
    return envelope.get("result")


def map_array(result: Any, operation: str, map_item: Callable[[dict], T]) -> list[T]:
    """Map a ``result`` array.

    A missing or null result is an empty list — RIoT returns ``"result":[]`` for "nothing
    here", and both spellings mean the same thing. A result that is present but not an array
    is a protocol violation and fails closed.
    """
    if result is None:
        return []

    if not isinstance(result, list):
        raise RiotApiException(
            f"{operation} returned result of type {type(result).__name__}, expected an array."
        )

    items: list[T] = []
    for item in result:
        if not isinstance(item, dict):
            raise RiotApiException(
                f"{operation} returned a {type(item).__name__} array element, "
                "expected an object."
            )
        items.append(map_item(item))

    return items


def require_int(element: dict, operation: str, *keys: str) -> int:
    """Read an int, trying each spelling in turn. Fails closed when none is present."""
    value = _first(element, keys, (int,))
    if value is _MISSING:
        raise _missing(operation, keys, "int")
    return int(value)  # type: ignore[arg-type]


def optional_int(element: dict, fallback: int, *keys: str) -> int:
    value = _first(element, keys, (int,))
    return fallback if value is _MISSING else int(value)  # type: ignore[arg-type]


def require_float(element: dict, operation: str, *keys: str) -> float:
    """Read a number. Fails closed when no spelling is present."""
    value = _first(element, keys, (int, float))
    if value is _MISSING:
        raise _missing(operation, keys, "number")
    return float(value)  # type: ignore[arg-type]


def optional_float(element: dict, fallback: float, *keys: str) -> float:
    value = _first(element, keys, (int, float))
    return fallback if value is _MISSING else float(value)  # type: ignore[arg-type]


def optional_bool(element: dict, fallback: bool, *keys: str) -> bool:
    """Read a bool.

    RIoT spells these both ways — ``is_back_edge`` is a JSON bool, ``isDelete`` is 0 or 1 —
    so both are accepted.
    """
    for key in keys:
        value = element.get(key, _MISSING)
        if isinstance(value, bool):
            return value
        if isinstance(value, int):
            return value != 0
    return fallback


def require_str(element: dict, operation: str, *keys: str) -> str:
    """Read a string. Fails closed when no spelling holds a non-blank one."""
    value = optional_str(element, *keys)
    if value is None or not value.strip():
        raise _missing(operation, keys, "string")
    return value


def optional_str(element: dict, *keys: str) -> str | None:
    for key in keys:
        value = element.get(key)
        if isinstance(value, str):
            return value
    return None


def _first(element: dict, keys: tuple[str, ...], types: tuple[type, ...]) -> Any:
    for key in keys:
        value = element.get(key, _MISSING)
        # bool is a subclass of int in Python; a JSON true here is a shape error, not a 1.
        if value is not _MISSING and isinstance(value, types) and not isinstance(value, bool):
            return value
    return _MISSING


def _missing(operation: str, keys: tuple[str, ...], kind: str) -> RiotApiException:
    return RiotApiException(
        f"{operation} element is missing a {kind} under any of: {', '.join(keys)}."
    )
