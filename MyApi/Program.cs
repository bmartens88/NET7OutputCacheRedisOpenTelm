using Microsoft.EntityFrameworkCore;
using MyApi.Caching;
using MyApi.Data.Context;
using MyApi.Extensions;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.ConfigurationOptions = new ConfigurationOptions
    {
        EndPoints = { "localhost:6379" },
        Ssl = false
    };
});
builder.Services.AddRedisOutputCache();
builder.Services.AddDbContextPool<AppDbContext>(
    opts => opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();
app.UseHttpsRedirection();
app.UseOutputCache();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", async () =>
{
    await Task.Delay(1000);
    var forecast = Enumerable.Range(1, 5).Select(index =>
            new WeatherForecast(
                DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                Random.Shared.Next(-20, 55),
                summaries[Random.Shared.Next(summaries.Length)]))
        .ToArray();
    return forecast;
}).CacheOutput();

app.MapGet("/products", async (AppDbContext context, CancellationToken ctx) =>
{
    var products = await context.Products.ToListAsync(ctx);
    var res = products.Select(p => p.AsDto()).ToList();
    return Results.Ok(res);
}).CacheOutput();

app.Run();

internal record WeatherForecast(DateOnly Date, int TemparetureC, string? Summary)
{
    public int TemperaturF => 32 + (int)(TemparetureC / 0.5556);
}