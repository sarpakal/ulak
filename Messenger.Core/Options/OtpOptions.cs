namespace Messenger.Core.Options
{
    // OtpOptions.cs — in Messenger.Core/Options/
    public record OtpOptions
    {
        public int ExpirySeconds { get; init; } = 120;
        public int MaxAttempts { get; init; } = 5;
    }
}
