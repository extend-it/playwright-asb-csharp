using System.Diagnostics;
using Azure.Messaging.ServiceBus;

namespace PlaywrightAsb.Tests.ServiceBus;

public abstract class ServiceBusTestBase
{
    protected ServiceBusClient Client = null!;
    // protected string Topic => Environment.GetEnvironmentVariable("SB_TOPIC") ?? "security-alerts";
    protected string Topic => Setting("SB_TOPIC", "security-alerts");
    // protected string Queue => Environment.GetEnvironmentVariable("SB_QUEUE") ?? "incident-actions";
    protected string Queue => Setting("SB_QUEUE", "incident-actions");

    private static string Setting(string name, string fallback) =>
        TestContext.Parameters.Get(name)
        ?? Environment.GetEnvironmentVariable(name)
        ?? fallback;

    /// <summary>Fail fast with a clear message when the emulator isn't reachable, instead of a 30-second AMQP timeout.</summary>
    private static async Task EnsureEmulatorIsUpAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            var response = await http.GetAsync("http://localhost:5300/health");
            if (!response.IsSuccessStatusCode)
                Assert.Fail($"Service Bus emulator answered {(int)response.StatusCode}. Is it still starting?");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Assert.Fail("Service Bus emulator is not running. From the repo root: " +
                        "docker compose -f docker/docker-compose.yaml up -d");
        }
    }

    [OneTimeSetUp]
    // public void CreateClient()
    // {
    //     var cs = Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION_STRING")
    //              ?? throw new InvalidOperationException("SERVICEBUS_CONNECTION_STRING is not set");
    //     Client = new ServiceBusClient(cs);
    // }
    public async Task CreateClient()
    {
        
        await EnsureEmulatorIsUpAsync();
        Client = new ServiceBusClient(Setting(
            "SERVICEBUS_CONNECTION_STRING",
            "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"));
    }

    [OneTimeTearDown]
    public async Task DisposeClient() => await Client.DisposeAsync();

    protected static string NewCorrelationId() => $"test-{Guid.NewGuid()}";

    /// <summary>Receive until the predicate has matched or the budget runs out. Everything received is completed.</summary>
    protected static async Task<List<ServiceBusReceivedMessage>> ReceiveMatchingAsync(
        ServiceBusReceiver receiver,
        Func<ServiceBusReceivedMessage, bool> predicate,
        TimeSpan? budget = null)
    {
        var found = new List<ServiceBusReceivedMessage>();
        var limit = budget ?? TimeSpan.FromSeconds(10);
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < limit)
        {
            var batch = await receiver.ReceiveMessagesAsync(10, TimeSpan.FromSeconds(1));
            foreach (var message in batch)
            {
                if (predicate(message)) found.Add(message);
                await receiver.CompleteMessageAsync(message);
            }
            if (found.Count > 0 && batch.Count == 0) break;
        }
        return found;
    }

    /// <summary>Receive until our message arrives and return it still locked; complete everything else.</summary>
    protected static async Task<ServiceBusReceivedMessage?> ReceiveOwnAsync(
        ServiceBusReceiver receiver, string correlationId, TimeSpan? budget = null)
    {
        var limit = budget ?? TimeSpan.FromSeconds(10);
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < limit)
        {
            ServiceBusReceivedMessage? mine = null;
            foreach (var message in await receiver.ReceiveMessagesAsync(10, TimeSpan.FromSeconds(1)))
            {
                if (mine is null && message.CorrelationId == correlationId)
                    mine = message;
                else
                    await receiver.CompleteMessageAsync(message);
            }
            if (mine is not null) return mine;
        }
        return null;
    }

    /// <summary>Empty a subscription so a test starts from a known state.</summary>
    protected async Task DrainAsync(string subscription)
    {
        await using var receiver = Client.CreateReceiver(Topic, subscription);
        var batch = await receiver.ReceiveMessagesAsync(50, TimeSpan.FromSeconds(1));
        while (batch.Count > 0)
        {
            foreach (var message in batch) await receiver.CompleteMessageAsync(message);
            batch = await receiver.ReceiveMessagesAsync(50, TimeSpan.FromSeconds(1));
        }
    }
}