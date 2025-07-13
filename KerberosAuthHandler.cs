using System.Security;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kerberos.NET;
using Kerberos.NET.Crypto;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

public class KerberosAuthOptions : AuthenticationSchemeOptions
{
    public string? KeytabPath { get; set; }
    public bool AutoSendChallenge { get; set; } = true;
}

public class KerberosAuthHandler : AuthenticationHandler<KerberosAuthOptions>
{
    private static KeyTable? keytab;
    private readonly ILoggerFactory loggerFactory;
    private KerberosValidator? kerbValidator;

    public KerberosAuthHandler(
        IOptionsMonitor<KerberosAuthOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder) : base(options, loggerFactory, encoder)
    {
        this.loggerFactory = loggerFactory;
    }

    protected override async Task InitializeHandlerAsync()
    {
        await base.InitializeHandlerAsync();

        if (keytab != null)
        {
            kerbValidator = new KerberosValidator(keytab, loggerFactory)
            {
                ValidateAfterDecrypt = ValidationActions.All
            };
            Logger.LogDebug("Using cached keytab");
            return;
        }

        using (var fs = new FileStream(Options.KeytabPath ?? throw new InvalidOperationException("Keytab path is not configured"), FileMode.Open, FileAccess.Read))
        {
            keytab = new KeyTable(fs);
        }
        kerbValidator = new KerberosValidator(keytab, loggerFactory)
        {
            ValidateAfterDecrypt = ValidationActions.All
        };
        Logger.LogInformation("Successfully loaded keytab from {KeytabPath}", Options.KeytabPath);
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            Logger.LogDebug("No Authorization header is present");
            return AuthenticateResult.NoResult();
        }

        var authHeaderValue = authHeader.First();

        if (string.IsNullOrEmpty(authHeaderValue) || !authHeaderValue.StartsWith("Negotiate ", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogDebug("Authorization header is not a Negotiate token");
            return AuthenticateResult.NoResult();
        }

        try
        {
            Logger.LogDebug("Validating Kerberos token with validator actions: {ValidationActions}",
                kerbValidator!.ValidateAfterDecrypt);

            var authenticator = new KerberosAuthenticator(kerbValidator);
            var identity = await authenticator.Authenticate(authHeaderValue);

            var authenticatedUser = identity.Name;
            Logger.LogInformation("Successfully authenticated user: {Username}", authenticatedUser);

            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                var claimsJson = JsonSerializer.Serialize(identity.Claims.Select(c => new { c.Type, c.Value }).ToArray());
                Logger.LogDebug("Available claims for {Username}: {ClaimsJson}", authenticatedUser, claimsJson);
            }

            return AuthenticateResult.Success(ticket);
        }
        catch (KerberosValidationException krbEx)
        {
            Logger.LogWarning(krbEx, "Kerberos validation failed: {Message}", krbEx.Message);
            return AuthenticateResult.Fail($"Kerberos validation failed: {krbEx.Message}");
        }
        catch (SecurityException secEx)
        {
            Logger.LogWarning(secEx, "Security validation failed: {Message}", secEx.Message);
            return AuthenticateResult.Fail($"Security validation failed: {secEx.Message}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Kerberos authentication failed unexpectedly");
            return AuthenticateResult.Fail($"Authentication failed: {ex.Message}");
        }
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Options.AutoSendChallenge)
        {
            Response.Headers.Append("WWW-Authenticate", "Negotiate");
            Logger.LogDebug("Sending Negotiate challenge to client");
        }
        else
        {
            Logger.LogWarning("AutoSendChallenge is disabled, not sending WWW-Authenticate header");
        }

        await base.HandleChallengeAsync(properties);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Logger.LogDebug("Access forbidden for authenticated user");
        await base.HandleForbiddenAsync(properties);
    }

    protected override Task InitializeEventsAsync()
    {
        Logger.LogDebug("Kerberos authentication events initialized");
        return base.InitializeEventsAsync();
    }
}