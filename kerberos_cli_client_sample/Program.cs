using System.Net;
using System.Text.Json;

Console.WriteLine("Kerberos CLI Client Sample");
Console.WriteLine("===========================\n");

// Get the API base URL from command line or use default
var apiBaseUrl = args.Length > 0 ? args[0] : "https://linux-server.example.local:5001";
Console.WriteLine($"API Base URL: {apiBaseUrl}\n");

// Create HttpClient with Windows credentials
var handler = new HttpClientHandler
{
    // This enables automatic Windows/Kerberos authentication
    UseDefaultCredentials = true,
    // Allow cookies for session management
    UseCookies = true,
    CookieContainer = new CookieContainer(),
    // Development only - in production, validate certificates properly
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
};

using var client = new HttpClient(handler)
{
    BaseAddress = new Uri(apiBaseUrl)
};

try
{
    Console.WriteLine("=== Testing Public Endpoint (no auth required) ===");
    var publicResponse = await client.GetAsync("/public");
    await PrintResponse(publicResponse);

    Console.WriteLine("\n=== Checking Authentication Status (before login) ===");
    var statusResponse1 = await client.GetAsync("/auth-status");
    await PrintResponse(statusResponse1);

    Console.WriteLine("\n=== Attempting to access Secure Endpoint (before login) ===");
    var secureResponse1 = await client.GetAsync("/secure");
    await PrintResponse(secureResponse1);

    Console.WriteLine("\n=== Logging In (Kerberos authentication) ===");
    var loginResponse = await client.GetAsync("/login");
    await PrintResponse(loginResponse);

    if (loginResponse.IsSuccessStatusCode)
    {
        Console.WriteLine("\n=== Checking Authentication Status (after login) ===");
        var statusResponse2 = await client.GetAsync("/auth-status");
        await PrintResponse(statusResponse2);

        Console.WriteLine("\n=== Accessing Secure Endpoint (after login) ===");
        var secureResponse2 = await client.GetAsync("/secure");
        await PrintResponse(secureResponse2);

        Console.WriteLine("\n=== Logging Out ===");
        var logoutResponse = await client.GetAsync("/logout");
        await PrintResponse(logoutResponse);

        Console.WriteLine("\n=== Checking Authentication Status (after logout) ===");
        var statusResponse3 = await client.GetAsync("/auth-status");
        await PrintResponse(statusResponse3);

        Console.WriteLine("\n=== Attempting to access Secure Endpoint (after logout) ===");
        var secureResponse3 = await client.GetAsync("/secure");
        await PrintResponse(secureResponse3);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}

Console.WriteLine("\n=== Test Complete ===");
Console.WriteLine("Press any key to exit...");
Console.ReadKey();

static async Task PrintResponse(HttpResponseMessage response)
{
    Console.WriteLine($"Status: {(int)response.StatusCode} {response.StatusCode}");
    
    var content = await response.Content.ReadAsStringAsync();
    
    try
    {
        // Try to pretty-print JSON
        var jsonDoc = JsonDocument.Parse(content);
        var prettyJson = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"Response: {prettyJson}");
    }
    catch
    {
        // If not JSON, just print as-is
        Console.WriteLine($"Response: {content}");
    }
}
