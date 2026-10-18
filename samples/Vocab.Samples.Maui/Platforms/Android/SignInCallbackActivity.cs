using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Maui.Authentication;

namespace Vocab.Samples.Maui;

/// <summary>
/// Receives vocabsample://callback when auth.vo.cab sends the user back, and hands it to the sign-in that is waiting.
/// The scheme and host here must match VocabMauiOptions.RedirectUri.
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "vocabsample", DataHost = "callback")]
public class SignInCallbackActivity : WebAuthenticatorCallbackActivity
{
}
