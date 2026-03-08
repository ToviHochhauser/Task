using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;
using backend.Services;

namespace backend.Tests.Tests;

public class WorldTimeApiServiceTests
{
    public WorldTimeApiServiceTests()
    {
        WorldTimeApiService.ResetCache();
    }

    private static WorldTimeApiService CreateService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new WorldTimeApiService(httpClient, NullLogger<WorldTimeApiService>.Instance);
    }

    private static HttpMessageHandler MockHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var mock = new Mock<HttpMessageHandler>();
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) => respond(req));
        return mock.Object;
    }

    private static HttpMessageHandler MockHandlerAlwaysThrow(Exception ex)
    {
        var mock = new Mock<HttpMessageHandler>();
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(ex);
        return mock.Object;
    }

    // ── Test 1: WorldTimeAPI format (datetime field) ─────────────────────────

    [Fact]
    public async Task GetZurichTimeAsync_WorldTimeApiFormat_ParsesDatetimeField()
    {
        // WorldTimeAPI returns ISO-8601 with offset in "datetime"
        const string json = """{"datetime":"2026-03-07T10:30:00.123456+01:00","timezone":"Europe/Zurich"}""";

        var handler = MockHandler(_ => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(json)
        });
        var svc = CreateService(handler);

        var result = await svc.GetZurichTimeAsync();

        // DateTimeOffset.Parse strips the offset → local DateTime
        result.Should().Be(new DateTime(2026, 3, 7, 10, 30, 0, 123, DateTimeKind.Unspecified).AddMicroseconds(456));
    }

    // ── Test 2: TimeAPI.io format (dateTime field) ───────────────────────────

    [Fact]
    public async Task GetZurichTimeAsync_TimeApiIoFormat_ParsesDateTimeField()
    {
        const string json = """{"dateTime":"2026-03-07T10:30:00","timeZone":"Europe/Zurich"}""";

        var handler = MockHandler(_ => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(json)
        });
        var svc = CreateService(handler);

        var result = await svc.GetZurichTimeAsync();

        result.Should().Be(new DateTime(2026, 3, 7, 10, 30, 0));
    }

    // ── Test 3: First URL fails, fallback to second ──────────────────────────

    [Fact]
    public async Task GetZurichTimeAsync_FirstApiFails_FallsBackToSecondApi()
    {
        // First call (worldtimeapi.org) throws; second (timeapi.io) succeeds with TimeAPI.io format
        const string successJson = """{"dateTime":"2026-03-07T14:00:00","timeZone":"Europe/Zurich"}""";

        var callCount = 0;
        var handler = MockHandler(_ =>
        {
            callCount++;
            if (callCount == 1)
                throw new HttpRequestException("Connection refused");

            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(successJson)
            };
        });

        var svc = CreateService(handler);
        var result = await svc.GetZurichTimeAsync();

        result.Should().Be(new DateTime(2026, 3, 7, 14, 0, 0));
    }

    // ── Test 4: Both APIs fail → server-side TimeZoneInfo fallback ───────────

    [Fact]
    public async Task GetZurichTimeAsync_BothApisFail_FallsBackToServerTimezone()
    {
        var handler = MockHandlerAlwaysThrow(new HttpRequestException("Unreachable"));
        var svc = CreateService(handler);

        var before = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow.AddSeconds(-2),
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich"));

        var result = await svc.GetZurichTimeAsync();

        var after = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow.AddSeconds(2),
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich"));

        result.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ── Test 5: API returns unknown JSON shape → falls back to next URL ───────

    [Fact]
    public async Task GetZurichTimeAsync_UnknownJsonShape_FallsBackToNextUrl()
    {
        // First API returns unexpected JSON (no "datetime" / "dateTime" fields)
        // Second API returns valid TimeAPI.io format
        const string badJson = """{"time":"10:00:00","zone":"Zurich"}""";
        const string goodJson = """{"dateTime":"2026-03-07T10:00:00","timeZone":"Europe/Zurich"}""";

        var callCount = 0;
        var handler = MockHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(callCount == 1 ? badJson : goodJson)
            };
        });

        var svc = CreateService(handler);
        var result = await svc.GetZurichTimeAsync();

        result.Should().Be(new DateTime(2026, 3, 7, 10, 0, 0));
    }
}
