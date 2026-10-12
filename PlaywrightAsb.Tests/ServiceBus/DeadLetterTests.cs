using Azure.Messaging.ServiceBus;

namespace PlaywrightAsb.Tests.ServiceBus;

[TestFixture]
[NonParallelizable]   // one broker, shared entities
public class DeadLetterTests : ServiceBusTestBase
{
    private const string Audit = "alerts-audit";

    [SetUp]
    public async Task DrainAudit() => await DrainAsync(Audit);

    [Test]
    public async Task AlertThatFailedValidationLandsInDlqWithReason()
    {
        var correlationId = NewCorrelationId();
        var alert = new SecurityAlert("ALR-BAD-1", "contoso", "Malformed");  // no DeviceId: fails validation

        await using (var sender = Client.CreateSender(Topic))
        {
            var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(alert))
            {
                CorrelationId = correlationId,
                ApplicationProperties = { ["tenantId"] = "contoso", ["severity"] = "unknown" }
            };
            await sender.SendMessageAsync(message);
        }

        // Act as the consumer: take our alert, still locked, and reject it.
        await using (var receiver = Client.CreateReceiver(Topic, Audit))
        {
            var mine = await ReceiveOwnAsync(receiver, correlationId);
            Assert.That(mine, Is.Not.Null, "alert reached alerts-audit before being rejected");

            await receiver.DeadLetterMessageAsync(mine!,
                deadLetterReason: "SchemaValidationFailed",
                deadLetterErrorDescription: "deviceId is required on a device alert");
        }

        await using var dlq = Client.CreateReceiver(Topic, Audit, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter,
            ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
        });

        var dead = await ReceiveMatchingAsync(dlq, m => m.CorrelationId == correlationId, TimeSpan.FromSeconds(15));

        Assert.That(dead, Has.Count.EqualTo(1), "exactly one dead-lettered copy");
        Assert.Multiple(() =>
        {
            Assert.That(dead[0].DeadLetterReason, Is.EqualTo("SchemaValidationFailed"));
            Assert.That(dead[0].DeadLetterErrorDescription, Does.Contain("deviceId"));
            Assert.That(dead[0].Alert(), Is.EqualTo(alert));
        });
    }

    [Test]
    public async Task PoisonAlertAbandonedMaxDeliveryCountIsDeadLetteredByTheBroker()
    {
        var correlationId = NewCorrelationId();
        var alert = new SecurityAlert("ALR-POISON-1", "contoso", "Enrichment keeps crashing", "DEV-99");

        await using (var sender = Client.CreateSender(Topic))
        {
            await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromObjectAsJson(alert))
            {
                CorrelationId = correlationId,
                ApplicationProperties = { ["tenantId"] = "contoso", ["severity"] = "unknown" },
            });
        }

        // alerts-audit has MaxDeliveryCount = 2 in docker/Config.json: two abandons are enough.
        var deliveryCounts = new List<int>();
        await using (var receiver = Client.CreateReceiver(Topic, Audit))
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var mine = await ReceiveOwnAsync(receiver, correlationId, TimeSpan.FromSeconds(5));
                if (mine is null) break;

                deliveryCounts.Add(mine.DeliveryCount);
                await receiver.AbandonMessageAsync(mine);   // a consumer that keeps crashing
            }
        }

        Assert.That(deliveryCounts, Is.EqualTo(new[] { 1, 2 }), "delivered twice, then the broker stopped");

        await using var dlq = Client.CreateReceiver(Topic, Audit, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter,
            ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
        });

        var dead = await ReceiveMatchingAsync(dlq, m => m.CorrelationId == correlationId, TimeSpan.FromSeconds(15));

        Assert.That(dead, Has.Count.EqualTo(1), "exactly one dead-lettered copy");
        Assert.Multiple(() =>
        {
            Assert.That(dead[0].DeadLetterReason, Is.EqualTo("MaxDeliveryCountExceeded"));
            Assert.That(dead[0].Alert(), Is.EqualTo(alert));
        });
    }
}