using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(AvaloniaBuilder.Tests.TestAppBuilder))]

namespace AvaloniaBuilder.Tests;

public class TestAppBuilder
{
	public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
		.UseSkia()
		.WithInterFont()
		.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
