namespace Vocab.Samples.Maui;

public class App(MainPage mainPage) : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(mainPage);
}
