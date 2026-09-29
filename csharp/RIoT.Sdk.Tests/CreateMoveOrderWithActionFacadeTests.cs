using System.Net;
using System.Text;
using System.Text.Json;
using RIoT.Sdk.Core;
using RIoT.Sdk.Facade;

namespace RIoT.Sdk.Tests;

/// <summary>
/// Seam: the move + act overload of CreateMoveOrderAsync (charging order shape,
/// program allowlist 1.2 shape two) and the act-mission facts read back by
/// FindOrderByUpperIdAsync.
/// </summary>
public class CreateMoveOrderWithActionFacadeTests
{
    private const string DeviceKey = "BROKERX-aee2f93d717546cf9510c98c854fe83e";

    private const string CreateSuccessBody =
        """{"code":"0","message":"成功","result":{"id":488650,"orderId":"order-2079374239101747200","upperId":"UPPER-CHARGE","orderState":1}}""";

    // The exact single-segment request body the SDK sent before the overload existed.
    // Pinned byte for byte: the charging overload must not change it.
    private const string SingleSegmentRequestBody =
        """{"appointVehicleKey":"BROKERX-aee2f93d717546cf9510c98c854fe83e","isAppointEnable":1,"lockStatus":0,"mission":[{"destination":211,"mapId":26,"type":"move"}],"orderName":"UPPER-CHARGE","upperId":"UPPER-CHARGE"}""";

    // Trimmed from program repo rcs/riot-behavior-lab/evidence/rounds/2026-07-21-round-24/
    // runs/S1b-hang-detail.json: RIoT expanded move(6)+act(78,1,0) into
    // move(8) -> move(6) -> act, the act failed with 407802 and the order hung (9).
    private const string Round24HangDetailBody =
        """
        {"code":"0","message":"成功","result":{"id":488650,"orderId":"order-2079374239101747200","upperId":"riot-behavior-lab-R24-fail-m6-20260721-091202","orderState":9,"appointVehicleKey":"BROKERX-aee2f93d717546cf9510c98c854fe83e","executeVehicleKey":"BROKERX-aee2f93d717546cf9510c98c854fe83e","endStationNo":6,"missions":[{"actionId":0,"actionParam1":0,"actionParam2":0,"destination":8,"mapId":30,"resultCode":900,"resultStr":"订单完成","type":"move"},{"actionId":0,"actionParam1":0,"actionParam2":0,"destination":6,"mapId":30,"resultCode":900,"resultStr":"订单完成","type":"move"},{"actionId":78,"actionName":"charge ;action: 78;  1;  0;  null","actionParam1":1,"actionParam2":0,"destination":0,"mapId":0,"resultCode":407802,"resultStr":"未知类型错误,导致订单挂起:错误编码为:407802","type":"act"}]}}
        """;

    [Fact]
    public async Task CreateMoveOrder_with_action_sends_move_then_act_with_same_other_fields()
    {
        using var handler = new RecordingHandler(CreateSuccessBody);
        await using var session = CreateSession(handler);

        OrderRef order = await session.Order.CreateMoveOrderAsync(
            upperId: "UPPER-CHARGE",
            appointVehicleKey: DeviceKey,
            mapId: 26,
            destinationStationId: 211,
            actionAfterMove: new OrderMissionAction(78, 1, 0));

        Assert.Equal(488650, order.Id);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/order/v1/add/byDefaultMissions", handler.Path);

        using JsonDocument document = JsonDocument.Parse(handler.Body!);
        JsonElement root = document.RootElement;
        Assert.Equal(
            ["appointVehicleKey", "isAppointEnable", "lockStatus", "mission", "orderName", "upperId"],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(DeviceKey, root.GetProperty("appointVehicleKey").GetString());
        Assert.Equal(1, root.GetProperty("isAppointEnable").GetInt32());
        Assert.Equal(0, root.GetProperty("lockStatus").GetInt32());
        Assert.Equal("UPPER-CHARGE", root.GetProperty("orderName").GetString());
        Assert.Equal("UPPER-CHARGE", root.GetProperty("upperId").GetString());

        JsonElement[] missions = root.GetProperty("mission").EnumerateArray().ToArray();
        Assert.Equal(2, missions.Length);

        JsonElement move = missions[0];
        Assert.Equal(
            ["destination", "mapId", "type"],
            move.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("move", move.GetProperty("type").GetString());
        Assert.Equal(26, move.GetProperty("mapId").GetInt32());
        Assert.Equal(211, move.GetProperty("destination").GetInt32());

        JsonElement act = missions[1];
        Assert.Equal(
            ["actionId", "actionParam1", "actionParam2", "type"],
            act.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("act", act.GetProperty("type").GetString());
        Assert.Equal(78, act.GetProperty("actionId").GetInt32());
        Assert.Equal(1, act.GetProperty("actionParam1").GetInt32());
        Assert.Equal(0, act.GetProperty("actionParam2").GetInt32());
    }

    [Fact]
    public async Task CreateMoveOrder_without_action_keeps_the_single_segment_body_byte_for_byte()
    {
        using var handler = new RecordingHandler(CreateSuccessBody);
        await using var session = CreateSession(handler);

        await session.Order.CreateMoveOrderAsync(
            upperId: "UPPER-CHARGE",
            appointVehicleKey: DeviceKey,
            mapId: 26,
            destinationStationId: 211);

        Assert.Equal(SingleSegmentRequestBody, handler.Body);
    }

    [Fact]
    public async Task CreateMoveOrder_with_action_uses_order_name_like_the_single_segment_overload()
    {
        using var handler = new RecordingHandler(CreateSuccessBody);
        await using var session = CreateSession(handler);

        await session.Order.CreateMoveOrderAsync(
            upperId: "UPPER-CHARGE",
            appointVehicleKey: DeviceKey,
            mapId: 26,
            destinationStationId: 211,
            actionAfterMove: new OrderMissionAction(78, 1, 0),
            orderName: "charge-211");

        using JsonDocument document = JsonDocument.Parse(handler.Body!);
        Assert.Equal("charge-211", document.RootElement.GetProperty("orderName").GetString());
    }

    [Theory]
    [InlineData(0, 211)]
    [InlineData(26, 0)]
    [InlineData(-1, 211)]
    [InlineData(26, -1)]
    public async Task CreateMoveOrder_with_action_rejects_non_positive_map_or_destination_without_http(
        int mapId,
        int destinationStationId)
    {
        using var handler = new RecordingHandler(CreateSuccessBody);
        await using var session = CreateSession(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            session.Order.CreateMoveOrderAsync(
                upperId: "UPPER-INVALID",
                appointVehicleKey: DeviceKey,
                mapId: mapId,
                destinationStationId: destinationStationId,
                actionAfterMove: new OrderMissionAction(78, 1, 0)));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateMoveOrder_with_null_action_is_rejected_without_http()
    {
        using var handler = new RecordingHandler(CreateSuccessBody);
        await using var session = CreateSession(handler);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            session.Order.CreateMoveOrderAsync(
                upperId: "UPPER-NULL-ACTION",
                appointVehicleKey: DeviceKey,
                mapId: 26,
                destinationStationId: 211,
                actionAfterMove: null!));

        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(-78, 1, 0)]
    [InlineData(78, -1, 0)]
    [InlineData(78, 1, -1)]
    public void OrderMissionAction_rejects_invalid_values(int actionId, int param1, int param2)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderMissionAction(actionId, param1, param2));
    }

    [Fact]
    public async Task CreateMoveOrder_with_action_rejects_mismatched_response_upper_id_without_retry()
    {
        using var handler = new RecordingHandler(
            """{"code":"0","result":{"id":9,"orderId":"ORDER-9","upperId":"OTHER-UPPER","orderState":1}}""");
        await using var session = CreateSession(handler);

        RiotApiException error = await Assert.ThrowsAsync<RiotApiException>(() =>
            session.Order.CreateMoveOrderAsync(
                upperId: "EXPECTED-UPPER",
                appointVehicleKey: DeviceKey,
                mapId: 26,
                destinationStationId: 211,
                actionAfterMove: new OrderMissionAction(78, 1, 0)));

        Assert.Equal("order-upper-id-mismatch", error.BusinessCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CreateMoveOrder_with_action_business_failure_throws_without_retry()
    {
        using var handler = new RecordingHandler(
            """{"code":"0610008","message":"upperId exists","result":null}""");
        await using var session = CreateSession(handler);

        RiotApiException error = await Assert.ThrowsAsync<RiotApiException>(() =>
            session.Order.CreateMoveOrderAsync(
                upperId: "UPPER-EXISTS",
                appointVehicleKey: DeviceKey,
                mapId: 26,
                destinationStationId: 211,
                actionAfterMove: new OrderMissionAction(78, 1, 0)));

        Assert.Equal("0610008", error.BusinessCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task FindOrderByUpperId_reads_back_act_mission_facts_from_round24_hang()
    {
        using var handler = new RecordingHandler(Round24HangDetailBody);
        await using var session = CreateSession(handler);

        OrderLookupResult result = await session.Order.FindOrderByUpperIdAsync(
            "riot-behavior-lab-R24-fail-m6-20260721-091202");

        Assert.Equal(OrderLookupStatus.Found, result.Status);
        OrderSnapshot order = Assert.IsType<OrderSnapshot>(result.Order);
        Assert.Equal(9, order.OrderState);
        Assert.Equal(6, order.EndStationNo);
        Assert.Equal(["move", "move", "act"], order.Missions.Select(mission => mission.Type).ToArray());

        OrderMissionSnapshot act = order.Missions[2];
        Assert.Equal(78, act.ActionId);
        Assert.Equal(1, act.ActionParam1);
        Assert.Equal(0, act.ActionParam2);
        Assert.Equal(407802, act.ResultCode);
        Assert.Equal(0, act.MapId);
        Assert.Equal(0, act.Destination);

        OrderMissionSnapshot lastMove = order.Missions[1];
        Assert.Equal(6, lastMove.Destination);
        Assert.Equal(900, lastMove.ResultCode);
        Assert.Equal(0, lastMove.ActionId);
    }

    [Fact]
    public async Task FindOrderByUpperId_reads_missing_act_facts_as_null_not_zero()
    {
        const string body =
            """{"code":"0","result":{"id":7,"orderId":"ORDER-7","upperId":"UPPER-7","orderState":3,"endStationNo":211,"missions":[{"type":"move","mapId":26,"destination":211},{"type":"act","actionId":78,"resultCode":null}]}}""";
        using var handler = new RecordingHandler(body);
        await using var session = CreateSession(handler);

        OrderLookupResult result = await session.Order.FindOrderByUpperIdAsync("UPPER-7");

        OrderSnapshot order = Assert.IsType<OrderSnapshot>(result.Order);
        OrderMissionSnapshot move = order.Missions[0];
        Assert.Null(move.ActionId);
        Assert.Null(move.ActionParam1);
        Assert.Null(move.ActionParam2);
        Assert.Null(move.ResultCode);

        OrderMissionSnapshot act = order.Missions[1];
        Assert.Equal(78, act.ActionId);
        Assert.Null(act.ActionParam1);
        Assert.Null(act.ActionParam2);
        Assert.Null(act.ResultCode);
        Assert.Null(act.MapId);
        Assert.Null(act.Destination);
    }

    private static RiotSession CreateSession(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://riot.test/"),
        };
        return new RiotSession(
            new RiotOptions
            {
                BaseUrl = "http://riot.test",
                CallApiKey = "test-call-api-key",
            },
            http);
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
