namespace PlaywrightAsb.Tests.ServiceBus;

public sealed record SecurityAlert(
    string AlertId,
    string TenantId,
    string Detection,
    string? DeviceId = null);