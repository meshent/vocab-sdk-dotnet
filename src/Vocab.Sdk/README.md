# Vocab.Sdk

The core client for vo.cab network apps. It speaks to two hosts:

- `https://auth.vo.cab`: redeem an authorization code, refresh and revoke tokens, read the signed-in user's profile claims (`VocabAuthClient`).
- `https://{network}.api.vo.cab/v1`: the network API for the signed-in user's profile in your network (`VocabClient`).

Most apps should start from a platform package instead and let it wire this one up:

- ASP.NET Core: `Vocab.Sdk.AspNetCore`
- .NET MAUI: `Vocab.Sdk.Maui`

```csharp
var options = new VocabOptions { NetworkId = "example", ClientId = "<client id>" };
var http = new HttpClient { BaseAddress = options.ApiBaseAddress };
var client = new VocabClient(http, new StaticTokenProvider(accessToken));

var me = await client.WhoAmIAsync();
await client.SetExternalIdAsync("user-42");
```

Every error the API returns surfaces as a `VocabApiException` carrying the HTTP status and the API's error `code`
(`invalid_token`, `insufficient_scope`, `no_profile`, `rate_limited`, …). Documentation and guides: https://developers.vo.cab.
