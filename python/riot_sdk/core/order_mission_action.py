from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class OrderMissionAction:
    """An ``act`` mission appended after the move mission of a byDefaultMissions order.

    For example charging: action_id 78, action_param1 1, action_param2 0.
    Keeps callers off the Kiota-generated MissionDTO.
    """

    action_id: int
    action_param1: int
    action_param2: int

    def __post_init__(self) -> None:
        if self.action_id <= 0:
            raise ValueError("action_id must be positive")
        if self.action_param1 < 0:
            raise ValueError("action_param1 must not be negative")
        if self.action_param2 < 0:
            raise ValueError("action_param2 must not be negative")
