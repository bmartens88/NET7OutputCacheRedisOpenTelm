using System.Diagnostics;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace MyApi.Caching;

public static class OutputCacheExtensions
{
    public static IServiceCollection AddRedisOutputCache(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
        services.AddSingleton<IOutputCacheStore>(sp =>
        {
            var distributedCache = sp.GetRequiredService<IDistributedCache>();
            var redisCacheOptions = sp.GetRequiredService<IOptions<RedisCacheOptions>>();
            return new RedisOutputCacheStore(distributedCache, redisCacheOptions);
        });

        return services;
    }
}

public sealed class RedisOutputCacheStore : IOutputCacheStore
{
    private const string SetScript = @"
                redis.call('HSET', KEYS[1], 'absexp', ARGV[1], 'sldexp', ARGV[2], 'data', ARGV[4])
                if ARGV[3] ~= '-1' then
                  redis.call('EXPIRE', KEYS[1], ARGV[3])
                end
                return 1";

    private const string SetScriptPreExtendedSetCommand = @"
                redis.call('HMSET', KEYS[1], 'absexp', ARGV[1], 'sldexp', ARGV[2], 'data', ARGV[4])
                if ARGV[3] ~= '-1' then
                  redis.call('EXPIRE', KEYS[1], ARGV[3])
                end
                return 1";

    private static readonly Version ServerVersionWithExtendedSetCommand = new(4, 0, 0);
    private readonly IDistributedCache _distributedCache;
    private readonly RedisCacheOptions _options;

    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private volatile IConnectionMultiplexer? _connection;
    private IDatabase? _cache;
    private string _setScript = SetScript;

    public RedisOutputCacheStore(IDistributedCache distributedCache, IOptions<RedisCacheOptions> redisCacheOptions)
    {
        _distributedCache = distributedCache;
        _options = redisCacheOptions.Value;
    }

    public async ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
        var memberKeys = _cache!.SetMembers($"tag_{tag}")
            .Select(x => x.ToString());
        var redisKeys = memberKeys.Select(x => new RedisKey(x)).ToArray();
        await _cache.KeyDeleteAsync(redisKeys).ConfigureAwait(false);
    }

    public async ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        return await _distributedCache.GetAsync(key, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor,
        CancellationToken cancellationToken)
    {
        var distributedCacheEntryOptions = new DistributedCacheEntryOptions
            { AbsoluteExpirationRelativeToNow = validFor };
        await _distributedCache.SetAsync(key, value, distributedCacheEntryOptions, cancellationToken);
        await AddKeyToTagSet(key, tags, cancellationToken).ConfigureAwait(false);
    }

    private async Task AddKeyToTagSet(string key, string[]? tags, CancellationToken cancellationToken)
    {
        if (tags is null)
            return;
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
        foreach (var tag in tags)
            await _cache!.SetAddAsync($"tag_{tag}", key).ConfigureAwait(false);
    }

    private async Task ConnectAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (_cache is not null)
        {
            Debug.Assert(_connection is not null);
            return;
        }

        await _connectionLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_cache is null)
                if (_options.ConnectionMultiplexerFactory is null)
                {
                    if (_options.ConfigurationOptions is not null)
                        _connection = await ConnectionMultiplexer.ConnectAsync(_options.ConfigurationOptions)
                            .ConfigureAwait(false);
                    else
                        _connection = await ConnectionMultiplexer.ConnectAsync(_options.Configuration)
                            .ConfigureAwait(false);
                }
                else
                {
                    _connection = await _options.ConnectionMultiplexerFactory().ConfigureAwait(false);
                }

            PrepareConnection();
            _cache = _connection.GetDatabase();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void PrepareConnection()
    {
        ValidateServerFeatures();
        TryRegisterProfiler();
    }

    private void ValidateServerFeatures()
    {
        _ = _connection ?? throw new InvalidOperationException($"{nameof(_connection)} cannot be null.");

        try
        {
            foreach (var endPoint in _connection.GetEndPoints())
                if (_connection.GetServer(endPoint).Version < ServerVersionWithExtendedSetCommand)
                {
                    _setScript = SetScriptPreExtendedSetCommand;
                    return;
                }
        }
        catch (NotSupportedException)
        {
            _setScript = SetScriptPreExtendedSetCommand;
        }
    }

    private void TryRegisterProfiler()
    {
        _ = _connection ?? throw new InvalidOperationException($"{nameof(_connection)} cannot be null.");

        if (_options.ProfilingSession != null) _connection.RegisterProfiler(_options.ProfilingSession);
    }
}