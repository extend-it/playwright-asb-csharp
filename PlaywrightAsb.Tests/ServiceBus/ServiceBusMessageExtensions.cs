using Azure;
using Azure.Messaging.ServiceBus;

namespace PlaywrightAsb.Tests.ServiceBus;

public static class ServiceBusMessageExtensions
{
    public static T BodyAs<T>(this ServiceBusReceivedMessage message) =>
        message.Body.ToObjectFromJson<T>()
        ?? throw new InvalidOperationException($"Message {message.MessageId} has no {typeof(T).Name} body: {message.Body}");

    public static SecurityAlert Alert(this ServiceBusReceivedMessage message) => 
        message.BodyAs<SecurityAlert>();
}