namespace Messenger.Core.Interfaces;

public interface ICorrelationContext
{
    /// <summary>
    /// The current request correlation id, or null when not available.
    /// </summary>
    string? CorrelationId { get; }
}
