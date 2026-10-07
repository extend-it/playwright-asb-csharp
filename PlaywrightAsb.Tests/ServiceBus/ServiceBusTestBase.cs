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

    [OneTimeSetUp]
    // public void CreateClient()
    // {
    //     var cs = Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION_STRING")
    //              ?? throw new InvalidOperationException("SERVICEBUS_CONNECTION_STRING is not set");
    //     Client = new ServiceBusClient(cs);
    // }
    public void CreateClient() =>
        Client = new ServiceBusClient(Setting(
            "SERVICEBUS_CONNECTION_STRING",
            "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"));

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