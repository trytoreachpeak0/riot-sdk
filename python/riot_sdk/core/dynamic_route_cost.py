from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class DynamicRouteCostPresence:
    """Whether RIoT currently holds any dynamic route cost, and how many entries.

    Presence and a count, not the values. ``GET /api/task/v1/route/`` has answered
    ``"result":{}`` on every observation anyone has made of it -- Round 15 on the test RCS and
    Round 43 on the production one -- so **nobody has seen its populated shape**. Inventing a type
    for a shape no one has observed would be a guess wearing a domain model's clothes; the SDK
    reports what can actually be established.

    That is enough for the consumer this exists for. The RouteGraphSnapshot engine builds its
    graph on the assumption that no dynamic cost is in play, and needs to notice the moment that
    stops being true -- at which point the graph is no longer a complete account of routing and
    the snapshot is stale. What the entries say does not change that verdict.
    """

    present: bool
    entry_count: int
