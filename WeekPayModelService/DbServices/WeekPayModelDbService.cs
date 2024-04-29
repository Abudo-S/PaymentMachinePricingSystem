using LibDTO;
using LibDTO.Config;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace WeekPayModelService.DbServices
{
    public class WeekPayModelDbService
    {
        private readonly IMongoCollection<WeekPayModel> weekPayModelCollection;

        public WeekPayModelDbService(
            IOptions<PricingSystemDataBaseConfig> pricingSystemDataBaseConfig)
        {
            var mongoClient = new MongoClient(
                pricingSystemDataBaseConfig.Value.ConnectionString);

            var mongoDatabase = mongoClient.GetDatabase(
                pricingSystemDataBaseConfig.Value.DatabaseName);

            //mongoDatabase.CreateCollection(pricingSystemDataBaseConfig.Value.WeekPayModelCollectionName);

            weekPayModelCollection = mongoDatabase.GetCollection<WeekPayModel>(
                pricingSystemDataBaseConfig.Value.WeekPayModelCollectionName);
        }

        public async Task<List<WeekPayModel>> GetAllAsync() =>
            await weekPayModelCollection.Find(_ => true).ToListAsync();

        public async Task<WeekPayModel?> GetAsync(string id) =>
            await weekPayModelCollection.Find(x => x.Id == StringTo24Hex(id)).FirstOrDefaultAsync();

        public async Task<bool> CreateAsync(WeekPayModel weekPayModel) 
        {
            try
            {
                weekPayModel.Id = StringTo24Hex(weekPayModel.Id);
                await weekPayModelCollection.InsertOneAsync(weekPayModel);
                return true;
            }
            catch (Exception _) { }
            return false;
        }


        public async Task<bool> UpdateAsync(string id, WeekPayModel weekPayModel)
        {
            try
            {
                weekPayModel.Id = StringTo24Hex(weekPayModel.Id);
                return (await weekPayModelCollection.ReplaceOneAsync(x => x.Id == StringTo24Hex(id), weekPayModel)).IsAcknowledged;
            }
            catch (Exception _) { }
            return false;
        }

        public async Task<bool> RemoveAsync(string id) =>
            (await weekPayModelCollection.DeleteOneAsync(x => x.Id == StringTo24Hex(id))).IsAcknowledged;


        public static string StringTo24Hex(string id)
        {
            return Int32.TryParse(id, out _)? Int32.Parse(id).ToString("x").PadLeft(24, '0').ToUpper() : id;
        }
    }
}
