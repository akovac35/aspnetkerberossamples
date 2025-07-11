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
}

public class KerberosAuthHandler : AuthenticationHandler<KerberosAuthOptions>
{
    private readonly ILoggerFactory loggerFactory;
    private KeyTable? keytab;

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
            // Load keytab during initialization to improve performance
            using (var fs = new FileStream(Options.KeytabPath ?? throw new InvalidOperationException(), FileMode.Open, FileAccess.Read))
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

        try
        {
            // Correctly create the validator with logger factory and set ValidationActions
            var kerbValidator = new KerberosValidator(keytab, loggerFactory);
            kerbValidator.ValidateAfterDecrypt = ValidationActions.All;

            // Use Validate method, not Authenticate
            Logger.LogDebug("Validating Kerberos token with validator actions {ValidationActions}", kerbValidator.ValidateAfterDecrypt);
            var decryptedApReq = await kerbValidator.Validate(Convert.FromBase64String(token));

            // Get the authenticated user
            var authenticatedUser = decryptedApReq.Ticket.CName.FullyQualifiedName;

            Logger.LogInformation("Successfully authenticated user: {Username}", authenticatedUser);

            // Create claims identity
            var claims = new[]
            {
                    new Claim(ClaimTypes.Name, authenticatedUser),
                    new Claim(ClaimTypes.AuthenticationMethod, "Kerberos")
                };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Kerberos authentication failed");
            return AuthenticateResult.Fail(ex);
        }
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Options.AutoSendChallenge)
        {
            Response.Headers.Append("WWW-Authenticate", "Negotiate");
            Logger.LogDebug("Sending Negotiate challenge");
        }

        await base.HandleChallengeAsync(properties);
    }
}