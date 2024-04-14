using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LibHelpers
{
    public static class DistributedCacheHelper
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

        public static async Task<bool> SetRecordAsync<T>(this IDistributedCache cache,
            string recordId,
            T dataObject,
            TimeSpan? absoluteExpireTime = null, //auto deletion
            TimeSpan? unusedExpireTime = null)
        {
            var result = false;

            try
            {
                var options = new DistributedCacheEntryOptions();

                options.AbsoluteExpirationRelativeToNow = absoluteExpireTime ?? TimeSpan.FromSeconds(60);
                options.SlidingExpiration = unusedExpireTime ?? TimeSpan.FromHours(23);

                var jsonData = JsonSerializer.Serialize(dataObject);
                await cache.SetStringAsync(recordId, jsonData, options);

                result = true;
            }
            catch (Exception e)
            {
                log.Error(e, $" In SetRecordAsync with recordId {recordId}");
            }

            return result;
        }

        public static async Task<T> GetRecordAsync<T>(this IDistributedCache cache, string recordId)
        {
            T dataObject = default(T);

            try
            {
                var jsonData = await cache.GetStringAsync(recordId);

                if (jsonData is not null)
                    dataObject = JsonSerializer.Deserialize<T>(jsonData);
            }
            catch (Exception e)
            {
                log.Error(e, $" In GetRecordAsync with recordId {recordId}");
            }

            return dataObject;
        }
        public static async Task<bool> RemoveRecordAsync<T>(this IDistributedCache cache, string recordId)
        {
            var result = false;

            try
            {
                await cache.RemoveAsync(recordId);
                result = true;
            }
            catch (Exception e)
            {
                log.Error(e, $" In RemoveRecordAsync with recordId {recordId}");
            }

            return result;
        }
    }
}
