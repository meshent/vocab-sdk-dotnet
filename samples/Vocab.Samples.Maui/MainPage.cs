using Vocab.Sdk;
using Vocab.Sdk.Maui;

namespace Vocab.Samples.Maui;

/// <summary>Sign in, ask the network API who the user is, sign out. The SDK keeps the session between launches.</summary>
public class MainPage : ContentPage
{
    private readonly VocabSignIn _signIn;
    private readonly VocabClient _vocab;
    private readonly Label _status = new() { FontSize = 16, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Button _signInButton = new() { Text = "Sign in with vo.cab" };
    private readonly Button _whoAmIButton = new() { Text = "Who am I, according to the API?" };
    private readonly Button _signOutButton = new() { Text = "Sign out" };

    public MainPage(VocabSignIn signIn, VocabClient vocab)
    {
        _signIn = signIn;
        _vocab = vocab;
        Title = "Vocab Sample";

        _signInButton.Clicked += async (_, _) => await RunAsync(async () =>
        {
            var session = await _signIn.SignInAsync();
            return $"Signed in. The access token expires at {session.ExpiresAt.ToLocalTime():t}.";
        });
        _whoAmIButton.Clicked += async (_, _) => await RunAsync(async () =>
        {
            var me = await _vocab.WhoAmIAsync();
            return $"You are profile {me.ProfileId} in network {me.NetworkId}, with scopes {string.Join(' ', me.Scopes)}.";
        });
        _signOutButton.Clicked += async (_, _) => await RunAsync(async () =>
            await _signIn.SignOutAsync()
                ? "Signed out; the refresh token was revoked at auth.vo.cab."
                : "Signed out of this app; auth.vo.cab could not be reached to revoke the refresh token.");

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 12,
                Children =
                {
                    new Label { Text = "Sign in with vo.cab", FontSize = 28, FontAttributes = FontAttributes.Bold },
                    _status,
                    _signInButton,
                    _whoAmIButton,
                    _signOutButton,
                },
            },
        };

        _signIn.SessionChanged += (_, e) => MainThread.BeginInvokeOnMainThread(() => Render(e.Session));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Render(await _signIn.GetSessionAsync());
    }

    private void Render(VocabSession? session)
    {
        var signedIn = session is not null;
        _signInButton.IsVisible = !signedIn;
        _whoAmIButton.IsVisible = signedIn;
        _signOutButton.IsVisible = signedIn;
        if (!signedIn)
            _status.Text = "You are not signed in.";
    }

    private async Task RunAsync(Func<Task<string>> action)
    {
        try
        {
            _status.Text = await action();
        }
        catch (VocabSignInException ex) when (ex.IsAccessDenied)
        {
            _status.Text = "You chose not to sign in.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "The browser was closed before sign-in finished.";
        }
        catch (VocabApiException ex) when (ex.IsNoProfile)
        {
            _status.Text = "You have no profile in this network yet. Sign in again to be offered one.";
        }
        catch (Exception ex) when (ex is VocabApiException or VocabSignInException or HttpRequestException)
        {
            _status.Text = ex.Message;
        }
    }
}
