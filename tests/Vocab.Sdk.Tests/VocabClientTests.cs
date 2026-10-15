using System.Net;
using System.Text.Json;
using Xunit;

namespace Vocab.Sdk.Tests;

public class VocabClientTests
{
    private static VocabClient ClientOver(FakeApi api, string token = "tok") =>
        new(api.Client(), new StaticTokenProvider(token));

    [Fact]
    public async Task WhoAmI_sends_the_bearer_token_to_v1_whoami_and_reads_the_answer()
    {
        var api = new FakeApi().Answer(HttpStatusCode.OK,
            """{"profileId":"p1","networkId":"example","clientId":"app","scopes":["vo.profile.basic","openid"]}""");

        var me = await ClientOver(api, "secret-token").WhoAmIAsync();

        var (request, _) = Assert.Single(api.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://example.api.vo.cab/v1/whoami", request.RequestUri!.ToString());
        Assert.Equal("secret-token", FakeApi.BearerOf(request));
        Assert.Equal("p1", me.ProfileId);
        Assert.Equal("example", me.NetworkId);
        Assert.Equal(["vo.profile.basic", "openid"], me.Scopes);
    }

    [Fact]
    public async Task An_error_body_becomes_a_VocabApiException_carrying_the_code()
    {
        var api = new FakeApi().Answer(HttpStatusCode.NotFound,
            """{"error":"You have no profile in this network.","code":"no_profile"}""");

        var ex = await Assert.ThrowsAsync<VocabApiException>(() => ClientOver(api).WhoAmIAsync());

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal(VocabErrorCodes.NoProfile, ex.Code);
        Assert.True(ex.IsNoProfile);
        Assert.Equal("You have no profile in this network.", ex.Message);
    }

    [Fact]
    public async Task A_401_without_a_body_is_still_an_invalid_token_failure()
    {
        var api = new FakeApi().Answer(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<VocabApiException>(() => ClientOver(api).WhoAmIAsync());

        Assert.Equal(VocabErrorCodes.InvalidToken, ex.Code);
        Assert.True(ex.IsTokenProblem);
    }

    [Fact]
    public async Task A_429_carries_the_retry_after_wait()
    {
        var api = new FakeApi().Answer(HttpStatusCode.TooManyRequests,
            """{"error":"Too many avatar uploads for this profile. Try again later.","code":"rate_limited"}""",
            response => response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90)));

        var ex = await Assert.ThrowsAsync<VocabApiException>(() => ClientOver(api).AddEmailAsync("a@b.example"));

        Assert.Equal(VocabErrorCodes.RateLimited, ex.Code);
        Assert.Equal(TimeSpan.FromSeconds(90), ex.RetryAfter);
    }

    [Fact]
    public async Task External_id_round_trip_and_not_found_as_null()
    {
        var api = new FakeApi()
            .Answer(HttpStatusCode.OK, """{"profileId":"p1","externalId":"user-42"}""")
            .Answer(HttpStatusCode.NotFound, """{"error":"This app has recorded no external id for your profile in this network.","code":"not_found"}""")
            .Answer(HttpStatusCode.NoContent)
            .Answer(HttpStatusCode.NotFound, """{"error":"none","code":"not_found"}""");
        var client = ClientOver(api);

        var link = await client.SetExternalIdAsync("user-42");
        Assert.Equal("user-42", link.ExternalId);
        Assert.Equal("""{"externalId":"user-42"}""", api.Requests[0].Body);
        Assert.Equal(HttpMethod.Put, api.Requests[0].Request.Method);

        Assert.Null(await client.GetExternalIdAsync());
        Assert.True(await client.ClearExternalIdAsync());
        Assert.False(await client.ClearExternalIdAsync());
        Assert.Equal(HttpMethod.Delete, api.Requests[2].Request.Method);
    }

    [Fact]
    public async Task A_no_profile_404_on_the_external_id_read_is_not_swallowed_as_null()
    {
        var api = new FakeApi().Answer(HttpStatusCode.NotFound, """{"error":"You have no profile in this network.","code":"no_profile"}""");

        var ex = await Assert.ThrowsAsync<VocabApiException>(() => ClientOver(api).GetExternalIdAsync());

        Assert.Equal(VocabErrorCodes.NoProfile, ex.Code);
    }

    [Fact]
    public async Task UpdateProfile_patches_only_the_fields_that_were_set()
    {
        var api = new FakeApi().Answer(HttpStatusCode.NoContent);

        await ClientOver(api).UpdateProfileAsync(new ProfileUpdate { DisplayName = "Jane", Bio = "" });

        var (request, body) = Assert.Single(api.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("https://example.api.vo.cab/v1/me/profile", request.RequestUri!.ToString());
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(body!)!;
        Assert.Equal(new Dictionary<string, string> { ["displayName"] = "Jane", ["bio"] = "" }, fields);
    }

    [Fact]
    public async Task UpdateProfile_with_nothing_set_is_refused_before_any_request()
    {
        var api = new FakeApi();

        await Assert.ThrowsAsync<ArgumentException>(() => ClientOver(api).UpdateProfileAsync(new ProfileUpdate()));

        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task SetHandle_echoes_the_stored_handle_and_maps_409()
    {
        var api = new FakeApi()
            .Answer(HttpStatusCode.OK, """{"handle":"jane_doe"}""")
            .Answer(HttpStatusCode.Conflict, """{"error":"That handle is not available.","code":"handle_unavailable"}""");
        var client = ClientOver(api);

        Assert.Equal("jane_doe", await client.SetHandleAsync("Jane_Doe"));
        Assert.Equal("""{"handle":"Jane_Doe"}""", api.Requests[0].Body);

        var ex = await Assert.ThrowsAsync<VocabApiException>(() => client.SetHandleAsync("taken"));
        Assert.Equal(VocabErrorCodes.HandleUnavailable, ex.Code);
    }

    [Fact]
    public async Task UploadAvatar_posts_one_multipart_part_named_file()
    {
        var api = new FakeApi().Answer(HttpStatusCode.OK, """{"avatar":"media:abc","avatarUrl":"https://cdn.vo.cab/a.webp"}""");
        using var image = new MemoryStream([1, 2, 3]);

        var upload = await ClientOver(api).UploadAvatarAsync(image, "image/png", "me.png");

        var (request, body) = Assert.Single(api.Requests);
        Assert.Equal("multipart/form-data", FakeApi.ContentTypeOf(request)!.MediaType);
        Assert.Contains("name=file", body);
        Assert.Contains("filename=me.png", body);
        Assert.Contains("Content-Type: image/png", body);
        Assert.Equal("https://cdn.vo.cab/a.webp", upload.AvatarUrl);
    }

    [Fact]
    public async Task AddEmail_reads_the_202_pending_answer()
    {
        var api = new FakeApi().Answer(HttpStatusCode.Accepted, """{"status":"verification_required","email":"jane@example.com"}""");

        var addition = await ClientOver(api).AddEmailAsync("jane@example.com");

        Assert.Equal("verification_required", addition.Status);
        Assert.Equal("""{"email":"jane@example.com"}""", api.Requests[0].Body);
    }

    [Fact]
    public async Task Linked_profiles_are_read_as_profiles()
    {
        var api = new FakeApi().Answer(HttpStatusCode.OK,
            """{"profiles":[{"vo_profile_id":"p2","vo_network_id":"example","vo_handle":"bob","name":"Bob","vo_avatar_url":null}]}""");

        var linked = await ClientOver(api).ListLinkedProfilesAsync();

        var bob = Assert.Single(linked);
        Assert.Equal("p2", bob.ProfileId);
        Assert.Equal("bob", bob.Handle);
        Assert.Equal("Bob", bob.DisplayName);
        Assert.Null(bob.Email);
    }

    [Fact]
    public async Task Without_a_token_the_call_goes_out_anonymously_and_the_401_is_reported()
    {
        var api = new FakeApi().Answer(HttpStatusCode.Unauthorized, """{"error":"The access token is missing.","code":"invalid_token"}""");
        var client = new VocabClient(api.Client(), new NoToken());

        var ex = await Assert.ThrowsAsync<VocabApiException>(() => client.WhoAmIAsync());

        Assert.Null(FakeApi.BearerOf(api.Requests[0].Request));
        Assert.True(ex.IsTokenProblem);
    }

    private sealed class NoToken : IVocabTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => new((string?)null);
    }
}
