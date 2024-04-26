using LibDTO;
using LibDTO.Config;
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

            //mongoDatabase.CreateCollection(pricingSystemDataBaseConfig.Value.DayRateCollectionName);

            dayRateCollection = mongoDatabase.GetCollection<DayRate>(
                pricingSystemDataBaseConfig.Value.DayRateCollectionName);
        }

        public async Task<List<DayRate>> GetAllAsync() =>
            await dayRateCollection.Find(_ => true).ToListAsync();

        public async Task<DayRate?> GetAsync(string id) =>
            await dayRateCollection.Find(x => x.Id == StringTo24Hex(id)).FirstOrDefaultAsync();

        public async Task<bool> CreateAsync(DayRate dayRate) 
        {
            try
            {
                dayRate.Id = StringTo24Hex(dayRate.Id);
                await dayRateCollection.InsertOneAsync(dayRate);
                return true;
            }
            catch (Exception _) { }
            return false;
        }


        public async Task<bool> UpdateAsync(string id, DayRate dayRate)
        {
            try
            {
                dayRate.Id = StringTo24Hex(dayRate.Id);
                return (await dayRateCollection.ReplaceOneAsync(x => x.Id == StringTo24Hex(id), dayRate)).IsAcknowledged;
            }
            catch (Exception _) { }
            return false;
        }

        public async Task<bool> RemoveAsync(string id) =>
            (await dayRateCollection.DeleteOneAsync(x => x.Id == StringTo24Hex(id))).IsAcknowledged;


        public static string StringTo24Hex(string id)
        {
            return Int32.TryParse(id, out _)? Int32.Parse(id).ToString("x").PadLeft(24, '0').ToUpper() : id;
        }
    }
}
