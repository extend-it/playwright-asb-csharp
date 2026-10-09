using Azure.Messaging.ServiceBus;

namespace PlaywrightAsb.Tests.ServiceBus;

public static class ServiceBusMessageExtensions
{
    public static SecurityAlert Alert(this ServiceBusReceivedMessage message) =>
        message.Body.ToObjectFromJson<SecurityAlert>()
        ?? throw new InvalidOperationException($"Message {message.MessageId} has no SecurityAlert body: {message.Body}");
}