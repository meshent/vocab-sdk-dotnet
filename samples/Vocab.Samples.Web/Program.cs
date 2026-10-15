// The smallest ASP.NET Core app that signs in with vo.cab and reads the user's profile in its network.
// Configure Vocab:NetworkId, Vocab:ClientId and (for a confidential app) Vocab:ClientSecret in appsettings
// or user secrets, and register https://localhost:5001/signin-vocab as the redirect URI in the Apps pane.
using Microsoft.AspNetCore.Authentication;
using Vocab.Sdk;
using Vocab.Sdk.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddVocab(builder.Configuration.GetSection("Vocab"))
    .AddSignIn()
    .AddClient();
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", (HttpContext http) =>
{
    var vocab = http.User.GetVocab();
    return vocab is null
        ? Results.Content("""<a href="/signin">Sign in with vo.cab</a>""", "text/html")
        : Results.Content($"""
            <p>Signed in as <b>{vocab.Username}</b>.
            {(vocab.HasProfile ? $"Your handle here is <b>@{vocab.Handle}</b>." : "You have no profile in this network yet.")}</p>
            <p><a href="/me">Who am I, according to the API?</a> · <a href="/signout">Sign out</a></p>
            """, "text/html");
});

app.MapGet("/signin", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }));

// Sign out of the session cookie only (the default): the SDK revokes the refresh token at auth.vo.cab.
app.MapPost("/signout", () => Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }));
app.MapGet("/signout", () => Results.Content("""<form method="post"><button>Sign out</button></form>""", "text/html"));

app.MapGet("/me", async (VocabClient vocab) =>
{
    try
    {
        return Results.Ok(await vocab.WhoAmIAsync());
    }
    catch (VocabApiException ex) when (ex.IsNoProfile)
    {
        return Results.Problem(statusCode: 404, title: "No profile in this network", detail: ex.Message);
    }
}).RequireAuthorization();

app.Run();
