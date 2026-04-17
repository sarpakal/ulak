namespace AuthApi.Models;

public class TwilioOptions
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; init; } = string.Empty;
    public string AuthToken  { get; init; } = string.Empty;
    public string FromNumber { get; init; } = string.Empty;
}
