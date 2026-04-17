namespace AuthApi.Models;

public class CorvassOptions
{
    public const string SectionName = "Corvass";

    public string SmsUrl        { get; init; } = string.Empty;
    public string ApiKey        { get; init; } = string.Empty;
    public string ApiSecret     { get; init; } = string.Empty;
    public string Originator    { get; init; } = "AKAL YNT.";
    public string MessageType   { get; init; } = "B";
    public string RecipientType { get; init; } = "BIREYSEL";
}
