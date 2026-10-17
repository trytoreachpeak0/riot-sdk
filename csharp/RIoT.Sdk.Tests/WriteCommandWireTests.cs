using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using RIoT.Sdk.Core;
using RIoT.Sdk.Facade;

namespace RIoT.Sdk.Tests;

/// <summary>
/// Seam: the exact HTTP request every facade POST puts on the wire -- method, path and query, HTTP version and version
/// policy, every request and content header, and body, byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// The expected requests were recorded with Microsoft.Kiota.Bundle 1.17.4 (riot-sdk 0.3.0) and hold on Kiota 1.22.2
/// with one accepted change (below). Kiota 1.21.2 changed how request bodies are serialized (recyclable streams), and
/// these requests include the emergency stop, whose body RIoT rejects with an NPE when <c>thingsProperties</c> is
/// missing (simple#48).
/// </para>
/// <para>
/// The accepted change is the HTTP version. Kiota 1.17.4 asked for HTTP/2.0 on every request; since Kiota 1.20.0 it takes
/// the version of the <see cref="HttpClient"/>, which defaults to 1.1. The policy stays RequestVersionOrLower. Against an
/// <c>http://</c> RIoT address .NET does not try h2c under that policy, so both versions send HTTP/1.1 on the wire; over
/// <c>https://</c>, 0.3.0 would negotiate HTTP/2 through ALPN and 0.3.1 stays on 1.1.
/// </para>
/// <para>The emergency-stop <c>messageId</c> is random, so it is checked for shape and then masked.</para>
/// </remarks>
public partial class WriteCommandWireTests
{
    private const string DeviceKey = "BROKERX-aee2f93d717546cf9510c98c854fe83e";
    private const string OrderId = "order-2079374239101747200";

    private const string UpperId = "UPPER-CHARGE";

    private const string SuccessBody = """{"code":"0","message":"成功"}""";

    private const string CreateOrderBody =
        """{"code":"0","message":"成功","result":{"id":488650,"orderId":"order-2079374239101747200","upperId":"UPPER-CHARGE","orderState":1}}""";

    private const string RouteCostBody =
        """{"code":"0","message":"成功","result":{"deviceCostsList":[{"costs":2210,"deviceKey":"BROKERX-aee2f93d717546cf9510c98c854fe83e","message":"ok"}],"mapId":29,"stationId":1}}""";

    private const string NearStationBody = """{"code":"0","message":"成功","result":2}""";

    public static TheoryData<string, string> Commands => new()
    {
        {
            "TriggerEmergencyStop",
            """
            POST /api/device/v1/command/sync/service/BROKERX-aee2f93d717546cf9510c98c854fe83e/triggerEmergency HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 91
            Content-Type: application/json

            {"messageId":"######","mqCallback":{"tag":"string","topic":"string"},"thingsProperties":{}}
            """
        },
        {
            "CancelEmergencyStop",
            """
            POST /api/device/v1/command/sync/service/BROKERX-aee2f93d717546cf9510c98c854fe83e/cancelEmergency HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 91
            Content-Type: application/json

            {"messageId":"######","mqCallback":{"tag":"string","topic":"string"},"thingsProperties":{}}
            """
        },
        {
            "CancelOrder",
            """
            POST /api/task/v1/order/command/order-2079374239101747200 HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 57
            Content-Type: application/json

            {"commandType":"CMD_ORDER_CANCEL","disableVehicle":false}
            """
        },
        {
            "CancelOrderWithReason",
            """
            POST /api/task/v1/order/command/order-2079374239101747200 HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 93
            Content-Type: application/json

            {"commandType":"CMD_ORDER_CANCEL","disableVehicle":false,"reason":"\u4E0A\u4F4D\u53D6\u6D88"}
            """
        },
        {
            "OrderHold",
            """
            POST /api/task/v1/order/command/order-2079374239101747200 HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 55
            Content-Type: application/json

            {"commandType":"CMD_ORDER_HELD","disableVehicle":false}
            """
        },
        {
            "OrderContinue",
            """
            POST /api/task/v1/order/command/order-2079374239101747200 HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 69
            Content-Type: application/json

            {"commandType":"CMD_ORDER_CONTINUE_FROM_HELD","disableVehicle":false}
            """
        },
        {
            "HangContinue",
            """
            POST /api/task/v1/order/command/order-2079374239101747200 HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 95
            Content-Type: application/json

            {"commandType":"CMD_ORDER_CONTINUE_FROM_HANG","disableVehicle":false,"reason":"operator retry"}
            """
        },
        {
            "DispatchEnable",
            """
            POST /api/task/vehicles/updateVehicleIntegrationLevel HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 80
            Content-Type: application/json

            {"deviceKeys":["BROKERX-aee2f93d717546cf9510c98c854fe83e"],"serviceId":"enable"}
            """
        },
        {
            "DispatchDisable",
            """
            POST /api/task/vehicles/updateVehicleIntegrationLevel HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 81
            Content-Type: application/json

            {"deviceKeys":["BROKERX-aee2f93d717546cf9510c98c854fe83e"],"serviceId":"disable"}
            """
        },
        {
            "PriorityExec",
            """
            POST /api/order/v1/orderRecordPriorityExec?orderTaskKey=order-2079374239101747200 HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key

            (no body)
            """
        },
        {
            "CreateMoveOrder",
            """
            POST /api/order/v1/add/byDefaultMissions HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 208
            Content-Type: application/json

            {"appointVehicleKey":"BROKERX-aee2f93d717546cf9510c98c854fe83e","isAppointEnable":1,"lockStatus":0,"mission":[{"destination":211,"mapId":26,"type":"move"}],"orderName":"UPPER-CHARGE","upperId":"UPPER-CHARGE"}
            """
        },
        {
            "CreateMoveOrderWithAction",
            """
            POST /api/order/v1/add/byDefaultMissions HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 271
            Content-Type: application/json

            {"appointVehicleKey":"BROKERX-aee2f93d717546cf9510c98c854fe83e","isAppointEnable":1,"lockStatus":0,"mission":[{"destination":211,"mapId":26,"type":"move"},{"actionId":78,"actionParam1":1,"actionParam2":0,"type":"act"}],"orderName":"UPPER-CHARGE","upperId":"UPPER-CHARGE"}
            """
        },
        {
            "RouteCost",
            """
            POST /api/task/v1/route/getRouteCostsBy HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 84
            Content-Type: application/json

            {"deviceKeys":["BROKERX-aee2f93d717546cf9510c98c854fe83e"],"mapId":29,"stationId":1}
            """
        },
        {
            "NearEnd",
            """
            POST /api/task/v1/route/queryNearEnd HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 55
            Content-Type: application/json

            {"endStationIds":[2,3,4],"mapId":28,"startStationId":1}
            """
        },
        {
            "NearStart",
            """
            POST /api/task/v1/route/queryNearestStart HTTP/1.1 RequestVersionOrLower
            Accept: application/json
            Authorization: Bearer test-call-api-key
            Content-Length: 55
            Content-Type: application/json

            {"endStationId":4,"mapId":28,"startStationIds":[1,2,3]}
            """
        },
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task Write_command_puts_the_recorded_request_on_the_wire(string command, string expected)
    {
        string actual = await CaptureAsync(command);

        Assert.Equal(expected.ReplaceLineEndings("\n"), actual);
    }

    internal static async Task<string> CaptureAsync(string command)
    {
        (string Response, Func<RiotSession, Task> Call) exchange = command switch
        {
            "TriggerEmergencyStop" => (SuccessBody, s => s.Device.TriggerEmergencyStopAsync(DeviceKey)),
            "CancelEmergencyStop" => (SuccessBody, s => s.Device.CancelEmergencyStopAsync(DeviceKey)),
            "CancelOrder" => (SuccessBody, s => s.Tasks.CancelOrderAsync(OrderId)),
            "CancelOrderWithReason" => (SuccessBody, s => s.Tasks.CancelOrderAsync(OrderId, "上位取消")),
            "OrderHold" => (SuccessBody, s => s.Tasks.OrderHoldAsync(OrderId)),
            "OrderContinue" => (SuccessBody, s => s.Tasks.OrderContinueAsync(OrderId)),
            "HangContinue" => (SuccessBody, s => s.Tasks.HangContinueAsync(OrderId, "operator retry")),
            "DispatchEnable" => (SuccessBody, s => s.Tasks.DispatchEnableAsync(DeviceKey)),
            "DispatchDisable" => (SuccessBody, s => s.Tasks.DispatchDisableAsync(DeviceKey)),
            "PriorityExec" => (SuccessBody, s => s.Order.PriorityExecAsync(OrderId)),
            "CreateMoveOrder" => (CreateOrderBody, s => s.Order.CreateMoveOrderAsync(UpperId, DeviceKey, 26, 211)),
            "CreateMoveOrderWithAction" => (CreateOrderBody, s => s.Order.CreateMoveOrderAsync(
                UpperId, DeviceKey, 26, 211, new OrderMissionAction(78, 1, 0))),
            "RouteCost" => (RouteCostBody, s => s.Tasks.GetRouteCostAsync(29, 1, DeviceKey)),
            "NearEnd" => (NearStationBody, s => s.Tasks.QueryNearestEndAsync(28, 1, [2, 3, 4])),
            "NearStart" => (NearStationBody, s => s.Tasks.QueryNearestStartAsync(28, 4, [1, 2, 3])),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };

        using var handler = new RecordingHandler(exchange.Response);
        await using (RiotSession session = CreateSession(handler))
        {
            await exchange.Call(session);
        }

        Assert.Single(handler.Requests);
        return handler.Requests[0];
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

    private sealed partial class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? "(no body)"
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Match messageId = MessageIdPattern().Match(body);
            if (messageId.Success)
            {
                Assert.Matches("^[0-9]{6}$", messageId.Groups["id"].Value);
                body = body.Replace(messageId.Value, "\"messageId\":\"######\"", StringComparison.Ordinal);
            }

            IEnumerable<string> headers = request.Headers
                .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .Select(header => $"{header.Key}: {string.Join(", ", header.Value)}\n")
                .Order(StringComparer.Ordinal);
            Requests.Add(
                $"{request.Method} {request.RequestUri!.PathAndQuery} HTTP/{request.Version} {request.VersionPolicy}\n"
                + $"{string.Concat(headers)}\n{body}");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }

        [GeneratedRegex("\"messageId\":\"(?<id>[^\"]*)\"")]
        private static partial Regex MessageIdPattern();
    }
}
