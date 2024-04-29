using LibDTO;
using LibDTO.Config;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace TimeIntervalService.DbServices
{
    public class TimeIntervalDbService
    {
        private readonly IMongoCollection<TimeInterval>timeIntervalCollection;

        public TimeIntervalDbService(
            IOptions<PricingSystemDataBaseConfig> pricingSystemDataBaseConfig)
        {
            var mongoClient = new MongoClient(
                pricingSystemDataBaseConfig.Value.ConnectionString);

            var mongoDatabase = mongoClient.GetDatabase(
                pricingSystemDataBaseConfig.Value.DatabaseName);

            //mongoDatabase.CreateCollection(pricingSystemDataBaseConfig.Value.timeIntervalCollectionName);

            timeIntervalCollection = mongoDatabase.GetCollection<TimeInterval>(
                pricingSystemDataBaseConfig.Value.TimeIntervalCollectionName);
        }

        public async Task<List<TimeInterval>> GetAllAsync() =>
            await timeIntervalCollection.Find(_ => true).ToListAsync();

        public async Task<TimeInterval?> GetAsync(string id) =>
            await timeIntervalCollection.Find(x => x.Id == StringTo24Hex(id)).FirstOrDefaultAsync();

        public async Task<bool> CreateAsync(TimeInterval timeInterval) 
        {
            try
            {
                timeInterval.Id = StringTo24Hex(timeInterval.Id);
                await timeIntervalCollection.InsertOneAsync(timeInterval);
                return true;
            }
            catch (Exception _) { }
            return false;
        }


        public async Task<bool> UpdateAsync(string id, TimeInterval timeInterval)
        {
            try
            {
                timeInterval.Id = StringTo24Hex(timeInterval.Id);
                return (await timeIntervalCollection.ReplaceOneAsync(x => x.Id == StringTo24Hex(id), timeInterval)).IsAcknowledged;
            }
            catch (Exception _) { }
            return false;
        }

        public async Task<bool> RemoveAsync(string id) =>
            (await timeIntervalCollection.DeleteOneAsync(x => x.Id == StringTo24Hex(id))).IsAcknowledged;


        public static string StringTo24Hex(string id)
        {
            return Int32.TryParse(id, out _)? Int32.Parse(id).ToString("x").PadLeft(24, '0').ToUpper() : id;
        }
    }
}
