using System.Diagnostics;
using Azure.Messaging.ServiceBus;

namespace PlaywrightAsb.Tests.ServiceBus;

public abstract class ServiceBusTestBase
{
    protected ServiceBusClient Client = null!;
    protected string Topic => Setting("SB_TOPIC", "security-alerts");
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

    /// <summary>Receive until the predicate has matched or the budget runs out. Returns each matching message once,
    /// however many times it was delivered. Everything received is completed (best effort).</summary>
    protected static async Task<List<ServiceBusReceivedMessage>> ReceiveMatchingAsync(
        ServiceBusReceiver receiver,
        Func<ServiceBusReceivedMessage, bool> predicate,
        TimeSpan? budget = null)
    {
        var found = new List<ServiceBusReceivedMessage>();
        var seen = new HashSet<long>();
        var limit = budget ?? TimeSpan.FromSeconds(10);
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < limit)
        {
            var batch = await receiver.ReceiveMessagesAsync(10, TimeSpan.FromSeconds(1));
            foreach (var message in batch)
            {
                if (predicate(message))
                {
                    if (seen.Add(message.SequenceNumber))
                        found.Add(message);
                    else
                        TestContext.Progress.WriteLine(
                            $"Redelivery of sequence {message.SequenceNumber} ignored (delivery count {message.DeliveryCount}).");
                }
                await CompleteQuietlyAsync(receiver, message);
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
                    await CompleteQuietlyAsync(receiver, message);
            }
            if (mine is not null) return mine;
        }
        return null;
    }

    /// <summary>Empty a subscription so a test starts from a known state.</summary>
    protected async Task DrainAsync(string subscription)
    {
        await using var receiver = Client.CreateReceiver(Topic, subscription);
        await DrainReceiverAsync(receiver);
    }

    protected async Task DrainQueueAsync(string queue)
    {
        await using var receiver = Client.CreateReceiver(queue);
        await DrainReceiverAsync(receiver);
    }

    private static async Task DrainReceiverAsync(ServiceBusReceiver receiver)
    {
        var batch = await receiver.ReceiveMessagesAsync(50, TimeSpan.FromSeconds(1));
        while (batch.Count > 0)
        {
            foreach (var message in batch) await CompleteQuietlyAsync(receiver, message);
            batch = await receiver.ReceiveMessagesAsync(50, TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>Settle a message we only observed. This is cleanup, not an assertion: a lost lock must not fail the test.</summary>
    private static async Task CompleteQuietlyAsync(ServiceBusReceiver receiver, ServiceBusReceivedMessage message)
    {
        if (receiver.ReceiveMode == ServiceBusReceiveMode.ReceiveAndDelete) return;   // removed on receipt
        try
        {
            await receiver.CompleteMessageAsync(message);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessageLockLost)
        {
            TestContext.Progress.WriteLine($"Lock already lost on {message.MessageId} during cleanup; ignored.");
        }
    }
}