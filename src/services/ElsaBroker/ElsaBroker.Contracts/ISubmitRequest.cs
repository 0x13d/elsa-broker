namespace ElsaBroker.Contracts;

/// <summary>
/// Single generic envelope for every inbound request.
/// Keys carries the natural key fields defined per RequestType in config.
/// This interface never changes — validation and routing are config-driven.
/// </summary>
public interface ISubmitRequest
{
    Guid                       CorrelationId { get; }
    string                     ClientId      { get; }
    string                     RequestType   { get; }
    Dictionary<string, string> Keys          { get; }
    Dictionary<string, object> Payload       { get; }  // optional extra data
    DateTime                   SubmittedAt   { get; }
}
