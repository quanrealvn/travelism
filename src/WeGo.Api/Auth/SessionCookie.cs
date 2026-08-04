namespace WeGo.Api.Auth;

/// <summary>Spec §5.7: HttpOnly, SameSite=Lax session cookie.</summary>
public static class SessionCookie
{
    public static void Write(HttpContext context, AuthOptions options, string token)
    {
        context.Response.Cookies.Append(options.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            // Secure tracks the current scheme so the cookie still works over
            // plain http on localhost, but is never sent in the clear once the
            // app is served over TLS.
            Secure = context.Request.IsHttps,
            Path = "/",
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(options.CookieDays),
        });
    }

    public static string? Read(HttpContext context, AuthOptions options) =>
        context.Request.Cookies.TryGetValue(options.CookieName, out var value) ? value : null;
}
