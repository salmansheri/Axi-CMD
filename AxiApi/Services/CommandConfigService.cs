using AxiApi.Observability;
using ARMCommon.Interface;
using AxExtend.Interface;
using AxiApi.DTOs;
using AxiApi.Interfaces;
using AxiApi.Lib.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AxiApi.Services
{
    public class CommandConfigService : ICommandConfigService
    {
        private readonly ICommandConfigRepository _repository;
        private readonly IAxExtend _axExtend;
        private readonly ILogger<CommandConfigService> _logger;

        public CommandConfigService(
            ICommandConfigRepository repository,
            IAxExtend axExtend,
            ILogger<CommandConfigService> logger)
        {
            _repository = repository;
            _axExtend = axExtend;
            _logger = logger;
        }

        public async Task<List<CommandConfigDTO>> GetCommandConfigsAsync(string appname, string username, bool forceRefresh = false)
        {
            if (string.IsNullOrWhiteSpace(appname) || string.IsNullOrWhiteSpace(username))
            {
                _logger.LogWarning("GetCommandConfigsAsync called with empty appname or username");
                return new List<CommandConfigDTO>();
            }

            _logger.LogInformation("CommandConfigService: Starting load sequence for app: {AppName}, User: {Username}, ForceRefresh: {ForceRefresh}", appname, username, forceRefresh);

            string cacheKey = Keygenerator.GenerateCacheKey(appname, "command_config", username);
            IRedisCacheHelper? redisCache = null;
            var isRedisConnected = await AxiTelemetry.DependencyAsync("cache", "connect", () => _axExtend.OpenRedisConnectionAsync(appname));
            var fallback = !isRedisConnected;
            if (forceRefresh) AxiTelemetry.Cache("command-config", "force_refresh");

            if (isRedisConnected)
            {
                try
                {
                    redisCache = await _axExtend.GetRedis();
                    if (forceRefresh)
                    {
                        _logger.LogInformation("CommandConfigService: Force refresh requested, clearing Redis cache key: {CacheKey}", cacheKey);
                        await AxiTelemetry.DependencyAsync("cache", "invalidate", () => redisCache.KeysDeleteAsync(new RedisKey[] { cacheKey }));
                    }
                    else
                    {
                        string cachedString = await AxiTelemetry.DependencyAsync("cache", "read", () => redisCache.StringGetAsync(cacheKey));
                        if (!string.IsNullOrEmpty(cachedString))
                        {
                            var cachedList = JsonConvert.DeserializeObject<List<CommandConfigDTO>>(cachedString);
                            if (cachedList != null && cachedList.Count > 0)
                            {
                                AxiTelemetry.Cache("command-config", "hit");
                                _logger.LogInformation("CommandConfigService: Redis Cache Hit with {Count} records for app: {AppName}", cachedList.Count, appname);
                                return cachedList;
                            }
                        }
                        AxiTelemetry.Cache("command-config", "miss");
                    }
                }
                catch (RedisException ex)
                {
                    fallback = true;
                    _logger.LogWarning(ex, "CommandConfigService: Redis operation failed. Falling back to database.");
                }
            }

            _logger.LogInformation("CommandConfigService: Fetching command configs from database for app: {AppName}", appname);
            var configs = await _repository.GetCommandConfigsAsync(appname);
            if (fallback) AxiTelemetry.Cache("command-config", "successful_fallback");

            if (configs != null && configs.Count > 0 && isRedisConnected && redisCache != null)
            {
                try
                {
                    var written = await AxiTelemetry.DependencyAsync("cache", "write", () => redisCache.StringSetAsync(cacheKey, JsonConvert.SerializeObject(configs)));
                    if (!written) AxiTelemetry.Cache("command-config", "write_failure");
                    _logger.LogInformation("CommandConfigService: Saved {Count} command configs to Redis cache for key: {CacheKey}", configs.Count, cacheKey);
                }
                catch (RedisException ex)
                {
                    AxiTelemetry.Cache("command-config", "write_failure");
                    _logger.LogWarning(ex, "CommandConfigService: Failed to write command configs to Redis cache.");
                }
            }

            return configs ?? new List<CommandConfigDTO>();
        }
    }
}
