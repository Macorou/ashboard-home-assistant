namespace HomeAssistant;

/// <summary>L'état d'une entité Home Assistant tel que lu par <c>GET /api/states/&lt;entity_id&gt;</c>.</summary>
internal sealed record HaState(string EntityId, string State, string? Unit, string? DeviceClass, string? FriendlyName);
