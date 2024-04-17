using LibDTO;
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

        public async Task<List<DayRate>> GetAsync() =>
            await dayRateCollection.Find(_ => true).ToListAsync();

        public async Task<DayRate?> GetAsync(string id) =>
            await dayRateCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

        public async Task CreateAsync(DayRate newBook) =>
            await dayRateCollection.InsertOneAsync(newBook);

        public async Task UpdateAsync(string id, DayRate updatedBook) =>
            await dayRateCollection.ReplaceOneAsync(x => x.Id == id, updatedBook);

        public async Task RemoveAsync(string id) =>
            await dayRateCollection.DeleteOneAsync(x => x.Id == id);
    }
}
