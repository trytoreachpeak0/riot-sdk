namespace RIoT.Sdk.Core;

/// <summary>
/// An edge currently removed from a Map — runtime state of the route graph (BC-MAP-003).
/// </summary>
/// <remarks>
/// Round 43 observed <c>result: []</c> on map25, so the element shape is taken from the
/// <c>MapRemovedEdge对象</c> component of the imap OpenAPI description and has never been seen
/// populated. The facade therefore fails closed: an element it cannot read an edge id out of
/// raises <see cref="RiotApiException"/> rather than being skipped, because silently dropping a
/// removal would leave a consumer routing over an edge that no longer exists.
/// </remarks>
/// <param name="Id">Wire <c>id</c> — the removal record's own id, not the edge's.</param>
/// <param name="MapId">Wire <c>mapId</c>.</param>
/// <param name="EdgeId">Wire <c>edgeId</c> — the removed edge, matching <see cref="MapEdge.Id"/>.</param>
public sealed record RemovedEdge(int Id, int MapId, int EdgeId);
