using System.Security;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Kerberos.NET;
using Kerberos.NET.Crypto;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

public class KerberosAuthOptions : AuthenticationSchemeOptions
{
    public string? KeytabPath { get; set; }
    public string? ServicePrincipalName { get; set; }
    public bool AutoSendChallenge { get; set; } = true;
    public bool ValidateClientHostname { get; set; } = false;
}

public class KerberosAuthHandler : AuthenticationHandler<KerberosAuthOptions>
{
    private readonly ILoggerFactory loggerFactory;
    private static KeyTable? keytab;

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
        try
        {
            if (keytab != null)
            {
                Logger.LogDebug("Using cached keytab");
                return;
            }

            // Load keytab during initialization to improve performance
            using (var fs = new FileStream(Options.KeytabPath ?? throw new InvalidOperationException("Keytab path is not configured"), FileMode.Open, FileAccess.Read))
            {
                keytab = new KeyTable(fs);
            }
            Logger.LogInformation("Successfully loaded keytab from {KeytabPath}", Options.KeytabPath);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load keytab file from {KeytabPath}", Options.KeytabPath);
            throw;
        }
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            Logger.LogDebug("No Authorization header present");
            return AuthenticateResult.NoResult();
        }

        var authHeaderValue = authHeader.ToString();

        if (string.IsNullOrEmpty(authHeaderValue) || !authHeaderValue.StartsWith("Negotiate ", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogDebug("Authorization header is not a Negotiate token");
            return AuthenticateResult.NoResult();
        }

        var token = authHeaderValue.Substring("Negotiate ".Length).Trim();

        // Validate token format
        if (string.IsNullOrEmpty(token))
        {
            Logger.LogWarning("Empty Negotiate token received");
            return AuthenticateResult.Fail("Empty Negotiate token");
        }

        byte[] tokenBytes;
        try
        {
            tokenBytes = Convert.FromBase64String(token);
        }
        catch (FormatException ex)
        {
            Logger.LogWarning(ex, "Invalid base64 token received");
            return AuthenticateResult.Fail("Invalid token format");
        }

        try
        {
            // Create the validator with proper configuration
            var kerbValidator = new KerberosValidator(keytab, loggerFactory)
            {
                ValidateAfterDecrypt = ValidationActions.All
            };

            Logger.LogDebug("Validating Kerberos token with validator actions: {ValidationActions}", 
                kerbValidator.ValidateAfterDecrypt);

            var decryptedApReq = await kerbValidator.Validate(tokenBytes);

            var authenticatedUser = decryptedApReq.Ticket.CName.FullyQualifiedName;
            Logger.LogInformation("Successfully authenticated user: {Username}", authenticatedUser);

            // Create comprehensive claims from the Kerberos ticket
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, authenticatedUser),
                new Claim(ClaimTypes.AuthenticationMethod, "Kerberos"),
                new Claim(ClaimTypes.NameIdentifier, authenticatedUser, ClaimValueTypes.String, Scheme.Name),
                new Claim("kerb-flags", decryptedApReq.Ticket.Flags.ToString(), ClaimValueTypes.String, Scheme.Name)
            };

            // Add authentication time if available from the authenticator
            if (decryptedApReq.Authenticator?.CTime != null)
            {
                claims.Add(new Claim("kerb-authtime", decryptedApReq.Authenticator.CTime.ToString("o"), 
                    ClaimValueTypes.DateTime, Scheme.Name));
            }

            // Add service principal name if available
            if (!string.IsNullOrEmpty(Options.ServicePrincipalName))
            {
                claims.Add(new Claim("kerb-spn", Options.ServicePrincipalName, ClaimValueTypes.String, Scheme.Name));
            }

            // Add realm information if available
            if (!string.IsNullOrEmpty(decryptedApReq.Ticket.CRealm))
            {
                claims.Add(new Claim("kerb-realm", decryptedApReq.Ticket.CRealm, ClaimValueTypes.String, Scheme.Name));
            }

            Logger.LogDebug("Created {ClaimCount} claims from Kerberos ticket", claims.Count);

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

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
            Logger.LogDebug("AutoSendChallenge is disabled, not sending WWW-Authenticate header");
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
        // Log when authentication events are initialized
        Logger.LogDebug("Kerberos authentication events initialized");
        return base.InitializeEventsAsync();
    }
}