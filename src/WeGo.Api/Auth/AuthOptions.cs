namespace WeGo.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Base64 HMAC key for session tokens. Left empty in development, where a
    /// key is generated and cached on disk at startup so restarts do not sign
    /// every existing cookie out. Must be set explicitly in production.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public string CookieName { get; set; } = "wego_session";

    public int CookieDays { get; set; } = 90;
}
