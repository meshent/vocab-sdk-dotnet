# Vocab.Sdk.AspNetCore

Sign in with vo.cab, in three lines.

```csharp
builder.Services.AddVocab(builder.Configuration.GetSection("Vocab"))
    .AddSignIn()      // cookie session + OpenID Connect (PKCE) against auth.vo.cab
    .AddClient();     // VocabClient for https://{network}.api.vo.cab, tokens handled for you

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/me", async (VocabClient vocab) => await vocab.WhoAmIAsync()).RequireAuthorization();
```

```json
{ "Vocab": { "NetworkId": "example", "ClientId": "<from the Apps pane>", "ClientSecret": "<confidential apps only>" } }
```

Register `https://your-app/signin-vocab` as the redirect URI in your network's Apps pane.

Sign out with `SignOutAsync()` (no scheme): the session cookie is cleared and its refresh token revoked at auth.vo.cab.
Never sign out of the OpenID Connect scheme itself; auth.vo.cab has no end-session endpoint, and the user stays
signed in to vo.cab, as with any "sign in with" provider. Concurrent requests near token expiry share one refresh within a process; a multi-instance deployment
without sticky sessions should keep `RefreshBefore` short or route a user's requests to one instance.

For an API that accepts vo.cab access tokens from your mobile or web app, use `.AddBearer()` instead of `.AddSignIn()`;
it validates the token against auth.vo.cab's keys and refuses tokens minted for any other app.

`User.GetVocab()` reads the signed-in user's account id, profile id, handle, roles, scopes and permissions
from the claims the SDK placed on the principal. Guides and reference: https://developers.vo.cab.
