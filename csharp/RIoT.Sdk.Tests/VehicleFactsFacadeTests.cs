using System.Net;
using System.Text;
using RIoT.Sdk.Core;
using RIoT.Sdk.Facade;

namespace RIoT.Sdk.Tests;

public sealed class VehicleFactsFacadeTests
{
    [Fact]
    public async Task GetVehicleCard_returns_exact_facts_for_matching_device_key()
    {
        RecordingHandler handler = new(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/task/vehicles/getVehicleInfoByDeviceKey", request.RequestUri?.AbsolutePath);
            Assert.Equal("?key=VEHICLE-1", request.RequestUri?.Query);
            return JsonResponse("""
                {"code":"0","result":{"deviceKey":"VEHICLE-1","enable":true,"status":1,
                "procState":"IDLE","currentMap":"MAP-25","currentPosition":12,"battery":80,
                "batteryState":"NO_CHARGE","speed":0,"lockStatus":0,"orderTaskId":null}}
                """);
        });
        await using RiotSession session = CreateSession(handler);

        VehicleCard card = await session.Tasks.GetVehicleCardAsync("VEHICLE-1");

        Assert.Equal("VEHICLE-1", card.DeviceKey);
        Assert.Equal(true, card.Enable);
        Assert.Equal(1, card.Status);
        Assert.Equal("IDLE", card.ProcState);
        Assert.Equal("MAP-25", card.CurrentMap);
        Assert.Equal(12, card.CurrentPosition);
        Assert.Equal(80, card.BatteryPercent);
        Assert.Equal("NO_CHARGE", card.BatteryState);
        Assert.Equal(0d, card.Speed);
        Assert.Equal(0, card.LockStatus);
        Assert.Null(card.OrderTaskId);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetVehicleCard_rejects_mismatched_device_key()
    {
        RecordingHandler handler = new(_ => JsonResponse(
            """{"code":"0","result":{"deviceKey":"OTHER","enable":true,"status":1}}"""));
        await using RiotSession session = CreateSession(handler);

        RiotApiException error = await Assert.ThrowsAsync<RiotApiException>(
            () => session.Tasks.GetVehicleCardAsync("VEHICLE-1"));

        Assert.Equal("vehicle-key-mismatch", error.BusinessCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetVehicleCard_rejects_missing_result()
    {
        RecordingHandler handler = new(_ => JsonResponse("""{"code":"0","result":null}"""));
        await using RiotSession session = CreateSession(handler);

        RiotApiException error = await Assert.ThrowsAsync<RiotApiException>(
            () => session.Tasks.GetVehicleCardAsync("VEHICLE-1"));

        Assert.Equal("vehicle-card-missing", error.BusinessCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetVehicleExecutionFacts_returns_vehicle_and_task_facts()
    {
        RecordingHandler handler = new(request =>
        {
            Assert.Equal("/api/task/v1/task/getVehicleInfo/VEHICLE-1", request.RequestUri?.AbsolutePath);
            return JsonResponse("""
                {
                  "vehicle":{"movementState":"MT_FINISHED","controlState":"CONTROL_STATE_OK",
                    "emergencyState":"OK","breakSwitchState":"MOVABLE",
                    "locationState":"LOCATION_STATE_RUNNING","speed":0,"mode":"MODE_MANUAL"},
                  "vehicleTaskInfo":{"key":"VEHICLE-1","procState":"IDLE",
                    "processingOrder":false,"enable":true,"integrationLevel":"ON_LINE"}
                }
                """);
        });
        await using RiotSession session = CreateSession(handler);

        VehicleExecutionFacts facts = await session.Tasks.GetVehicleExecutionFactsAsync("VEHICLE-1");

        Assert.Equal("VEHICLE-1", facts.DeviceKey);
        Assert.Equal("MT_FINISHED", facts.MovementState);
        Assert.Equal("CONTROL_STATE_OK", facts.ControlState);
        Assert.Equal("OK", facts.EmergencyState);
        Assert.Equal("MOVABLE", facts.BreakSwitchState);
        Assert.Equal("LOCATION_STATE_RUNNING", facts.LocationState);
        Assert.Equal(0d, facts.Speed);
        Assert.Equal("IDLE", facts.ProcState);
        Assert.Equal(false, facts.ProcessingOrder);
        Assert.Equal(true, facts.Enable);
        Assert.Equal("ON_LINE", facts.IntegrationLevel);
        Assert.Equal("MODE_MANUAL", facts.Mode);
        Assert.Equal(1, handler.CallCount);
    }

    /// <summary>
    /// control-server#639: a consumer proves standstill by requiring MODE_AUTO, so a mode RIoT left out or sent as a value the
    /// generated enum does not know must reach it as null, never as a default that reads like a real mode.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(",\"mode\":null")]
    [InlineData(",\"mode\":\"MODE_REMOTE\"")]
    public async Task GetVehicleExecutionFacts_reports_absent_or_unknown_mode_as_null(string modeMember)
    {
        RecordingHandler handler = new(_ => JsonResponse(
            "{\"vehicle\":{\"movementState\":\"MT_NA\"" + modeMember + "}," +
            "\"vehicleTaskInfo\":{\"key\":\"VEHICLE-1\",\"procState\":\"IDLE\"}}"));
        await using RiotSession session = CreateSession(handler);

        VehicleExecutionFacts facts = await session.Tasks.GetVehicleExecutionFactsAsync("VEHICLE-1");

        Assert.Equal("MT_NA", facts.MovementState);
        Assert.Null(facts.Mode);
    }

    /// <summary>
    /// riot-sdk#3 review M1: MODE_AUTO itself, off a real response. The body is behaviour lab Round 6
    /// <c>B2-getVehicleInfo-full.json</c> trimmed (identity replaced, the large nested objects dropped), without the UTF-8 BOM
    /// the lab file carries (that came from writing the file). control-server#639 proves standstill on this value, so
    /// projecting it wrongly would stop every vehicle.
    /// </summary>
    [Fact]
    public async Task GetVehicleExecutionFacts_reads_mode_auto_off_a_real_response()
    {
        RecordingHandler handler = new(_ => JsonResponse(Round6VehicleInfo));
        await using RiotSession session = CreateSession(handler);

        VehicleExecutionFacts facts = await session.Tasks.GetVehicleExecutionFactsAsync("VEHICLE-1");

        Assert.Equal("MODE_AUTO", facts.Mode);
        Assert.Equal("MT_FINISHED", facts.MovementState);
        Assert.Equal("MOVABLE", facts.BreakSwitchState);
        Assert.Equal(0d, facts.Speed);
        Assert.Equal("OFF_LINE", facts.IntegrationLevel);
        Assert.Equal(1, handler.CallCount);
    }

    /// <summary>
    /// riot-sdk#3: the facade replaces only the parse of the generated read, never the request, so every failure must come out
    /// as the generated <c>GetAsync</c> throws it -- control-server sorts read failures by exception type. Each failure is sent
    /// to both and the types compared.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{}", "application/json")]
    [InlineData(HttpStatusCode.Forbidden, "{}", "application/json")]
    [InlineData(HttpStatusCode.NotFound, "{}", "application/json")]
    [InlineData(HttpStatusCode.InternalServerError, "{}", "application/json")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{}", "application/json")]
    [InlineData(HttpStatusCode.OK, "not json", "application/json")]
    [InlineData(HttpStatusCode.OK, "<html></html>", "text/html")]
    [InlineData(HttpStatusCode.OK, "{\"vehicle\":{}}", "text/plain")]
    public async Task GetVehicleExecutionFacts_fails_as_the_generated_read_did(
        HttpStatusCode status, string body, string mediaType)
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        });
        await using RiotSession session = CreateSession(handler);

        Exception generated = await Assert.ThrowsAnyAsync<Exception>(
            () => session.Tasks.Raw.Api.Task.V1.Task.GetVehicleInfo["VEHICLE-1"].GetAsync());
        Exception facade = await Assert.ThrowsAnyAsync<Exception>(
            () => session.Tasks.GetVehicleExecutionFactsAsync("VEHICLE-1"));

        Assert.Equal(generated.GetType(), facade.GetType());
    }

    /// <summary>
    /// riot-sdk#3 second review M1: a body that stops after its headers must fail on <c>HttpClient.Timeout</c>, as the generated
    /// read does. b35c653 read the raw stream after <c>ResponseHeadersRead</c>, where that timeout no longer applies, and hung.
    /// Both reads are given the same stalled body and must throw the same OperationCanceledException well inside the guard.
    /// </summary>
    [Fact]
    public async Task GetVehicleExecutionFacts_times_out_on_a_stalled_body_as_the_generated_read_does()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StalledContent() });
        await using RiotSession session = new(
            new RiotOptions { BaseUrl = "http://riot.test", CallApiKey = "test-key" },
            new HttpClient(handler) { BaseAddress = new Uri("http://riot.test/"), Timeout = TimeSpan.FromSeconds(1) });

        Exception generated = await ThrowsWithinGuardAsync(
            () => session.Tasks.Raw.Api.Task.V1.Task.GetVehicleInfo["VEHICLE-1"].GetAsync());
        Exception facade = await ThrowsWithinGuardAsync(
            () => session.Tasks.GetVehicleExecutionFactsAsync("VEHICLE-1"));

        Assert.IsAssignableFrom<OperationCanceledException>(facade);
        Assert.Equal(generated.GetType(), facade.GetType());
        Assert.Equal(generated.Message, facade.Message);
    }

    /// <summary>
    /// riot-sdk#3 second review S1: a failed response is released, as the generated read releases it. b35c653 left the content
    /// undisposed on 404 and 500, which with a real handler keeps the connection out of the pool until GC.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetVehicleExecutionFacts_releases_a_failed_response(HttpStatusCode status)
    {
        List<TrackedContent> contents = [];
        RecordingHandler handler = new(_ =>
        {
            TrackedContent content = new("{}");
            contents.Add(content);
            return new HttpResponseMessage(status) { Content = content };
        });
        await using RiotSession session = CreateSession(handler);

        await Assert.ThrowsAnyAsync<Exception>(() => session.Tasks.GetVehicleExecutionFactsAsync("VEHICLE-1"));

        TrackedContent only = Assert.Single(contents);
        Assert.True(only.Disposed);
    }

    /// <summary>
    /// A mode that is not a JSON string is no mode. The generated enum read threw on these, which control-server reports as
    /// RIOT_READ_FAILED; the raw read gives null, which it reports as RIOT_MODE_NOT_AUTO. Both keep the vehicle unknown.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[\"MODE_AUTO\"]")]
    public async Task GetVehicleExecutionFacts_reads_a_mode_that_is_not_a_string_as_null(string modeJson)
    {
        RecordingHandler handler = new(_ => JsonResponse(
            "{\"vehicle\":{\"movementState\":\"MT_NA\",\"mode\":" + modeJson + "}," +
            "\"vehicleTaskInfo\":{\"key\":\"VEHICLE-1\",\"procState\":\"IDLE\"}}"));
        await using RiotSession session = CreateSession(handler);

        VehicleExecutionFacts facts = await session.Tasks.GetVehicleExecutionFactsAsync("VEHICLE-1");

        Assert.Equal("MT_NA", facts.MovementState);
        Assert.Null(facts.Mode);
    }

    /// <summary>The three declared values pass through exactly as RIoT spells them.</summary>
    [Theory]
    [InlineData("MODE_AUTO")]
    [InlineData("MODE_MANUAL")]
    [InlineData("UNKNOWN")]
    public async Task GetVehicleExecutionFacts_passes_each_declared_mode_through(string mode)
    {
        RecordingHandler handler = new(_ => JsonResponse(
            "{\"vehicle\":{\"movementState\":\"MT_NA\",\"mode\":\"" + mode + "\"}," +
            "\"vehicleTaskInfo\":{\"key\":\"VEHICLE-1\",\"procState\":\"IDLE\"}}"));
        await using RiotSession session = CreateSession(handler);

        VehicleExecutionFacts facts = await session.Tasks.GetVehicleExecutionFactsAsync("VEHICLE-1");

        Assert.Equal(mode, facts.Mode);
    }

    /// <summary>
    /// riot-sdk#3 review S1: a value not spelled exactly as declared is no mode. The generated enum reader took
    /// <c>"0"</c>, <c>" 0"</c>, <c>"mode_auto"</c> and <c>" MODE_AUTO"</c> as MODE_AUTO, <c>"3"</c> and <c>"-1"</c> as
    /// themselves and <c>"MODE_AUTO,MODE_MANUAL"</c> as MODE_MANUAL; Python gives None for every one of them, and so does C#
    /// now. The same table is in python/tests/test_vehicle_facts_facade.py.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData(" 0")]
    [InlineData("mode_auto")]
    [InlineData(" MODE_AUTO")]
    [InlineData("MODE_AUTO ")]
    [InlineData("Mode_Auto")]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("MODE_AUTO,MODE_MANUAL")]
    [InlineData("mODE_AUTO")]
    [InlineData("mODE_MANUAL")]
    [InlineData("uNKNOWN")]
    [InlineData("")]
    public async Task GetVehicleExecutionFacts_reads_a_mode_not_spelled_as_declared_as_null(string mode)
    {
        RecordingHandler handler = new(_ => JsonResponse(
            "{\"vehicle\":{\"movementState\":\"MT_NA\",\"mode\":\"" + mode + "\"}," +
            "\"vehicleTaskInfo\":{\"key\":\"VEHICLE-1\",\"procState\":\"IDLE\"}}"));
        await using RiotSession session = CreateSession(handler);

        VehicleExecutionFacts facts = await session.Tasks.GetVehicleExecutionFactsAsync("VEHICLE-1");

        Assert.Equal("MT_NA", facts.MovementState);
        Assert.Null(facts.Mode);
    }

    [Theory]
    [InlineData("{\"vehicleTaskInfo\":{\"key\":\"VEHICLE-1\"}}")]
    [InlineData("{\"vehicle\":{\"movementState\":\"MT_FINISHED\"}}")]
    public async Task GetVehicleExecutionFacts_rejects_missing_vehicle_or_task_half(string body)
    {
        RecordingHandler handler = new(_ => JsonResponse(body));
        await using RiotSession session = CreateSession(handler);

        RiotApiException error = await Assert.ThrowsAsync<RiotApiException>(
            () => session.Tasks.GetVehicleExecutionFactsAsync("VEHICLE-1"));

        Assert.Equal("vehicle-execution-facts-missing", error.BusinessCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetVehicleExecutionFacts_rejects_mismatched_task_key()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            {"vehicle":{"movementState":"MT_FINISHED"},
             "vehicleTaskInfo":{"key":"OTHER","procState":"IDLE"}}
            """));
        await using RiotSession session = CreateSession(handler);

        RiotApiException error = await Assert.ThrowsAsync<RiotApiException>(
            () => session.Tasks.GetVehicleExecutionFactsAsync("VEHICLE-1"));

        Assert.Equal("vehicle-key-mismatch", error.BusinessCode);
        Assert.Equal(1, handler.CallCount);
    }

    private static RiotSession CreateSession(HttpMessageHandler handler) => new(
        new RiotOptions { BaseUrl = "http://riot.test", CallApiKey = "test-key" },
        new HttpClient(handler) { BaseAddress = new Uri("http://riot.test/") });

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    /// <summary>Awaits <paramref name="read"/> and returns what it threw; fails if it neither throws nor ends within 6 seconds.</summary>
    private static async Task<Exception> ThrowsWithinGuardAsync(Func<Task> read)
    {
        Task<Exception> thrown = Assert.ThrowsAnyAsync<Exception>(read);
        Task finished = await Task.WhenAny(thrown, Task.Delay(TimeSpan.FromSeconds(6)));
        Assert.True(ReferenceEquals(finished, thrown), "The read hung past the 6-second guard.");
        return await thrown;
    }

    /// <summary>A JSON body that sends its first 20 bytes and then stops until the request is cancelled.</summary>
    private sealed class StalledContent : HttpContent
    {
        public StalledContent() => Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream, System.Net.TransportContext? context, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"vehicle\":{\"mode\":"), cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>A JSON body that records whether it was disposed.</summary>
    private sealed class TrackedContent(string body) : StringContent(body, Encoding.UTF8, "application/json")
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>Behaviour lab Round 6 <c>B2-getVehicleInfo-full.json</c>, trimmed; see the test that uses it.</summary>
    private const string Round6VehicleInfo = "{\"vehicle\":{\"actionState\":\"AT_NA\",\"actionTaskNo\":0,\"agvInfoState\":\"NORMAL\",\"aliveState\":true,\"battery\":35,\"batteryState\":\"NO_CHARGE\",\"breakSwitchState\":\"MOVABLE\",\"confidence\":92,\"connected\":true,\"connectedTimestamp\":1784507855064,\"controlState\":\"CONTROL_STATE_OK\",\"curCheckPointNo\":0,\"curPathNo\":0,\"currentNode\":1,\"currentStation\":1,\"emergencyState\":\"OK\",\"faultCodes\":\"11322,11324,212416,211416\",\"faultCodesList\":[],\"firstReportData\":true,\"fleetMode\":\"FLEET_MODE_OFFLINE\",\"freshState\":1,\"hardWareErrorCode\":0,\"hardwareState\":\"H_STATE_OK\",\"key\":\"VEHICLE-1\",\"lastErrorCode\":0,\"lastReportTime\":1784514379092,\"length\":925,\"loadHandlingDevices\":[],\"loadState\":0,\"locationState\":\"LOCATION_STATE_RUNNING\",\"mode\":\"MODE_AUTO\",\"motorExceptionState\":\"NORMAL\",\"moveTaskNo\":2,\"movementState\":\"MT_FINISHED\",\"noNode\":false,\"noStation\":false,\"orientationAngle\":-1.578,\"originalLength\":925,\"originalWidth\":622,\"paused\":false,\"powerMode\":\"POWER_MODE_FULLER\",\"precisePosition\":{\"x\":128.0,\"y\":-539.0},\"productKey\":\"standard.oasis.300ul\",\"routingGroup\":\"EXECUTE_FREE\",\"running\":false,\"speed\":0.0,\"startPauseTime\":0,\"state\":\"IDLE\",\"status\":1,\"vehicleId\":40,\"width\":622},\"vehicleTaskInfo\":{\"batteryMaintenanceTime\":1762328282203,\"enable\":false,\"handlePauseTime\":0,\"integrationLevel\":\"OFF_LINE\",\"integrationLevelTime\":0,\"key\":\"VEHICLE-1\",\"lastExecuteOrderTimestamp\":0,\"procState\":\"IDLE\",\"processingOrder\":false,\"routeProgressIndex\":-1}}";

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(responseFactory(request));
        }
    }
}
