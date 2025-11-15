using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Configure logging with timestamps
builder.Logging.ClearProviders();
builder.Logging.AddConsole()
    .AddSimpleConsole(options =>
    {
        options.IncludeScopes = false;
        options.SingleLine = true;
        options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
    });

// The Kerberos authentication is cached with cookies for session management
builder.Services.AddAuthentication(options => {
    options.DefaultAuthenticateScheme = "Cookies";
    options.DefaultChallengeScheme = "Cookies";
    options.DefaultSignInScheme = "Cookies";
})
.AddCookie("Cookies", options => {
    options.ExpireTimeSpan = TimeSpan.FromHours(8); // Session expires after 8 hours
    options.SlidingExpiration = true; // Extend session on activity
    options.Cookie.HttpOnly = true; // Security: prevent XSS
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // HTTPS only
    options.Cookie.SameSite = SameSiteMode.Lax; // CSRF protection
    
    // Configure events to return 403 instead of redirecting,
    // the user needs to authenticate first in the login endpoint
    options.Events.OnRedirectToLogin = context => {
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context => {
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
})
.AddScheme<KerberosAuthOptions, KerberosAuthHandler>("Kerberos", options => {
    options.KeytabPath = builder.Configuration["Kerberos:KeytabPath"];
    options.AutoSendChallenge = true;
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseHttpsRedirection();

// Login endpoint - performs Kerberos authentication and signs in with cookie
app.MapGet("/login", async (HttpContext context) => {
    // First try to authenticate with Kerberos
    var kerberosResult = await context.AuthenticateAsync("Kerberos");
    
    if (kerberosResult.Succeeded)
    {
        // Sign in with cookie scheme to maintain session
        await context.SignInAsync("Cookies", kerberosResult.Principal);
        return Results.Ok(new { 
            message = "Successfully authenticated and logged in",
            username = kerberosResult.Principal.Identity?.Name
        });
    }
    else
    {
        // Challenge with Kerberos if authentication failed
        await context.ChallengeAsync("Kerberos");
        return Results.Unauthorized();
    }
});

// Logout endpoint
app.MapGet("/logout", async (HttpContext context) => {
    await context.SignOutAsync("Cookies");
    return Results.Ok(new { message = "Successfully logged out" });
});

// Add a public endpoint for testing
app.MapGet("/public", () => Results.Ok(new { message = "This is a public endpoint - no authentication required" }));

app.MapGet("/secure", (ClaimsPrincipal user) => {
    return Results.Ok(new { message = $"Hello, {user.Identity?.Name}! You are authenticated via {user.Identity?.AuthenticationType}." });
}).RequireAuthorization();

// Add a test endpoint to check authentication status
app.MapGet("/auth-status", (ClaimsPrincipal user) => {
    if (user.Identity?.IsAuthenticated == true)
    {
        return Results.Ok(new { 
            authenticated = true, 
            username = user.Identity.Name,
            authType = user.Identity.AuthenticationType,
            claims = user.Claims.Select(c => new { c.Type, c.Value }).ToArray()
        });
    }
    return Results.Ok(new { authenticated = false });
});

// Help endpoint to explain the authentication flow
app.MapGet("/", () => Results.Content(@"
<h2>Kerberos Authentication Sample</h2>
<p>This sample demonstrates hybrid Kerberos + Cookie authentication.</p>
<h3>Endpoints:</h3>
<ul>
    <li><a href='/public'>/public</a> - No authentication required</li>
    <li><a href='/login'>/login</a> - Authenticate with Kerberos and create session cookie</li>
    <li><a href='/secure'>/secure</a> - Requires authentication (returns 403 if not authenticated)</li>
    <li><a href='/auth-status'>/auth-status</a> - Check current authentication status</li>
    <li><a href='/logout'>/logout</a> - Clear session cookie</li>
</ul>
<h3>Flow:</h3>
<ol>
    <li>Visit <a href='/login'>/login</a> first to authenticate with Kerberos</li>
    <li>Once authenticated, a session cookie is created</li>
    <li>Subsequent requests to <a href='/secure'>/secure</a> will use the cookie (no Kerberos challenge)</li>
    <li>If you access <a href='/secure'>/secure</a> without authentication, you'll get a 403 Forbidden response</li>
    <li>Use <a href='/logout'>/logout</a> to end the session</li>
</ol>
", "text/html"));

app.Run();