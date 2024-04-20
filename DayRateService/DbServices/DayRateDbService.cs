﻿using LibDTO;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace DayRateService.DbServices
{
    public class DayRateDbService
    {
        private readonly IMongoCollection<DayRate> dayRateCollection;

        public DayRateDbService(
            IOptions<PricingSystemDataBaseConfig> pricingSystemDataBaseConfig)
        {
            var mongoClient = new MongoClient(
                pricingSystemDataBaseConfig.Value.ConnectionString);

            var mongoDatabase = mongoClient.GetDatabase(
                pricingSystemDataBaseConfig.Value.DatabaseName);

            dayRateCollection = mongoDatabase.GetCollection<DayRate>(
                pricingSystemDataBaseConfig.Value.DayRateCollectionName);
        }

        public async Task<List<DayRate>> GetAllAsync() =>
            await dayRateCollection.Find(_ => true).ToListAsync();

        public async Task<DayRate?> GetAsync(string id) =>
            await dayRateCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

        public async Task<bool> CreateAsync(DayRate dayRate) 
        {
            try
            {
                await dayRateCollection.InsertOneAsync(dayRate);
                return true;
            }
            catch (Exception _) { }
            return false;
        }
            

        public async Task<bool> UpdateAsync(string id, DayRate dayRate) =>
            (await dayRateCollection.ReplaceOneAsync(x => x.Id == id, dayRate)).IsAcknowledged;

        public async Task<bool> RemoveAsync(string id) =>
            (await dayRateCollection.DeleteOneAsync(x => x.Id == id)).IsAcknowledged;
    }
}