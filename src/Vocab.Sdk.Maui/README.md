# Vocab.Sdk.Maui

Sign in with vo.cab from a .NET MAUI app.

```csharp
builder.Services.AddVocab(
    vocab =>
    {
        vocab.NetworkId = "example";                 // your network: {network}.vo.cab
        vocab.ClientId = "<from the Apps pane>";     // a public client: no secret in an app
    },
    signIn => signIn.RedirectUri = "myapp://callback");
```

```csharp
public MainPage(VocabSignIn signIn, VocabClient vocab)
{
    signInButton.Clicked += async (_, _) => await signIn.SignInAsync();
    whoAmIButton.Clicked += async (_, _) => label.Text = (await vocab.WhoAmIAsync()).Handle;
    signOutButton.Clicked += async (_, _) => await signIn.SignOutAsync();
}
```

Register `myapp://callback` as a redirect URI in your network's Apps pane, and declare the `myapp` scheme to each
platform: a `WebAuthenticatorCallbackActivity` with an intent filter on Android, `CFBundleURLTypes` in `Info.plist`
on iOS and Mac Catalyst. The sample in `samples/Vocab.Samples.Maui` shows both.

What the package does for you:

- Authorization code flow with PKCE (S256) through the system browser (`WebAuthenticator`), with a `state` check on the way back.
- The session (access, refresh and id tokens) in `SecureStorage`, so the user stays signed in across launches.
- A fresh access token for every `VocabClient` call: tokens are refreshed before they expire, once per expiry window however many calls race, because auth.vo.cab rotates refresh tokens and revokes a session whose refresh token is used twice.
- Sign-out that revokes the refresh token at auth.vo.cab and forgets the session. There is no end-session endpoint; the user stays signed in to vo.cab itself.
- `SessionChanged` for your UI, and `IVocabSessionStore` / `IVocabAuthenticator` to substitute in tests.

The package targets `net8.0` and `net10.0` and binds to the platform's `Microsoft.Maui.Essentials` at run time, so
it builds without the MAUI workload and works from any MAUI target framework. Guides and reference: https://developers.vo.cab.
