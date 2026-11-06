// The smallest .NET MAUI app that signs in with vo.cab and reads the user's profile in its network.
// Put your network's label and the client id from its Apps pane below, and register vocabsample://callback as a
// redirect URI there. The scheme is declared to Android in Platforms/Android/SignInCallbackActivity.cs and to
// iOS and Mac Catalyst in their Info.plist.
using Vocab.Sdk.Maui;

namespace Vocab.Samples.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddVocab(
            vocab =>
            {
                vocab.NetworkId = "example";
                vocab.ClientId = "<your client id>";
            },
            signIn => signIn.RedirectUri = "vocabsample://callback");

        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
