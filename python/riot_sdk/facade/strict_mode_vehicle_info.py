"""getVehicleInfo models whose vehicle ``mode`` is kept as the raw string (control-server#639, riot-sdk#3).

Python's Kiota reads an enum by member name after upper-casing the first letter, so ``"mODE_AUTO"`` came out as
MODE_AUTO while C# gave None (riot-sdk#3 second review L3). A consumer proves standstill on MODE_AUTO, so both
languages keep only the declared spellings, by the same mechanism: the generated model parses every field except
``mode``, which is read raw and compared exactly. Only the parse is replaced; the request is the generated one.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Callable

from kiota_abstractions.serialization import ParseNode

from riot_sdk.generated.task.models.vehicle import Vehicle
from riot_sdk.generated.task.models.vehicle_all_info import VehicleAllInfo

# The three mode values RIoT's task API declares.
KNOWN_MODES = ("MODE_AUTO", "MODE_MANUAL", "UNKNOWN")


def exact_mode(raw: str | None) -> str | None:
    """``raw`` when it is exactly one of ``KNOWN_MODES``, otherwise None."""
    return raw if raw in KNOWN_MODES else None


@dataclass
class StrictModeVehicle(Vehicle):
    # The mode member as sent; None when absent, null, or not a JSON string.
    raw_mode: str | None = None

    @staticmethod
    def create_from_discriminator_value(parse_node: ParseNode) -> StrictModeVehicle:
        if parse_node is None:
            raise TypeError("parse_node cannot be null.")
        return StrictModeVehicle()

    def get_field_deserializers(self) -> dict[str, Callable[[ParseNode], None]]:
        fields = super().get_field_deserializers()
        fields["mode"] = lambda n: setattr(self, "raw_mode", _string_or_none(n))
        return fields


@dataclass
class StrictModeVehicleAllInfo(VehicleAllInfo):
    @staticmethod
    def create_from_discriminator_value(parse_node: ParseNode) -> StrictModeVehicleAllInfo:
        if parse_node is None:
            raise TypeError("parse_node cannot be null.")
        return StrictModeVehicleAllInfo()

    def get_field_deserializers(self) -> dict[str, Callable[[ParseNode], None]]:
        fields = super().get_field_deserializers()
        fields["vehicle"] = lambda n: setattr(self, "vehicle", n.get_object_value(StrictModeVehicle))
        return fields


def _string_or_none(node: ParseNode) -> Any:
    # get_str_value returns the JSON value as-is when it is not a string; only a str counts.
    value = node.get_str_value()
    return value if isinstance(value, str) else None
