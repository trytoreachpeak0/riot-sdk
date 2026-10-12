using System.Net;
using System.Text;
using RIoT.Sdk.Core;
using RIoT.Sdk.Facade;

namespace RIoT.Sdk.Tests;

/// <summary>
/// riot-sdk#4: the six reads that go through <c>RiotWire.ReadAsync</c> must fail, time out and release their response
/// exactly as the generated <c>GetAsync</c> of the same endpoint does. Each case runs the facade read and the generated read
/// side by side and compares them, so the generated layer is the expectation rather than a hand-written one.
/// </summary>
/// <remarks>
/// control-server sorts RIoT read failures by exception type, and counts any <see cref="OperationCanceledException"/> as a
/// timeout (<c>RiotCallFailureClassification.IsTimeout</c>). A stalled body that hangs, or a timeout of another shape, would
/// leave it waiting or misfiled.
/// </remarks>
public sealed class RiotWireTransportTests
{
    /// <summary>The six RiotWire reads, each paired with the generated read of the same endpoint.</summary>
    public static TheoryData<string> Reads => new(ReadPairs.Keys);

    private static readonly Dictionary<string, (Func<RiotSession, Task> Facade, Func<RiotSession, Task> Generated)> ReadPairs =
        new()
        {
            ["edges"] = (
                s => s.Maps.ListEdgesAsync(25),
                s => s.Maps.Raw.Api.Imap.V1.MapInfo.Edges[25].GetAsync()),
            ["stations"] = (
                s => s.Maps.ListStationDetailsAsync(25),
                s => s.Maps.Raw.Api.Imap.V1.MapInfo.Stations[25].GetAsync()),
            ["removedEdge"] = (
                s => s.Maps.ListRemovedEdgesAsync(25),
                s => s.Maps.Raw.Api.Imap.V1.MapResource.RemovedEdge[25].GetAsync()),
            ["removedStation"] = (
                s => s.Maps.ListRemovedStationsAsync(25),
                s => s.Maps.Raw.Api.Imap.V1.MapResource.RemovedStation[25].GetAsync()),
            ["mapEdgeGroup/all"] = (
                s => s.Maps.ListEdgeGroupsAsync(),
                s => s.Maps.Raw.Api.Imap.V1.MapEdgeGroup.All.GetAsync()),
            ["route/"] = (
                s => s.Tasks.ReadDynamicRouteCostPresenceAsync(),
                s => s.Tasks.Raw.Api.Task.V1.Route.EmptyPathSegment.GetAsync()),
        };

    /// <summary>
    /// A body that stops after its headers fails on <c>HttpClient.Timeout</c>, as the generated read does. Before #4 the
    /// facade read the raw stream after <c>ResponseHeadersRead</c>, where that timeout no longer applies, and hung.
    /// </summary>
    [Theory]
    [MemberData(nameof(Reads))]
    public async Task A_stalled_body_times_out_as_the_generated_read_does(string read)
    {
        var (facadeRead, generatedRead) = ReadPairs[read];
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StalledContent() });
        await using RiotSession session = new(
            new RiotOptions { BaseUrl = "http://riot.test", CallApiKey = "test-key" },
            new HttpClient(handler) { BaseAddress = new Uri("http://riot.test/"), Timeout = TimeSpan.FromSeconds(1) });

        Exception generated = await ThrowsWithinGuardAsync(() => generatedRead(session));
        Exception facade = await ThrowsWithinGuardAsync(() => facadeRead(session));

        Assert.IsAssignableFrom<OperationCanceledException>(facade);
        Assert.Equal(generated.GetType(), facade.GetType());
        Assert.Equal(generated.Message, facade.Message);
    }

    /// <summary>
    /// The response is released whether the read fails or succeeds. Before #4 a 404 or 5xx left it undisposed, which with a
    /// real handler keeps the connection out of the pool until GC.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReadsByStatus))]
    public async Task The_response_is_released(string read, HttpStatusCode status)
    {
        var (facadeRead, _) = ReadPairs[read];
        List<TrackedContent> contents = [];
        RecordingHandler handler = new(_ =>
        {
            TrackedContent content = new("""{"code":"0","message":"成功","result":[]}""");
            contents.Add(content);
            return new HttpResponseMessage(status) { Content = content };
        });
        await using RiotSession session = CreateSession(handler);

        try
        {
            await facadeRead(session);
        }
        catch (Exception) when (status != HttpStatusCode.OK)
        {
        }

        TrackedContent only = Assert.Single(contents);
        Assert.True(only.Disposed);
    }

    public static TheoryData<string, HttpStatusCode> ReadsByStatus()
    {
        TheoryData<string, HttpStatusCode> data = new();
        foreach (string read in ReadPairs.Keys)
        {
            foreach (HttpStatusCode status in new[]
                     {
                         HttpStatusCode.OK, HttpStatusCode.NotFound,
                         HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable,
                     })
            {
                data.Add(read, status);
            }
        }

        return data;
    }

    /// <summary>Every failure comes out as the generated read throws it.</summary>
    [Theory]
    [MemberData(nameof(ReadsByFailure))]
    public async Task A_failure_throws_what_the_generated_read_throws(
        string read, HttpStatusCode status, string body, string mediaType)
    {
        var (facadeRead, generatedRead) = ReadPairs[read];
        RecordingHandler handler = new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        });
        await using RiotSession session = CreateSession(handler);

        Exception generated = await Assert.ThrowsAnyAsync<Exception>(() => generatedRead(session));
        Exception facade = await Assert.ThrowsAnyAsync<Exception>(() => facadeRead(session));

        Assert.Equal(generated.GetType(), facade.GetType());
    }

    public static TheoryData<string, HttpStatusCode, string, string> ReadsByFailure()
    {
        TheoryData<string, HttpStatusCode, string, string> data = new();
        foreach (string read in ReadPairs.Keys)
        {
            data.Add(read, HttpStatusCode.Unauthorized, "{}", "application/json");
            data.Add(read, HttpStatusCode.Forbidden, "{}", "application/json");
            data.Add(read, HttpStatusCode.NotFound, "{}", "application/json");
            data.Add(read, HttpStatusCode.InternalServerError, "{}", "application/json");
            data.Add(read, HttpStatusCode.ServiceUnavailable, "{}", "application/json");
            data.Add(read, HttpStatusCode.OK, "not json", "application/json");
            data.Add(read, HttpStatusCode.OK, "<html></html>", "text/html");
            data.Add(read, HttpStatusCode.OK, """{"code":"0","result":[]}""", "text/plain");
        }

        return data;
    }

    /// <summary>
    /// The body now reaches the readers through Kiota's untyped model and is written back out, so numbers must survive the
    /// round trip with their value and, for decimals, their scale. Each value below takes a different branch of that model:
    /// int, long, decimal, and beyond decimal's range float.
    /// </summary>
    [Fact]
    public async Task Numbers_survive_the_untyped_round_trip()
    {
        const string body =
            """
            {"code":"0","message":"成功","result":[
              {"id":2147483647,"s_node":-2,"e_node":1,"cost":3000000000,"sx":-18410,"sy":24890,"ex":0,"ey":40340,
               "s_facing":-1.578,"e_facing":1570.7963267948965,"direction":1,"is_back_edge":true,"type":1,"desc":"东"},
              {"id":3,"s_node":1,"e_node":2,"cost":1e30,"sx":0,"sy":0,"ex":0,"ey":0,"desc":null}
            ]}
            """;
        await using RiotSession session = CreateSession(new RecordingHandler(_ => Json(body)));

        IReadOnlyList<MapEdge> edges = await session.Maps.ListEdgesAsync(25);

        MapEdge first = edges[0];
        Assert.Equal(int.MaxValue, first.Id);
        Assert.Equal(-2, first.StartNode);
        Assert.Equal(3_000_000_000d, first.CostMm);
        Assert.Equal(-18410, first.StartX);
        Assert.Equal(-1.578, first.StartFacing);
        Assert.Equal(1570.7963267948965, first.EndFacing);
        Assert.True(first.IsBackEdge);
        Assert.Equal("东", first.Description);

        MapEdge second = edges[1];
        Assert.Equal(1e30, second.CostMm, 1e30 * 1e-6);
        Assert.Equal(string.Empty, second.Description);
    }

    /// <summary>
    /// A decimal keeps its scale: <c>128.0</c> is still not an int after the round trip, so a required int written that way
    /// fails closed as it did when the reader parsed the raw body.
    /// </summary>
    [Fact]
    public async Task A_decimal_with_a_zero_fraction_is_still_not_an_int()
    {
        const string body =
            """{"code":"0","message":"成功","result":[{"id":1.0,"s_node":1,"e_node":2,"cost":1,"sx":0,"sy":0,"ex":0,"ey":0}]}""";
        await using RiotSession session = CreateSession(new RecordingHandler(_ => Json(body)));

        RiotApiException error = await Assert.ThrowsAsync<RiotApiException>(() => session.Maps.ListEdgesAsync(25));

        Assert.Contains("id", error.Message);
    }

    /// <summary>
    /// Beyond float's range Kiota's untyped model holds infinity, which has no JSON spelling. The read fails closed as a
    /// RIoT failure rather than escaping as the writer's ArgumentException.
    /// </summary>
    [Fact]
    public async Task A_number_beyond_float_range_fails_closed()
    {
        const string body =
            """{"code":"0","message":"成功","result":[{"id":1,"s_node":1,"e_node":2,"cost":1e300,"sx":0,"sy":0,"ex":0,"ey":0}]}""";
        await using RiotSession session = CreateSession(new RecordingHandler(_ => Json(body)));

        RiotApiException error = await Assert.ThrowsAsync<RiotApiException>(() => session.Maps.ListEdgesAsync(25));

        Assert.Contains("beyond the range", error.Message);
    }

    private static RiotSession CreateSession(HttpMessageHandler handler) => new(
        new RiotOptions { BaseUrl = "http://riot.test", CallApiKey = "test-key" },
        new HttpClient(handler) { BaseAddress = new Uri("http://riot.test/") });

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static async Task<Exception> ThrowsWithinGuardAsync(Func<Task> read)
    {
        Task<Exception> thrown = Assert.ThrowsAnyAsync<Exception>(read);
        Task finished = await Task.WhenAny(thrown, Task.Delay(TimeSpan.FromSeconds(6)));
        Assert.True(ReferenceEquals(finished, thrown), "The read hung past the 6-second guard.");
        return await thrown;
    }

    /// <summary>A JSON body that sends its first bytes and then stops until the request is cancelled.</summary>
    private sealed class StalledContent : HttpContent
    {
        public StalledContent() => Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"code\":\"0\",\"result\":"), cancellationToken);
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

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
