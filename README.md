# Vocab SDK for .NET

The recommended way for a network app to sign in with [vo.cab](https://vo.cab) and manage its users' profiles.
Three packages, one repository:

| Package | Use it for |
|---|---|
| `Vocab.Sdk` | The core client: token exchange and refresh against `auth.vo.cab`, and the network API over `https://{network}.api.vo.cab`. Works in any .NET app. |
| `Vocab.Sdk.AspNetCore` | Sign in with vo.cab for ASP.NET Core apps (`AddSignIn`), vo.cab bearer tokens on your own API (`AddBearer`), and a ready `VocabClient` (`AddClient`). |
| `Vocab.Sdk.Maui` | Sign in from a .NET MAUI app with a private-use redirect and a secure token store. Coming next. |

Documentation, quickstarts and the API reference live at **https://developers.vo.cab**.

## Quickstart (ASP.NET Core)

```bash
dotnet add package Vocab.Sdk.AspNetCore --prerelease
```

```csharp
builder.Services.AddVocab(builder.Configuration.GetSection("Vocab"))
    .AddSignIn()
    .AddClient();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/me", async (VocabClient vocab) => await vocab.WhoAmIAsync()).RequireAuthorization();
```

```json
{ "Vocab": { "NetworkId": "posili", "ClientId": "<from the Apps pane>", "ClientSecret": "<confidential apps only>" } }
```

Register `https://your-app/signin-vocab` as a redirect URI in your network's Apps pane on vo.cab. A user who signs
in without a profile in your network is offered one by auth.vo.cab before they come back to you (Public networks).

A runnable version is in [`samples/Vocab.Samples.Web`](samples/Vocab.Samples.Web).

## What the SDK takes care of

- Authorization code flow with PKCE (S256), which auth.vo.cab requires of every app.
- auth.vo.cab's JSON token endpoints, so an off-the-shelf OpenID Connect handler works through `AddSignIn`.
- Token refresh before expiry, inside the cookie session.
- The `mn_*` profile claims on `User`, read with `User.GetVocab()`.
- The network API's error contract as `VocabApiException` with a machine-readable `Code`.
- Bearer validation on your own API that accepts only tokens minted for *your* app.

## Versioning

Semantic versioning. Versions are set by hand in `Directory.Build.props`; a `v*` tag publishes to nuget.org.
Until 1.0 the packages ship as previews and the API may change between previews.

## Building

```bash
dotnet build
dotnet test
```

## Licence

MIT. See [LICENSE](LICENSE).
