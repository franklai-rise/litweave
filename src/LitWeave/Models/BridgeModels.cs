using System.Text.Json;

namespace LitWeave.Models;

public sealed class BridgeRequest
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public JsonElement Payload { get; set; }
}

public sealed class BridgeResponse
{
    public string Id { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public object? Payload { get; set; }
    public BridgeError? Error { get; set; }
}

public sealed class BridgeError
{
    public string Code { get; set; } = "native_error";
    public string Message { get; set; } = string.Empty;
}
