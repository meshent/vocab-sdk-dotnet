using System.Net;
using Xunit;

namespace Vocab.Sdk.AspNetCore.Tests;

public class SessionRefresherTests
{
    private static readonly VocabOptions Options = new() { NetworkId = "example", ClientId = "app", Authority = TestApp.Authority };

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Callers_in_flight_share_one_request_and_a_retained_result_is_reused_until_it_ages_out()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var authority = new FakeAuthority().On(HttpMethod.Post, "/auth/refresh", async (_, _) =>
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return FakeAuthority.Json(HttpStatusCode.OK, """{"access_token":"a2","token_type":"Bearer","expires_in":3600,"refresh_token":"r2"}""");
        });
        var auth = new VocabAuthClient(new HttpClient(authority) { BaseAddress = TestApp.Authority }, Options);
        var clock = new Clock();
        var refresher = new VocabSessionRefresher(clock);

        var first = refresher.RefreshAsync("r1", auth);
        var second = refresher.RefreshAsync("r1", auth);
        Assert.False(first.IsCompleted);
        Assert.Equal(1, calls);

        gate.SetResult();
        var tokens = await Task.WhenAll(first, second);
        Assert.All(tokens, t => Assert.Equal("a2", t.AccessToken));
        Assert.Equal(1, calls);

        clock.Now += TimeSpan.FromSeconds(30);
        Assert.Equal("a2", (await refresher.RefreshAsync("r1", auth)).AccessToken);
        Assert.Equal(1, calls);

        clock.Now += VocabSessionRefresher.RetainFor;
        await refresher.RefreshAsync("r1", auth);
        Assert.Equal(2, calls);
        Assert.Equal(1, refresher.Count);
    }

    [Fact]
    public async Task A_failed_refresh_is_shared_with_the_callers_in_flight_and_then_forgotten()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authority = new FakeAuthority().On(HttpMethod.Post, "/auth/refresh", async (_, _) =>
        {
            await gate.Task;
            return FakeAuthority.Json(HttpStatusCode.BadRequest, """{"error":"gone","code":"invalid_grant"}""");
        });
        var auth = new VocabAuthClient(new HttpClient(authority) { BaseAddress = TestApp.Authority }, Options);
        var refresher = new VocabSessionRefresher(new Clock());

        var first = refresher.RefreshAsync("r1", auth);
        var second = refresher.RefreshAsync("r1", auth);
        Assert.Equal(1, refresher.Count);
        gate.SetResult();
        var one = await Assert.ThrowsAsync<VocabApiException>(() => first);
        var two = await Assert.ThrowsAsync<VocabApiException>(() => second);

        Assert.Equal(VocabErrorCodes.InvalidGrant, one.Code);
        Assert.Same(one, two);
        Assert.Equal(0, refresher.Count);
    }
}
