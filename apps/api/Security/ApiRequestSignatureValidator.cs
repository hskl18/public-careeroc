using System.Security.Cryptography;
using System.Text;
using Careeroc.Application;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace Careeroc.Api.Security;

internal static class ApiRequestSignatureValidator
{
    public const string UserEmailHeader = "X-Careeroc-User-Email";
    public const string UserNameHeader = "X-Careeroc-User-Name";
    public const string TimestampHeader = "X-Careeroc-Timestamp";
    public const string BodySha256Header = "X-Careeroc-Body-SHA256";
    public const string SignatureHeader = "X-Careeroc-Signature";

    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static async Task<ApiRequestSignatureValidationResult> ValidateAsync(
        HttpRequest request,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadRequiredHeader(request.Headers, UserEmailHeader, out var email))
        {
            return ApiRequestSignatureValidationResult.Invalid($"Missing required header {UserEmailHeader}.");
        }

        if (!TryReadRequiredHeader(request.Headers, TimestampHeader, out var timestamp))
        {
            return ApiRequestSignatureValidationResult.Invalid($"Missing required header {TimestampHeader}.");
        }

        if (!TryReadRequiredHeader(request.Headers, SignatureHeader, out var signature))
        {
            return ApiRequestSignatureValidationResult.Invalid($"Missing required header {SignatureHeader}.");
        }

        if (!TryReadRequiredHeader(request.Headers, BodySha256Header, out var bodyHash))
        {
            return ApiRequestSignatureValidationResult.Invalid($"Missing required header {BodySha256Header}.");
        }

        if (!IsSha256Hash(bodyHash))
        {
            return ApiRequestSignatureValidationResult.Invalid($"Invalid header value for {BodySha256Header}.");
        }

        if (!DateTimeOffset.TryParse(timestamp, out var parsedTimestamp))
        {
            return ApiRequestSignatureValidationResult.Invalid($"Invalid header value for {TimestampHeader}.");
        }

        var utcTimestamp = parsedTimestamp.ToUniversalTime();
        if (DateTimeOffset.UtcNow - utcTimestamp > MaxClockSkew ||
            utcTimestamp - DateTimeOffset.UtcNow > MaxClockSkew)
        {
            return ApiRequestSignatureValidationResult.Invalid("The request signature timestamp is outside the allowed window.");
        }

        var secret = ResolveSecret(configuration, environment);
        if (string.IsNullOrWhiteSpace(secret))
        {
            return ApiRequestSignatureValidationResult.Invalid("API request signing secret is not configured.");
        }

        if (!TryNormalizeEmail(email, out var normalizedEmail))
        {
            return ApiRequestSignatureValidationResult.Invalid($"Invalid header value for {UserEmailHeader}.");
        }

        var normalizedName = NormalizeName(request.Headers[UserNameHeader]);
        // Check the signature over the claimed hash first, so an unsigned caller cannot make the
        // API buffer a large body.
        var expectedSignature = ComputeSignature(
            secret,
            request.Method,
            GetRequestPathAndQuery(request),
            normalizedEmail,
            normalizedName,
            timestamp,
            bodyHash);

        if (!HasMatchingSignature(expectedSignature, signature))
        {
            return ApiRequestSignatureValidationResult.Invalid("The API request signature is invalid.");
        }

        var actualBodyHash = await ComputeRequestBodyHashAsync(request, cancellationToken);
        if (!HasMatchingBodyHash(actualBodyHash, bodyHash))
        {
            return ApiRequestSignatureValidationResult.Invalid("The API request body hash is invalid.");
        }

        // The web app percent-encodes the name, since header values cannot carry other scripts.
        var displayName = Uri.UnescapeDataString(normalizedName).Trim();
        return ApiRequestSignatureValidationResult.Valid(
            new AuthenticatedUserContext(
                normalizedEmail,
                string.IsNullOrWhiteSpace(displayName) ? null : displayName));
    }

    private static string ResolveSecret(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured =
            FirstNonEmpty(
                configuration["Careeroc:ApiAuthSecret"],
                configuration["CAREEROC_API_AUTH_SECRET"]);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return environment.IsDevelopment()
            ? "careeroc-dev-api-auth-secret-change-me"
            : string.Empty;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static bool TryReadRequiredHeader(
        IHeaderDictionary headers,
        string headerName,
        out string value)
    {
        value = headers.TryGetValue(headerName, out var headerValue)
            ? headerValue.ToString().Trim()
            : string.Empty;

        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryNormalizeEmail(string value, out string normalizedEmail)
    {
        try
        {
            normalizedEmail = ApplicationDefaults.NormalizeRequiredUserEmail(value);
            return true;
        }
        catch (InvalidOperationException)
        {
            normalizedEmail = string.Empty;
            return false;
        }
    }

    private static string NormalizeName(StringValues values)
    {
        var value = values.ToString();
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    // The request target exactly as the web app sent and signed it. Path and QueryString come back
    // re-escaped by ASP.NET's own rules, which differ from fetch's for characters like é or |.
    private static string GetRequestPathAndQuery(HttpRequest request)
    {
        // An absolute-form target (GET http://host/api/x) carries the host too; the web app never
        // sends one, so only origin-form targets are taken as they arrived.
        var rawTarget = request.HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget;
        return rawTarget is not null && rawTarget.StartsWith('/')
            ? rawTarget
            : $"{request.PathBase}{request.Path}{request.QueryString}";
    }

    private static async Task<string> ComputeRequestBodyHashAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        request.EnableBuffering();

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        // The raw bytes, so two different invalid UTF-8 bodies cannot share a hash.
        var hash = await SHA256.HashDataAsync(request.Body, cancellationToken);

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsSha256Hash(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        return value.All(Uri.IsHexDigit);
    }

    private static string ComputeSignature(
        string secret,
        string method,
        string pathAndQuery,
        string email,
        string name,
        string timestamp,
        string bodyHash)
    {
        var payload = string.Join(
            '\n',
            method.ToUpperInvariant(),
            pathAndQuery,
            email,
            name,
            timestamp,
            bodyHash);

        var signatureBytes = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(payload));

        return Convert.ToHexString(signatureBytes).ToLowerInvariant();
    }

    private static bool HasMatchingBodyHash(string expectedHash, string actualHash)
    {
        var normalizedActualHash = actualHash.Trim().ToLowerInvariant();
        if (normalizedActualHash.Length != expectedHash.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHash),
            Encoding.ASCII.GetBytes(normalizedActualHash));
    }

    private static bool HasMatchingSignature(string expectedSignature, string actualSignature)
    {
        var normalizedActualSignature = actualSignature.Trim().ToLowerInvariant();
        if (normalizedActualSignature.Length != expectedSignature.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedSignature),
            Encoding.ASCII.GetBytes(normalizedActualSignature));
    }
}

internal sealed record ApiRequestSignatureValidationResult(
    bool IsValid,
    AuthenticatedUserContext? UserContext,
    string Error)
{
    public static ApiRequestSignatureValidationResult Valid(AuthenticatedUserContext userContext)
    {
        return new ApiRequestSignatureValidationResult(true, userContext, string.Empty);
    }

    public static ApiRequestSignatureValidationResult Invalid(string error)
    {
        return new ApiRequestSignatureValidationResult(false, null, error);
    }
}
