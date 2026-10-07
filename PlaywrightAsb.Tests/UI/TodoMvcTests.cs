using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PlaywrightAsb.Tests.Ui;

public class ToDoMvcTests : UiTestBase
{
    private ILocator NewTodo => Page.GetByPlaceholder("What needs to be done?");
    private ILocator Todos => Page.GetByTestId("todo-item");

    [SetUp]
    public async Task OpenApp() => await Page.GotoAsync("./");

    private async Task AddAsync(params string[] titles)
    {
        foreach (var title in titles)
        {
            await NewTodo.FillAsync(title);
            await NewTodo.PressAsync("Enter");
        }
    }

    [Test]
    public async Task AddedTodosAppearInOrderAndAreCounted()
    {
        await AddAsync("port the Service Bus suite", "push to GitHub");

        await Expect(Todos).ToHaveCountAsync(2);
        await Expect(Todos).ToHaveTextAsync(new[] { "port the Service Bus suite", "push to GitHub" });
        await Expect(Page.GetByTestId("todo-count")).ToContainTextAsync("2 items left");
    }

    [Test]
    public async Task CompletedTodoLeavesTheActiveFilter()
    {
        await AddAsync("port the Service Bus suite", "push to GitHub");

        var first = Todos.First;
        await first.GetByRole(AriaRole.Checkbox).CheckAsync();
        await Expect(first).ToHaveClassAsync(new Regex("completed"));
        await Expect(Page.GetByTestId("todo-count")).ToContainTextAsync("1 item left");

        await Page.GetByRole(AriaRole.Link, new() { Name = "Active" }).ClickAsync();
        await Expect(Todos).ToHaveCountAsync(1);
        await Expect(Todos).ToHaveTextAsync(new[] { "push to GitHub" });

        await Page.GetByRole(AriaRole.Link, new() { Name = "Completed" }).ClickAsync();
        await Expect(Todos).ToHaveTextAsync(new[] { "port the Service Bus suite" });
    }
}