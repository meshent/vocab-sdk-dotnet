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
{ "Vocab": { "NetworkId": "example", "ClientId": "<from the Apps pane>", "ClientSecret": "<confidential apps only>" } }
```

Register `https://your-app/signin-vocab` as a redirect URI in your network's Apps pane on vo.cab. A user who signs
in without a profile in your network is offered one by auth.vo.cab before they come back to you (Public networks).

A runnable version is in [`samples/Vocab.Samples.Web`](samples/Vocab.Samples.Web).

## What the SDK takes care of

- Authorization code flow with PKCE (S256), which auth.vo.cab requires of every app.
- auth.vo.cab's JSON token endpoints, so an off-the-shelf OpenID Connect handler works through `AddSignIn`.
- Token refresh before expiry, inside the cookie session (one refresh however many requests race), and refresh-token revocation at sign-out.
- The `vo_*` profile claims on `User`, read with `User.GetVocab()`.
- The network API's error contract as `VocabApiException` with a machine-readable `Code`.
- Bearer validation on your own API that accepts only tokens minted for *your* app.

## Versioning

Versions are time based, like every vo.cab package: `0.0.<days since 2020-01-01>.<half-second of day>`, so a newer build is always a higher version. The two packages version independently and are published in dependency order, `Vocab.Sdk` first; `Vocab.Sdk.AspNetCore` depends on the newest `Vocab.Sdk` published alongside it. Every merge to `main` publishes release versions to nuget.org after a maintainer approves the deployment; a build from any other branch is a prerelease tagged with the branch name and is published only on demand.
Until 1.0 the packages ship as previews and the API may change between previews.

## Building

```bash
dotnet build
dotnet test
```

`Vocab.Sdk.AspNetCore` references `Vocab.Sdk` as a published package, so a plain build compiles it against the newest `Vocab.Sdk` on nuget.org. To build and test everything from the working tree instead (needed when you change `Vocab.Sdk` and the code that uses it together):

```bash
dotnet build -p:UseProjectReferences=true
dotnet test -p:UseProjectReferences=true
```

Never pack with that property; the build refuses, because the package would then depend on a `Vocab.Sdk` version that is never published.

## Licence

MIT. See [LICENSE](LICENSE).
