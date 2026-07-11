
namespace Messenger.Core.Options;

/// <summary>
/// FCM HTTP v1 configuration. Delivery uses an OAuth2 access token minted from a Google
/// service-account credential (see the token provider in Infrastructure). Values are optional
/// so the app boots without FCM configured — sending fails fast with a clear error instead.
/// </summary>
public record FcmNotificationOptions
{
    /// <summary>FCM API base. The v1 send path is derived from <see cref="ProjectId"/>.</summary>
    public string BaseUrl { get; init; } = "https://fcm.googleapis.com";

    /// <summary>Firebase / GCP project id (the <c>{project_id}</c> in the v1 send URL).</summary>
    public string ProjectId { get; init; } = "";

    /// <summary>Filesystem path to the service-account JSON credential used to mint tokens.</summary>
    public string CredentialsPath { get; init; } = "";
}
