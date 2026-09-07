namespace RIoT.Sdk.Core;

/// <summary>
/// A directed edge of a Map's station graph (BC-MAP-003).
/// </summary>
/// <remarks>
/// The wire format is snake_case and its compound keys do not match the generated
/// <c>Edge</c> model (<c>s_node</c>/<c>e_node</c>, not <c>snode</c>/<c>enode</c>), so the facade
/// deserializes this shape itself. Vehicle kinematics on the wire (<c>limit_v</c>, <c>limit_w</c>,
/// <c>robot_direction</c>, <c>rotate_direction</c>, <c>param</c>, <c>radius</c>, the Bezier-looking
/// <c>cx</c>/<c>cy</c>/<c>dx</c>/<c>dy</c>) is deliberately not carried: it is not part of the
/// route graph, and naming a field whose meaning is unverified is worse than omitting it.
/// </remarks>
/// <param name="Id">RIoT <c>edge.id</c>, unique within the Map.</param>
/// <param name="StartNode">Wire <c>s_node</c>. The edge runs StartNode → EndNode.</param>
/// <param name="EndNode">Wire <c>e_node</c>.</param>
/// <param name="CostMm">
/// Wire <c>cost</c>. Round 43 measured it to be the edge's Euclidean length in mm
/// (median deviation 0.012 mm over map25's 403 edges), same unit as
/// <see cref="RouteCost.CostsMm"/> — that last part is an inference, never measured
/// on the same origin/destination pair.
/// </param>
/// <param name="StartX">Wire <c>sx</c>, mm.</param>
/// <param name="StartY">Wire <c>sy</c>, mm.</param>
/// <param name="EndX">Wire <c>ex</c>, mm.</param>
/// <param name="EndY">Wire <c>ey</c>, mm.</param>
/// <param name="StartFacing">Wire <c>s_facing</c>.</param>
/// <param name="EndFacing">Wire <c>e_facing</c>.</param>
/// <param name="Direction">Wire <c>direction</c>. Observed as 1 on every map25 edge.</param>
/// <param name="IsBackEdge">Wire <c>is_back_edge</c>. Observed as false on every map25 edge.</param>
/// <param name="Type">Wire <c>type</c>.</param>
/// <param name="Description">Wire <c>desc</c>; empty on every map25 edge.</param>
public sealed record MapEdge(
    int Id,
    int StartNode,
    int EndNode,
    double CostMm,
    int StartX,
    int StartY,
    int EndX,
    int EndY,
    double StartFacing,
    double EndFacing,
    int Direction,
    bool IsBackEdge,
    int Type,
    string Description);
