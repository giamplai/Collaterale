using Avalonia;
using Avalonia.Headless;
using Collaterale.App;

// The headless session owns one UI thread; tests of different classes starting it at the same time race ("calling thread cannot access this object").
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AvaloniaTestApplication(typeof(Collaterale.App.UiTests.TestAppBuilder))]

namespace Collaterale.App.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
