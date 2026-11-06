# Vocab.Samples.Maui

A .NET MAUI app that signs in with vo.cab, asks the network API who the user is, and signs out.

1. In your network's Apps pane on vo.cab, register a **public** app with `vocabsample://callback` as a redirect URI.
2. Put the network's label and the client id in `MauiProgram.cs`.
3. Build for your platform:

```bash
dotnet workload install maui-android          # once; maui-ios / maui-maccatalyst on a Mac
dotnet build -f net10.0-android
```

The `vocabsample` scheme is declared in `Platforms/Android/SignInCallbackActivity.cs` and in each Apple platform's
`Info.plist`. Change all three together with `RedirectUri` if you rename it.
