using System.Xml.Linq;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace PlaywrightAsb.Tests.Api;

[TestFixture]
public class EmulatorHealthApiTests : PlaywrightTest
{
    private IAPIRequestContext _request = null!;
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Sb = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect";

    [SetUp]
    public async Task CreateRequestContext() =>
        _request = await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = TestContext.Parameters.Get("SB_MANAGEMENT_URL") ?? "http://localhost:5300",
        });

    [TearDown]
    public async Task DisposeRequestContext() => await _request.DisposeAsync();

    [Test]
    public async Task HealthEndpointAnswersOk()
    {
        var response = await _request.GetAsync("/health");
        await Expect(response).ToBeOKAsync();

        var health = await response.JsonAsync();
        Assert.That(health?.GetProperty("status").GetString(), Is.EqualTo("healthy"));
    }

    [Test]
    public async Task UnknownPathIsReadAsAnEntityLookupAndAnswersAnEmptyFeed()
    {
        var response = await _request.GetAsync("/health-does-not-exist");
        await Expect(response).ToBeOKAsync();
        Assert.That(response.Headers["content-type"], Does.StartWith("application/atom+xml"));

        XNamespace atom = "http://www.w3.org/2005/Atom";
        var feed = XDocument.Parse(await response.TextAsync()).Root!;
        Assert.Multiple(() =>
        {
            Assert.That(feed.Name, Is.EqualTo(atom + "feed"));
            Assert.That(feed.Element(atom + "title")?.Value, Is.EqualTo("Publicly Listed Services"));
            Assert.That(feed.Elements(atom + "entry"), Is.Empty, "no entity of that name exists");
        });
    }

    [Test]
    public async Task IncidentActionsQueueIsConfiguredAsTheIdempotencyTestsAssume()
    {
        var response = await _request.GetAsync("/incident-actions");
        await Expect(response).ToBeOKAsync();

        var entry = XDocument.Parse(await response.TextAsync()).Root!;
        var queue = entry.Element(Atom + "content")?.Element(Sb + "QueueDescription");
        string? Setting(string name) => queue?.Element(Sb + name)?.Value;

        Assert.Multiple(() =>
        {
            Assert.That(entry.Name, Is.EqualTo(Atom + "entry"), "an existing entity answers with a single entry, not a feed");
            Assert.That(entry.Element(Atom + "title")?.Value, Is.EqualTo("incident-actions"));
            Assert.That(queue, Is.Not.Null, "the entry carries a QueueDescription");
            Assert.That(Setting("RequiresDuplicateDetection"), Is.EqualTo("true"), "BrokerDropsASecondSend… relies on it");
            Assert.That(Setting("DuplicateDetectionHistoryTimeWindow"), Is.EqualTo("PT2M"));
            Assert.That(Setting("MaxDeliveryCount"), Is.EqualTo("2"), "DeliverTwiceAsync expects exactly one redelivery");
        });
    }

    [TestCase("incident-actions", "QueueDescription")]
    [TestCase("security-alerts", "TopicDescription")]
    public async Task ExistingEntityAnswersASingleEntry(string entity, string description)
    {
        var response = await _request.GetAsync($"/{entity}");
        await Expect(response).ToBeOKAsync();

        var entry = XDocument.Parse(await response.TextAsync()).Root!;
        Assert.Multiple(() =>
        {
            Assert.That(entry.Name, Is.EqualTo(Atom + "entry"), "an existing entity answers with a single entry, not a feed");
            Assert.That(entry.Element(Atom + "title")?.Value, Is.EqualTo(entity));
            Assert.That(entry.Element(Atom + "content")?.Element(Sb + description), Is.Not.Null,
                $"the entry carries a {description}");
        });
    }
}