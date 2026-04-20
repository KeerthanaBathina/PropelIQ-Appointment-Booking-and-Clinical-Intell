using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement.Mvc;
using UPACIP.Api.Configuration;
using UPACIP.Service.Caching;

namespace UPACIP.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild",
        "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    private const string CacheKey = "weather:forecast";
    private readonly ICacheService _cache;

    public WeatherForecastController(ICacheService cache)
    {
        _cache = cache;
    }

    // [FeatureGate] demonstration: this endpoint requires WaitlistManagement = true.
    // When the flag is false in appsettings.json the DisabledFeaturesHandler returns
    // 404 { "error": "Feature is currently disabled", "feature": "WaitlistManagement" }.
    [FeatureGate(FeatureFlags.WaitlistManagement)]
    [HttpGet(Name = "GetWeatherForecast")]
    public async Task<IEnumerable<WeatherForecast>> Get()
    {
        // Cache-aside: returns the cached forecast or generates and caches a new one.
        // Demonstrates ICacheService.GetOrSetAsync with the default 5-minute TTL (NFR-030).
        return await _cache.GetOrSetAsync(
            CacheKey,
            factory: () => Task.FromResult<IEnumerable<WeatherForecast>?>(
                Enumerable.Range(1, 5).Select(index => new WeatherForecast
                {
                    Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    TemperatureC = Random.Shared.Next(-20, 55),
                    Summary = Summaries[Random.Shared.Next(Summaries.Length)]
                }).ToList()))
            ?? [];
    }
}

public record WeatherForecast
{
    public DateOnly Date { get; init; }
    public int TemperatureC { get; init; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    public string? Summary { get; init; }
}

