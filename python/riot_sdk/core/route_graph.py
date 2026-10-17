from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class MapEdge:
    """A directed edge of a Map's station graph (BC-MAP-003).

    The wire format is snake_case and its compound keys do not match the generated ``Edge``
    model (``s_node``/``e_node``, not ``snode``/``enode``), so the facade deserializes this
    shape itself. Vehicle kinematics on the wire (``limit_v``, ``limit_w``, ``robot_direction``,
    ``rotate_direction``, ``param``, ``radius``, the Bezier-looking ``cx``/``cy``/``dx``/``dy``)
    is deliberately not carried: it is not part of the route graph, and naming a field whose
    meaning is unverified is worse than omitting it.

    ``cost_mm`` is the wire ``cost``. Round 43 measured it to be the edge's Euclidean length in
    mm (median deviation 0.012 mm over map25's 403 edges), same unit as ``RouteCost.costs_mm``
    — that last part is an inference, never measured on the same origin/destination pair.
    """

    id: int
    start_node: int
    end_node: int
    cost_mm: float
    start_x: int
    start_y: int
    end_x: int
    end_y: int
    start_facing: float
    end_facing: float
    direction: int
    is_back_edge: bool
    type: int
    description: str


@dataclass(frozen=True, slots=True)
class MapStationDetail:
    """A station with its placement on the Map's edge graph (BC-MAP-003).

    Richer than ``Station``, which carries only id and name. The wire format spells several
    keys with a literal dot — ``pos.x``, not a nested object — which no generated model reads.
    The four navigation pose groups on the wire (``check_pos.*``, ``enter_pos.*``,
    ``exit_pos.*``, ``pgv_offset.*``) are deliberately not carried: they are vehicle motion
    parameters, not route graph facts.

    ``edge_id`` is the edge this station sits on. Which of that edge's two nodes it occupies is
    decided by comparing ``pos_x``/``pos_y`` against the edge's endpoints; on map25 that gave
    206 stations on 206 distinct nodes with zero collisions. ``station_offset`` is zero on all
    of them, so it cannot be used for that — carried for completeness, not for use.
    """

    map_id: int
    station_id: int
    name: str
    edge_id: int
    pos_x: float
    pos_y: float
    pos_yaw: float
    station_offset: int
    type: int
    description: str


@dataclass(frozen=True, slots=True)
class RemovedEdge:
    """An edge currently removed from a Map — runtime state of the route graph (BC-MAP-003).

    Round 43 observed ``result: []`` on map25, so the element shape is taken from the
    ``MapRemovedEdge对象`` component of the imap OpenAPI description and has never been seen
    populated. The facade therefore fails closed: an element it cannot read an edge id out of
    raises ``RiotApiException`` rather than being skipped, because silently dropping a removal
    would leave a consumer routing over an edge that no longer exists.

    ``id`` is the removal record's own id, not the edge's; ``edge_id`` matches ``MapEdge.id``.
    """

    id: int
    map_id: int
    edge_id: int


@dataclass(frozen=True, slots=True)
class RemovedStation:
    """A station currently removed from a Map — runtime state of the route graph (BC-MAP-003).

    Same evidence status as ``RemovedEdge``: ``result: []`` on map25, shape taken from the
    ``MapRemovedStation对象`` component, never seen populated, and read fail-closed.
    """

    id: int
    map_id: int
    station_id: int
    station_name: str | None


@dataclass(frozen=True, slots=True)
class MapEdgeGroup:
    """One edge's membership in a named edge group — a traffic constraint (BC-MAP-003).

    ``GET /api/imap/v1/mapEdgeGroup/all`` returns every Map's groups keyed by group name, with
    one entry per member edge. The facade flattens that map, copying the outer key into
    ``group_name``, and returns entries for every Map — filter by ``map_id``.

    Two wire quirks: this endpoint is camelCase while ``edges`` and ``stations`` on the same
    service are snake_case, and its timestamps are ``"2024-06-26 10:57:58"``, which is not
    ISO 8601 and so is carried as the raw string rather than a parsed instant. ``type`` (e.g.
    ``SINGLE_VEHICLE_ONLY``) is likewise the raw string: an unknown value must reach the
    consumer intact, not collapse into an enum default.

    Whether these constraints change RIoT's own routing result is untested (Round 43).
    """

    group_name: str
    id: int
    map_id: int
    map_name: str
    edge_id: int
    type: str
    is_deleted: bool
    gmt_create: str | None
    gmt_update: str | None
