using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace PlaywrightAsb.Tests.Ui;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class UiTestBase : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = Environment.GetEnvironmentVariable("UI_BASE_URL") ?? "https://demo.playwright.dev/todomvc/",
        ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
        Locale = "en-GB",
        ColorScheme = ColorScheme.Light,
        // return base.ContextOptions();
    };

    [SetUp]
    public async Task StartTracing() => 
        await Context.Tracing.StartAsync(new()
        {
            Title = TestContext.CurrentContext.Test.Name,
            Screenshots = true,
            Snapshots = true,
            Sources = true,
        });
    
    [TearDown]
    public async Task StopTracing()
    {
        var failed = TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Failed;
        await Context.Tracing.StopAsync(new()
        {
            Path = failed
                ? Path.Combine(TestContext.CurrentContext.WorkDirectory, "traces", 
                    $"{TestContext.CurrentContext.Test.Name}.zip")
                : null,
        });
    }
}