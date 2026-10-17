using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using RIoT.Sdk.Core;
using RIoT.Sdk.Facade;

namespace RIoT.Sdk.Tests;

/// <summary>
/// Seam: the exact HTTP request every facade write command puts on the wire -- method, path and query, Content-Type,
/// Accept and body, byte for byte.
/// </summary>
/// <remarks>
/// The expected requests were recorded with Microsoft.Kiota.Bundle 1.17.4 (riot-sdk 0.3.0) and must hold unchanged on
/// later Kiota versions: Kiota 1.21.2 changed how request bodies are serialized (recyclable streams), and these commands
/// include the emergency stop, whose body RIoT rejects with an NPE when <c>thingsProperties</c> is missing
/// (simple#48). CreateMoveOrderAsync is pinned byte for byte in <see cref="CreateMoveOrderWithActionFacadeTests"/>.
/// The emergency-stop <c>messageId</c> is random, so it is checked for shape and then masked.
/// </remarks>
public partial class WriteCommandWireTests
{
    private const string DeviceKey = "BROKERX-aee2f93d717546cf9510c98c854fe83e";
    private const string OrderId = "order-2079374239101747200";

    private const string SuccessBody = """{"code":"0","message":"成功"}""";

    public static TheoryData<string, string> Commands => new()
    {
        {
            "TriggerEmergencyStop",
            """
            POST /api/device/v1/command/sync/service/BROKERX-aee2f93d717546cf9510c98c854fe83e/triggerEmergency
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
            POST /api/device/v1/command/sync/service/BROKERX-aee2f93d717546cf9510c98c854fe83e/cancelEmergency
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
            POST /api/task/v1/order/command/order-2079374239101747200
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
            POST /api/task/v1/order/command/order-2079374239101747200
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
            POST /api/task/v1/order/command/order-2079374239101747200
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
            POST /api/task/v1/order/command/order-2079374239101747200
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
            POST /api/task/v1/order/command/order-2079374239101747200
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
            POST /api/task/vehicles/updateVehicleIntegrationLevel
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
            POST /api/task/vehicles/updateVehicleIntegrationLevel
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
            POST /api/order/v1/orderRecordPriorityExec?orderTaskKey=order-2079374239101747200
            Accept: application/json
            Authorization: Bearer test-call-api-key

            (no body)
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
        using var handler = new RecordingHandler();
        await using (RiotSession session = CreateSession(handler))
        {
            Task call = command switch
            {
                "TriggerEmergencyStop" => session.Device.TriggerEmergencyStopAsync(DeviceKey),
                "CancelEmergencyStop" => session.Device.CancelEmergencyStopAsync(DeviceKey),
                "CancelOrder" => session.Tasks.CancelOrderAsync(OrderId),
                "CancelOrderWithReason" => session.Tasks.CancelOrderAsync(OrderId, "上位取消"),
                "OrderHold" => session.Tasks.OrderHoldAsync(OrderId),
                "OrderContinue" => session.Tasks.OrderContinueAsync(OrderId),
                "HangContinue" => session.Tasks.HangContinueAsync(OrderId, "operator retry"),
                "DispatchEnable" => session.Tasks.DispatchEnableAsync(DeviceKey),
                "DispatchDisable" => session.Tasks.DispatchDisableAsync(DeviceKey),
                "PriorityExec" => session.Order.PriorityExecAsync(OrderId),
                _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
            };
            await call;
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

    private sealed partial class RecordingHandler : HttpMessageHandler
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
            Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery}\n{string.Concat(headers)}\n{body}");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessBody, Encoding.UTF8, "application/json"),
            };
        }

        [GeneratedRegex("\"messageId\":\"(?<id>[^\"]*)\"")]
        private static partial Regex MessageIdPattern();
    }
}
