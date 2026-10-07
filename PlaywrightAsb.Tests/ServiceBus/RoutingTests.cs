using Azure.Messaging.ServiceBus;

namespace PlaywrightAsb.Tests.ServiceBus;

[TestFixture]
[NonParallelizable]   // one broker, shared entities
public class RoutingTests : ServiceBusTestBase
{
    [SetUp]
    public async Task DrainSubscriptions()
    {
        foreach (var subscription in new[] { "alerts-critical", "alerts-informational", "alerts-audit" })
            await DrainAsync(subscription);
    }

    [Test]
    public async Task CriticalAlertReachesCriticalAndAuditOnly()
    {
        var correlationId = NewCorrelationId();
        var alert = new SecurityAlert("ALR-1001", "contoso", "Suspicious PowerShell encoded command", "DEV-77");

        await using (var sender = Client.CreateSender(Topic))
        {
            var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(alert))
            {
                CorrelationId = correlationId,
                ApplicationProperties = { ["severity"] = "critical", ["tenantId"] = "contoso" },
            };
            await sender.SendMessageAsync(message);
        }

        bool Mine(ServiceBusReceivedMessage m) => m.CorrelationId == correlationId;

        await using var critical = Client.CreateReceiver(Topic, "alerts-critical");
        await using var audit = Client.CreateReceiver(Topic, "alerts-audit");
        await using var informational = Client.CreateReceiver(Topic, "alerts-informational");

        var onCritical = await ReceiveMatchingAsync(critical, Mine);
        var onAudit = await ReceiveMatchingAsync(audit, Mine);
        var onInformational = await ReceiveMatchingAsync(informational, Mine, TimeSpan.FromSeconds(4));

        Assert.Multiple(() =>
        {
            Assert.That(onCritical, Has.Count.EqualTo(1), "critical subscription receives it");
            Assert.That(onAudit, Has.Count.EqualTo(1), "audit subscription receives everything");
            Assert.That(onInformational, Is.Empty, "informational subscription must not receive it");
        });

        var received = onCritical[0].Body.ToObjectFromJson<SecurityAlert>();
        Assert.That(received, Is.Not.Null, "message body deserializes to a SecurityAlert");
        Assert.Multiple(() =>
        {
            Assert.That(received.AlertId, Is.EqualTo("ALR-1001"));
            Assert.That(received.DeviceId, Is.EqualTo("DEV-77"));
            Assert.That(onCritical[0].ApplicationProperties["severity"], Is.EqualTo("critical"));
        });
    }

    [Test]
    public async Task InformationalAlertDoesNotReachCritical()
    {
        var correlationId = NewCorrelationId();
        var alert = new SecurityAlert("ALR-1002", "contoso", "New device enrolled");

        await using (var sender = Client.CreateSender(Topic))
        {
            await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromObjectAsJson(alert))
            {
                CorrelationId = correlationId,
                ApplicationProperties = { ["severity"] = "informational", ["tenantId"] = "contoso" },
            });
        }

        bool Mine(ServiceBusReceivedMessage m) => m.CorrelationId == correlationId;

        await using var informational = Client.CreateReceiver(Topic, "alerts-informational");
        await using var critical = Client.CreateReceiver(Topic, "alerts-critical");

        Assert.That(await ReceiveMatchingAsync(informational, Mine), Has.Count.EqualTo(1));
        Assert.That(await ReceiveMatchingAsync(critical, Mine, TimeSpan.FromSeconds(4)), Is.Empty);
    }
}