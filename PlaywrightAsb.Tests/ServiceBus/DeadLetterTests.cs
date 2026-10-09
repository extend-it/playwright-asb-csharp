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

        await using var dlq = Client.CreateReceiver(Topic, Audit,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        var dead = await ReceiveMatchingAsync(dlq, m => m.CorrelationId == correlationId, TimeSpan.FromSeconds(15));

        Assert.That(dead, Has.Count.EqualTo(1), "exactly one dead-lettered copy");
        Assert.Multiple(() =>
        {
            Assert.That(dead[0].DeadLetterReason, Is.EqualTo("SchemaValidationFailed"));
            Assert.That(dead[0].DeadLetterErrorDescription, Does.Contain("deviceId"));
            Assert.That(dead[0].Alert(), Is.EqualTo(alert));
        });
    }

}