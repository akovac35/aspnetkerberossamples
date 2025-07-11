using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(options => {
    options.DefaultAuthenticateScheme = "Kerberos";
    options.DefaultChallengeScheme = "Kerberos";
})
.AddScheme<KerberosAuthOptions, KerberosAuthHandler>("Kerberos", options => {
    options.KeytabPath = builder.Configuration["Kerberos:KeytabPath"];
    options.ServicePrincipalName = builder.Configuration["Kerberos:ServicePrincipalName"];
    options.AutoSendChallenge = true;
});

builder.Services.AddAuthorization();



var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
 
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapGet("/secure", (ClaimsPrincipal user) => {
    return $"Hello, {user.Identity?.Name}! You are authenticated.";
}).RequireAuthorization();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
