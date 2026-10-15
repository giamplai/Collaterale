using Avalonia;
using Avalonia.Headless;
using Collaterale.App;

[assembly: AvaloniaTestApplication(typeof(Collaterale.App.UiTests.TestAppBuilder))]

namespace Collaterale.App.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
