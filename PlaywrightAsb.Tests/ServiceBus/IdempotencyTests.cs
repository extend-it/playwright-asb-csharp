using Azure.Messaging.ServiceBus;

namespace PlaywrightAsb.Tests.ServiceBus;

[TestFixture]
[NonParallelizable]   // one broker, shared entities
public class IdempotencyTests : ServiceBusTestBase
{
    [SetUp]
    public async Task DrainActions() => await DrainQueueAsync(Queue);

    [Test]
    public async Task BrokerDropsASecondSendWithTheSameMessageId()
    {
        // Unique per run: the queue remembers MessageIds for 2 minutes, so a fixed id would collide with the last run.
        var messageId = $"isolate-DEV-77-{Guid.NewGuid():N}";
        var correlationId = NewCorrelationId();
        var action = new IncidentAction(messageId, "isolate-device", "DEV-77", "contoso");

        await using (var sender = Client.CreateSender(Queue))
        {
            for (var send = 0; send < 2; send++)   // the second send is a publisher retry
            {
                await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromObjectAsJson(action))
                {
                    MessageId = messageId,
                    CorrelationId = correlationId,
                });
            }
        }

        await using var receiver = Client.CreateReceiver(Queue);
        var received = await ReceiveMatchingAsync(receiver, m => m.CorrelationId == correlationId, TimeSpan.FromSeconds(8));

        Assert.That(received.Select(m => m.BodyAs<IncidentAction>()), Is.EqualTo(new[] { action }),
            "duplicate detection keeps only the first send");
    }

    [Test]
    public async Task BrokerAcceptsResendsWithADifferentMessageId()
    {
        // Control for the duplicate test: same body, different MessageIds, so both must arrive.
        var correlationId = NewCorrelationId();
        var baseId = $"isolate-DEV-77-{Guid.NewGuid():N}";
        var action = new IncidentAction(baseId, "isolate-device", "DEV-77", "contoso");

        await using (var sender = Client.CreateSender(Queue))
        {
            foreach (var id in new[] { baseId, baseId + "-retry" })
            {
                await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromObjectAsJson(action))
                {
                    MessageId = id,
                    CorrelationId = correlationId,
                });
            }
        }

        await using var receiver = Client.CreateReceiver(Queue);
        var received = await ReceiveMatchingAsync(receiver, m => m.CorrelationId == correlationId, TimeSpan.FromSeconds(8));

        Assert.That(received, Has.Count.EqualTo(2), "deduplication is keyed on MessageId, not on the body");
    }

    /// <summary>Stand-in for a responder that isolates devices; optionally guarded by a processed-ids store.</summary>
    private sealed class IsolationResponder(bool idempotent)
    {
        private readonly HashSet<string> _processed = new();
        public List<string> IsolatedDevices { get; } = new();

        public string Handle(ServiceBusReceivedMessage message)
        {
            if (idempotent && !_processed.Add(message.MessageId)) return "skipped";
            IsolatedDevices.Add(message.BodyAs<IncidentAction>().DeviceId);
            return "applied";
        }
    }

    /// <summary>Send one isolate action, deliver it, abandon it as if the handler crashed, deliver it again.</summary>
    private async Task<List<string>> DeliverTwiceAsync(string deviceId, Func<ServiceBusReceivedMessage, string> handle)
    {
        var messageId = $"isolate-{deviceId}-{Guid.NewGuid():N}";
        var correlationId = NewCorrelationId();
        var action = new IncidentAction(messageId, "isolate-device", deviceId, "contoso");

        await using (var sender = Client.CreateSender(Queue))
        {
            await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromObjectAsJson(action))
            {
                MessageId = messageId,
                CorrelationId = correlationId,
            });
        }

        var outcomes = new List<string>();
        await using var receiver = Client.CreateReceiver(Queue);

        var first = await ReceiveOwnAsync(receiver, correlationId);
        Assert.That(first, Is.Not.Null, "first delivery");
        outcomes.Add(handle(first!));
        await receiver.AbandonMessageAsync(first!);

        var second = await ReceiveOwnAsync(receiver, correlationId);
        Assert.That(second, Is.Not.Null, "redelivery");
        Assert.That(second!.DeliveryCount, Is.EqualTo(2));
        outcomes.Add(handle(second));
        await receiver.CompleteMessageAsync(second);

        return outcomes;
    }

    [Test]
    public async Task IdempotentHandlerIsolatesTheDeviceOnlyOnceAcrossARedelivery()
    {
        var responder = new IsolationResponder(idempotent: true);
        var outcomes = await DeliverTwiceAsync("DEV-88", responder.Handle);

        Assert.Multiple(() =>
        {
            Assert.That(outcomes, Is.EqualTo(new[] { "applied", "skipped" }));
            Assert.That(responder.IsolatedDevices, Is.EqualTo(new[] { "DEV-88" }), "isolated once, not twice");
        });
    }

    [Test]
    public async Task NaiveHandlerIsolatesTheDeviceTwiceOnRedelivery()
    {
        // Control: without the guard, at-least-once delivery repeats the side effect.
        var responder = new IsolationResponder(idempotent: false);
        var outcomes = await DeliverTwiceAsync("DEV-89", responder.Handle);

        Assert.Multiple(() =>
        {
            Assert.That(outcomes, Is.EqualTo(new[] { "applied", "applied" }));
            Assert.That(responder.IsolatedDevices, Is.EqualTo(new[] { "DEV-89", "DEV-89" }), "the hazard the guard exists for");
        });
    }
}