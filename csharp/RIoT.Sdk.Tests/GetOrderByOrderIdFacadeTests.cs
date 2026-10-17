using System.Net;
using System.Text;
using RIoT.Sdk.Core;
using RIoT.Sdk.Facade;

namespace RIoT.Sdk.Tests;

/// <summary>
/// Seam: Facade get order by orderId (BC-ORDER-005).
/// Expected ids from Lab fixture E1-order-success-detail-station2.
/// </summary>
public class GetOrderByOrderIdFacadeTests
{
    private const string OrderId = "order-2079056586101358592";
    private const string UpperId = "riot-lab-I-20260720-120949";

    // Known-good body from E1-order-success-detail-station2.body
    private const string DetailSuccessBody =
        """
        {"code":"0","message":"成功","msgDetail":"","result":{"appointMapId":0,"appointStationId":0,"appointVehicleGroupId":0,"appointVehicleGroupName":"0","appointVehicleKey":"BROKERX-aee2f93d717546cf9510c98c854fe83e","changeVehicle":0,"changeVehicleReason":"","changeVehicleRecord":"","createTime":"2026-07-20 12:11:32","createdBy":"api-test","distance":0,"doneTime":"2026-07-20 12:11:40","endStationName":"站点2","endStationNo":2,"eta":0,"executeTime":"2026-07-20 12:11:36","executeVehicleKey":"BROKERX-aee2f93d717546cf9510c98c854fe83e","executeVehicleName":"新基测试300c协作1","executingIndex":-1,"failReason":"","finalState":true,"id":488003,"isAppointEnable":1,"isDeleted":0,"lockStatus":0,"missions":[{"actionId":0,"actionName":"","actionParam1":0,"actionParam2":0,"actionParamStr":"","createTime":"2026-07-20 12:11:35","createType":0,"destination":2,"destinationName":"站点2","executeTime":"2026-07-20 12:11:36","executingIndex":0,"failStrategy":"FAIL_STRATEGY_VOID","failValue":"0","finishTime":"2026-07-20 12:11:40","functionKey":"","id":7416323,"isDeleted":0,"length":0,"mapId":29,"mapName":"api测试","missionState":2,"orderId":488003,"orderUuid":"be8cb2d2-c50c-4b36-b04b-fb034a2ca3fb","resultCode":900,"resultStr":"订单完成","speed":0.0,"successStrategy":"SUCCESS_STRATEGY_VOID","type":"move","updateTime":"2026-07-20 12:11:40","width":0}],"modifiedBy":"internal","orderId":"order-2079056586101358592","orderName":"riot-lab-I-20260720-120949","orderState":5,"orderType":1,"priority":0,"priorityQueue":0,"progress":100,"source":"FMS","startEndStationNameDetail":"api测试 站点2 > api测试 站点2","startStationName":"站点2","startStationNo":2,"taskState":"1","totalCosts":0,"updateTime":"2026-07-20 12:11:40","upperId":"riot-lab-I-20260720-120949","userId":0},"tid":""}
        """;

    [Fact]
    public async Task GetOrderByOrderId_returns_order_identifiers()
    {
        using var http = new HttpClient(new FixedJsonHandler(HttpStatusCode.OK, DetailSuccessBody))
        {
            BaseAddress = new Uri("http://riot.test/"),
        };

        var options = new RiotOptions
        {
            BaseUrl = "http://riot.test",
            CallApiKey = "test-call-api-key",
        };

        await using var session = new RiotSession(options, http);

        var order = await session.Order.GetOrderByOrderIdAsync(OrderId);

        Assert.Equal(488003, order.Id);
        Assert.Equal(OrderId, order.OrderId);
        Assert.Equal(UpperId, order.UpperId);
        Assert.Equal(5, order.OrderState);
    }

    // control-server#617: an order dispatched by hand on the RIoT web page, read back on site on 2026-10-11 15:23:21
    // (control-server evidence/field/20261011-W3-S1-charging/13-foreign-order-readback.txt): HTTP 200, code 0, a full
    // result, orderState 2, upperId an empty string. The fields that record names are its own; the rest is the lab body's
    // shape, since the field read kept only those.
    private const string WebPageOrderId = "order-2109181133127286784";

    private const string WebPageOrderBody =
        """
        {"code":"0","message":"成功","msgDetail":"","result":{"appointVehicleKey":"","createTime":"2026-10-11 15:15:43","createdBy":"admin","doneTime":"2026-10-11 15:15:48","executeVehicleKey":"BROKERX-0000000000000000000000000000a002","finalState":true,"id":1757698,"isAppointEnable":0,"isDeleted":0,"lockStatus":0,"missions":[],"orderId":"order-2109181133127286784","orderName":"iiii","orderState":2,"orderType":1,"source":"WEB","upperId":""},"tid":""}
        """;

    [Fact]
    public async Task GetOrderByOrderId_reads_an_order_without_an_upper_id()
    {
        await using var session = Session(WebPageOrderBody, out FixedJsonHandler handler);

        OrderRef order = await session.Order.GetOrderByOrderIdAsync(WebPageOrderId, TestContext.Current.CancellationToken);

        Assert.Equal(new OrderRef(1757698, WebPageOrderId, "", 2), order);
        Assert.Equal("/api/order/v1/orderRecord/detailByOrderId/" + WebPageOrderId, Assert.Single(handler.Paths));
    }

    [Theory]
    [InlineData("null", """{"code":"0","message":"成功","result":{"id":7,"orderId":"order-7","upperId":null,"orderState":5}}""")]
    [InlineData("missing", """{"code":"0","message":"成功","result":{"id":7,"orderId":"order-7","orderState":5}}""")]
    [InlineData("whitespace", """{"code":"0","message":"成功","result":{"id":7,"orderId":"order-7","upperId":"  ","orderState":5}}""")]
    public async Task GetOrderByOrderId_reads_any_absent_upper_id(string shape, string body)
    {
        await using var session = Session(body, out _);

        OrderRef order = await session.Order.GetOrderByOrderIdAsync("order-7", TestContext.Current.CancellationToken);

        Assert.Equal((7, "order-7", 5), (order.Id, order.OrderId, order.OrderState));
        Assert.Equal(shape == "whitespace" ? "  " : "", order.UpperId);
    }

    /// <summary>Only the upperId is relaxed: an answer without an id, an orderId, an orderState or a result is still refused.</summary>
    [Theory]
    [InlineData("no-result", """{"code":"0","message":"成功","msgDetail":"","tid":""}""")]
    [InlineData("null-result", """{"code":"0","message":"成功","msgDetail":"","result":null,"tid":""}""")]
    [InlineData("no-id", """{"code":"0","message":"成功","result":{"orderId":"order-1","upperId":"","orderState":2}}""")]
    [InlineData("no-order-id", """{"code":"0","message":"成功","result":{"id":1,"orderId":"","upperId":"","orderState":2}}""")]
    [InlineData("no-order-state", """{"code":"0","message":"成功","result":{"id":1,"orderId":"order-1","upperId":""}}""")]
    public async Task GetOrderByOrderId_still_refuses_an_answer_without_its_identity_or_state(string shape, string body)
    {
        Assert.NotEmpty(shape);
        await using var session = Session(body, out _);

        RiotApiException error = await Assert.ThrowsAsync<RiotApiException>(() =>
            session.Order.GetOrderByOrderIdAsync("order-1", TestContext.Current.CancellationToken));

        Assert.Equal("order-ref-missing", error.BusinessCode);
    }

    /// <summary>
    /// The read by upperId stays strict: it asks for an order by the very upperId this SDK created it under, so an answer
    /// without one is not that order.
    /// </summary>
    [Fact]
    public async Task GetOrderByUpperId_still_refuses_an_answer_without_an_upper_id()
    {
        await using var session = Session(WebPageOrderBody, out _);

        RiotApiException error = await Assert.ThrowsAsync<RiotApiException>(() =>
            session.Order.GetOrderByUpperIdAsync("UPPER-1", TestContext.Current.CancellationToken));

        Assert.Equal("order-ref-missing", error.BusinessCode);
    }

    private static RiotSession Session(string body, out FixedJsonHandler handler)
    {
        handler = new FixedJsonHandler(HttpStatusCode.OK, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://riot.test/") };
        return new RiotSession(
            new RiotOptions { BaseUrl = "http://riot.test", CallApiKey = "test-call-api-key" },
            http);
    }

    private sealed class FixedJsonHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public List<string> Paths { get; } = [];

        public FixedJsonHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
