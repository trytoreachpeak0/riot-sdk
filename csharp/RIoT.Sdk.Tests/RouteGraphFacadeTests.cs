using System.Net;
using System.Text;
using RIoT.Sdk.Core;
using RIoT.Sdk.Facade;

namespace RIoT.Sdk.Tests;

/// <summary>
/// Seam: the five imap route-graph endpoints and the custom deserialization they need
/// (BC-MAP-003 / ADR-sdk-0005).
/// </summary>
/// <remarks>
/// Fixtures are verbatim response bodies from Round 43 against the production RIoT
/// (<c>172.19.206.222:8888</c>, mapId 25), trimmed to one or two elements. They are the reason
/// this facade exists: the generated Kiota models read <c>snode</c>, the wire says <c>s_node</c>.
/// </remarks>
public class RouteGraphFacadeTests
{
    // runs/003-edges-map-25.json, first edge, every key as it came off the wire.
    private const string EdgesBody =
        """
        {"code":"0","message":"成功","msgDetail":"","result":[
          {"cost":15450.0,"cx":0,"cy":0,"desc":"","direction":1,"dx":0,"dy":0,
           "e_facing":1570.7963267948965,"e_node":1,"ex":-18410,"ey":40340,"id":1,
           "is_back_edge":false,"limit_v":0,"limit_w":0,"param":255,"radius":0,
           "robot_direction":1,"rotate_direction":1,"s_facing":1570.7963267948965,
           "s_node":2,"sx":-18410,"sy":24890,"type":1,"user_define_properties":{}}
        ],"tid":""}
        """;

    // runs/004-stations-map-25.json, the two stations that share edge 187 (Round 43 §2).
    private const string StationsBody =
        """
        {"code":"0","message":"成功","msgDetail":"","result":[
          {"check_pos.x":0,"check_pos.y":0,"check_pos.yaw":0,"desc":"","dmcode_id":"",
           "edge_id":187,"enter_backward":false,"enter_pos.x":0,"enter_pos.y":0,
           "enter_pos.yaw":0,"exit_backward":false,"exit_pos.x":0,"exit_pos.y":0,
           "exit_pos.yaw":0,"id":1,"name":"N1-1","no_rotate":false,"param":0,
           "pgv_offset.x":0,"pgv_offset.y":0,"pgv_offset.yaw":0,
           "pos.x":760.0,"pos.y":27520.0,"pos.yaw":4731.3,"pos_dynamic":false,
           "station_offset":0,"type":1,"user_define_properties":{}},
          {"check_pos.x":0,"check_pos.y":0,"check_pos.yaw":0,"desc":"","dmcode_id":"",
           "edge_id":187,"enter_backward":false,"enter_pos.x":0,"enter_pos.y":0,
           "enter_pos.yaw":0,"exit_backward":false,"exit_pos.x":0,"exit_pos.y":0,
           "exit_pos.yaw":0,"id":2,"name":"N1-2","no_rotate":false,"param":0,
           "pgv_offset.x":0,"pgv_offset.y":0,"pgv_offset.yaw":0,
           "pos.x":730.0,"pos.y":29110.0,"pos.yaw":4725.0,"pos_dynamic":false,
           "station_offset":0,"type":1,"user_define_properties":{}}
        ],"tid":""}
        """;

    // runs/005-removed-edge-map-25.json and 007 verbatim — map25 has nothing removed.
    private const string EmptyResultBody =
        """{"code":"0","message":"成功","msgDetail":"","result":[],"tid":""}""";

    // runs/008-edge-group-all.json, one member of one group. Note camelCase on this endpoint,
    // and that map25 has no groups at all — every group on this RIoT belongs to another Map.
    private const string EdgeGroupsBody =
        """
        {"code":"0","message":"成功","msgDetail":"","result":{
          "老厂电梯":[
            {"edgeId":8,"gmtCreate":"2024-06-26 10:57:58","gmtUpdate":"2024-06-26 10:57:58",
             "id":223,"isDelete":0,"mapId":14,"mapName":"新厂二楼","name":"老厂电梯",
             "type":"SINGLE_VEHICLE_ONLY"}
          ]
        },"tid":""}
        """;

    [Fact]
    public async Task ListEdges_reads_the_snake_case_compound_keys_the_generated_model_misses()
    {
        await using var session = SessionOver(EdgesBody);

        var edges = await session.Maps.ListEdgesAsync(
            mapId: 25,
            cancellationToken: TestContext.Current.CancellationToken);

        var edge = Assert.Single(edges);
        Assert.Equal(1, edge.Id);
        // s_node/e_node: the generated Edge model reads snode/enode and would leave these null.
        Assert.Equal(2, edge.StartNode);
        Assert.Equal(1, edge.EndNode);
        Assert.Equal(15450.0, edge.CostMm);
        Assert.Equal((-18410, 24890), (edge.StartX, edge.StartY));
        Assert.Equal((-18410, 40340), (edge.EndX, edge.EndY));
        Assert.Equal(1570.7963267948965, edge.StartFacing);
        Assert.Equal(1570.7963267948965, edge.EndFacing);
        // is_back_edge, not isBackEdge.
        Assert.False(edge.IsBackEdge);
        Assert.Equal(1, edge.Direction);
        Assert.Equal(1, edge.Type);
        Assert.Equal(string.Empty, edge.Description);
    }

    [Fact]
    public async Task ListEdges_also_accepts_the_openapi_spelling_of_the_node_keys()
    {
        const string body =
            """
            {"code":"0","result":[
              {"id":1,"snode":2,"enode":1,"cost":15450.0,
               "sx":-18410,"sy":24890,"ex":-18410,"ey":40340}
            ]}
            """;
        await using var session = SessionOver(body);

        var edge = Assert.Single(await session.Maps.ListEdgesAsync(
            mapId: 25,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal((2, 1), (edge.StartNode, edge.EndNode));
    }

    [Fact]
    public async Task ListEdges_fails_closed_when_no_spelling_of_the_start_node_is_present()
    {
        const string body =
            """
            {"code":"0","result":[
              {"id":1,"e_node":1,"cost":15450.0,"sx":-18410,"sy":24890,"ex":-18410,"ey":40340}
            ]}
            """;
        await using var session = SessionOver(body);

        var error = await Assert.ThrowsAsync<RiotApiException>(() => session.Maps.ListEdgesAsync(
            mapId: 25,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("s_node", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListStationDetails_reads_the_keys_that_literally_contain_a_dot()
    {
        await using var session = SessionOver(StationsBody);

        var stations = await session.Maps.ListStationDetailsAsync(
            mapId: 25,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, stations.Count);

        var first = stations[0];
        Assert.Equal(25, first.MapId);
        Assert.Equal(1, first.StationId);
        Assert.Equal("N1-1", first.Name);
        // pos.x / pos.y / pos.yaw are property names containing a dot, not a nested object.
        Assert.Equal(760.0, first.PosX);
        Assert.Equal(27520.0, first.PosY);
        Assert.Equal(4731.3, first.PosYaw);
        Assert.Equal(0, first.StationOffset);
        Assert.Equal(1, first.Type);
    }

    [Fact]
    public async Task ListStationDetails_carries_the_edge_id_that_places_a_station_on_a_node()
    {
        await using var session = SessionOver(StationsBody);

        var stations = await session.Maps.ListStationDetailsAsync(
            mapId: 25,
            cancellationToken: TestContext.Current.CancellationToken);

        // Both stations sit on edge 187, at opposite ends — placement is decided downstream by
        // comparing their coordinates against that edge's endpoints, so the facade must carry
        // edge_id and pos through intact.
        Assert.All(stations, s => Assert.Equal(187, s.EdgeId));
        Assert.NotEqual(stations[0].PosY, stations[1].PosY);
    }

    [Fact]
    public async Task ListStationDetails_fails_closed_when_the_dotted_position_is_absent()
    {
        const string body = """{"code":"0","result":[{"id":1,"name":"N1-1","edge_id":187}]}""";
        await using var session = SessionOver(body);

        var error = await Assert.ThrowsAsync<RiotApiException>(
            () => session.Maps.ListStationDetailsAsync(
                mapId: 25,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("pos.x", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListRemovedEdges_returns_an_empty_list_for_a_Map_with_nothing_removed()
    {
        await using var session = SessionOver(EmptyResultBody);

        Assert.Empty(await session.Maps.ListRemovedEdgesAsync(
            mapId: 25,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListRemovedEdges_reads_the_shape_the_openapi_description_declares()
    {
        const string body =
            """
            {"code":"0","result":[
              {"edgeId":187,"gmtCreate":"2024-06-26 10:57:58","id":223,"mapId":25}
            ]}
            """;
        await using var session = SessionOver(body);

        var removed = Assert.Single(await session.Maps.ListRemovedEdgesAsync(
            mapId: 25,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(187, removed.EdgeId);
        Assert.Equal(223, removed.Id);
        Assert.Equal(25, removed.MapId);
    }

    [Fact]
    public async Task ListRemovedEdges_fails_closed_rather_than_dropping_an_unreadable_removal()
    {
        // A removal whose edge id cannot be read must raise: silently skipping it would leave the
        // caller routing over an edge RIoT has taken out of the graph.
        const string body = """{"code":"0","result":[{"id":223,"mapId":25}]}""";
        await using var session = SessionOver(body);

        var error = await Assert.ThrowsAsync<RiotApiException>(
            () => session.Maps.ListRemovedEdgesAsync(
                mapId: 25,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("edgeId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListRemovedStations_reads_the_shape_the_openapi_description_declares()
    {
        const string body =
            """
            {"code":"0","result":[
              {"id":7,"mapId":25,"stationId":170,"stationName":"N9-3"}
            ]}
            """;
        await using var session = SessionOver(body);

        var removed = Assert.Single(await session.Maps.ListRemovedStationsAsync(
            mapId: 25,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(170, removed.StationId);
        Assert.Equal("N9-3", removed.StationName);
    }

    [Fact]
    public async Task ListEdgeGroups_flattens_the_group_name_keyed_map_and_reads_camelCase()
    {
        await using var session = SessionOver(EdgeGroupsBody);

        var group = Assert.Single(await session.Maps.ListEdgeGroupsAsync(
            TestContext.Current.CancellationToken));

        // The group name is only the outer key of the response map; it is copied into each entry.
        Assert.Equal("老厂电梯", group.GroupName);
        Assert.Equal(8, group.EdgeId);
        Assert.Equal(14, group.MapId);
        Assert.Equal("新厂二楼", group.MapName);
        Assert.Equal("SINGLE_VEHICLE_ONLY", group.Type);
        // isDelete is 0/1 on the wire, not a JSON bool.
        Assert.False(group.IsDeleted);
    }

    [Fact]
    public async Task ListEdgeGroups_treats_an_empty_array_as_no_groups()
    {
        // map25 — the 8005 working Map — has no edge groups at all, and Round 43 never saw this
        // endpoint return an empty result, so both spellings of "none" have to be accepted.
        await using var empty = SessionOver(EmptyResultBody);
        Assert.Empty(await empty.Maps.ListEdgeGroupsAsync(TestContext.Current.CancellationToken));

        await using var emptyObject = SessionOver("""{"code":"0","result":{},"tid":""}""");
        Assert.Empty(await emptyObject.Maps.ListEdgeGroupsAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListEdgeGroups_fails_closed_on_a_populated_shape_nobody_has_seen()
    {
        await using var session = SessionOver("""{"code":"0","result":[{"edgeId":8}]}""");

        var error = await Assert.ThrowsAsync<RiotApiException>(
            () => session.Maps.ListEdgeGroupsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("expected an object keyed by group name", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListEdgeGroups_keeps_the_non_iso_timestamps_as_raw_strings()
    {
        await using var session = SessionOver(EdgeGroupsBody);

        var group = Assert.Single(await session.Maps.ListEdgeGroupsAsync(
            TestContext.Current.CancellationToken));

        // "2024-06-26 10:57:58" is not ISO 8601; parsing it as an instant is the consumer's call,
        // and the generated model's GetDateTimeOffsetValue would have failed on it.
        Assert.Equal("2024-06-26 10:57:58", group.GmtUpdate);
    }

    [Fact]
    public async Task A_business_failure_code_raises_instead_of_returning_an_empty_graph()
    {
        const string body = """{"code":"00002","message":"失败","result":null}""";
        await using var session = SessionOver(body);

        var error = await Assert.ThrowsAsync<RiotApiException>(() => session.Maps.ListEdgesAsync(
            mapId: 25,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("00002", error.BusinessCode);
    }

    [Fact]
    public async Task Each_method_calls_the_endpoint_the_whitelist_approved()
    {
        // These five methods route by hand off the generated request builders rather than through
        // a generated model, so nothing else would notice a wrong URL: the fixed-body handler
        // answers any path. REQ-0146 approved exactly these five and no others.
        var recorder = new RecordingHandler(EmptyResultBody);
        await using var session = SessionOver(recorder);

        var ct = TestContext.Current.CancellationToken;
        await session.Maps.ListEdgesAsync(mapId: 25, cancellationToken: ct);
        await session.Maps.ListStationDetailsAsync(mapId: 25, cancellationToken: ct);
        await session.Maps.ListRemovedEdgesAsync(mapId: 25, cancellationToken: ct);
        await session.Maps.ListRemovedStationsAsync(mapId: 25, cancellationToken: ct);
        await session.Maps.ListEdgeGroupsAsync(ct);

        Assert.Equal(
            [
                "/api/imap/v1/mapInfo/edges/25",
                "/api/imap/v1/mapInfo/stations/25",
                "/api/imap/v1/mapResource/removedEdge/25",
                "/api/imap/v1/mapResource/removedStation/25",
                "/api/imap/v1/mapEdgeGroup/all",
            ],
            recorder.Urls);
    }

    private static RiotSession SessionOver(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://riot.test/") };
        var options = new RiotOptions
        {
            BaseUrl = "http://riot.test",
            CallApiKey = "test-call-api-key",
        };

        return new RiotSession(options, http);
    }

    private static RiotSession SessionOver(string body)
    {
        var http = new HttpClient(new FixedJsonHandler(HttpStatusCode.OK, body))
        {
            BaseAddress = new Uri("http://riot.test/"),
        };

        var options = new RiotOptions
        {
            BaseUrl = "http://riot.test",
            CallApiKey = "test-call-api-key",
        };

        return new RiotSession(options, http);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly List<string> _urls = [];

        public RecordingHandler(string body) => _body = body;

        public IReadOnlyList<string> Urls => _urls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _urls.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FixedJsonHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public FixedJsonHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
