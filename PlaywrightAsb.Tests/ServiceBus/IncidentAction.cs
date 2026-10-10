namespace PlaywrightAsb.Tests.ServiceBus;

public sealed record IncidentAction(string ActionId, string Action, string DeviceId, string TenantId);