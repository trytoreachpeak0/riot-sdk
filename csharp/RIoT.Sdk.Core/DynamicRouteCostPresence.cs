namespace RIoT.Sdk.Core;

/// <summary>
/// Whether RIoT currently holds any dynamic route cost, and how many entries.
/// </summary>
/// <remarks>
/// <para>
/// Presence and a count, not the values. <c>GET /api/task/v1/route/</c> has answered
/// <c>"result":{}</c> on every observation anyone has made of it — Round 15 on the test RCS and
/// Round 43 on the production one — so <b>nobody has seen its populated shape</b>. Inventing a
/// type for a shape no one has observed would be a guess wearing a domain model's clothes; the
/// SDK reports what can actually be established.
/// </para>
/// <para>
/// That is enough for the consumer this exists for. The RouteGraphSnapshot engine builds its graph
/// on the assumption that no dynamic cost is in play, and needs to notice the moment that stops
/// being true — at which point the graph is no longer a complete account of routing and the
/// snapshot is stale. What the entries say does not change that verdict.
/// </para>
/// </remarks>
/// <param name="Present">True when the result carried at least one entry.</param>
/// <param name="EntryCount">
/// How many entries the result carried. Zero for the empty object every observation has seen.
/// </param>
public sealed record DynamicRouteCostPresence(bool Present, int EntryCount);
