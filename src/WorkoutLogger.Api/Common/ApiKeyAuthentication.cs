using System.Security.Cryptography;
using System.Text;

namespace WorkoutLogger.Api.Common;

/// <summary>
/// Requires a shared secret in the <c>X-Api-Key</c> header on every route except /health.
/// </summary>
/// <remarks>
/// One key for one user: this keeps a public deployment private, it is not user
/// accounts. The key comes from the <c>API_KEY</c> setting (an app setting on
/// Azure App Service). Outside Development a missing key stops the app at
/// startup, so a forgotten setting fails closed rather than serving an open API.
/// In Development with no key set the check is off, so local runs and the tests
/// need nothing.
/// </remarks>
public static class ApiKeyAuthentication
{
    public const string HeaderName = "X-Api-Key";
    public const string SettingName = "API_KEY";

    public static void UseApiKey(this WebApplication app)
    {
        var configured = app.Configuration[SettingName]?.Trim();

        if (string.IsNullOrEmpty(configured))
        {
            if (app.Environment.IsDevelopment())
            {
                return;
            }

            throw new InvalidOperationException(
                $"The {SettingName} setting is required outside Development.");
        }

        // Hashing both sides gives equal-length inputs, so FixedTimeEquals does
        // not leak the key's length through timing.
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(configured));

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/health"))
            {
                await next(context);
                return;
            }

            var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers[HeaderName].ToString()));

            if (!CryptographicOperations.FixedTimeEquals(supplied, expected))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });
    }
}
