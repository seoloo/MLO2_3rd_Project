using MongoDB.Bson;
using MongoDB.Driver;

namespace QaRunner.Pipeline;

/// <summary>
/// 실제 MongoDB의 메달리온 컬렉션에서 QA가 확인할 건수를 읽는다 (docs/contract.md 5-2장).
/// 아직 실제 MongoDB로 확인하지 못한 코드다. 컬렉션·필드 이름이 바뀌면 이 파일의 상수만 고친다.
/// </summary>
public sealed class MongoPipelineReader : IPipelineReader
{
    // 계약 5-2장의 컬렉션 이름
    public const string BronzeCollection = "bronze_events";
    public const string SilverCollection = "silver_events";
    public const string QuarantineCollection = "quarantine_events";
    public const string GoldCollection = "gold_metrics";

    // MongoDB에 연결하지 못하면 오래 기다리지 않고 바로 실패로 알린다
    private static readonly TimeSpan ServerSelectionTimeout = TimeSpan.FromSeconds(5);

    private readonly IMongoDatabase _database;

    /// <param name="connectionString">MongoDB 연결 문자열. 예: mongodb://localhost:27017</param>
    /// <param name="databaseName">메달리온 컬렉션이 있는 데이터베이스</param>
    public MongoPipelineReader(string connectionString, string databaseName)
    {
        var clientSettings = MongoClientSettings.FromConnectionString(connectionString);
        clientSettings.ServerSelectionTimeout = ServerSelectionTimeout;
        _database = new MongoClient(clientSettings).GetDatabase(databaseName);
    }

    public async Task<CaseCounts> ReadCaseCountsAsync(string runId, string caseId, CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter;

        // Bronze·격리는 받은 이벤트를 raw 안에 그대로 두고, Silver는 필드를 꺼내 둔다 (계약 5-2장)
        var rawEventFilter = filter.Eq("raw.runId", runId) & filter.Eq("raw.caseId", caseId);
        var silverFilter = filter.Eq("runId", runId) & filter.Eq("caseId", caseId);

        return await ReadSafelyAsync(async () => new CaseCounts(
            BronzeCount: await CountAsync(BronzeCollection, rawEventFilter, cancellationToken),
            SilverCount: await CountAsync(SilverCollection, silverFilter, cancellationToken),
            QuarantineCount: await CountAsync(QuarantineCollection, rawEventFilter, cancellationToken)));
    }

    public async Task<IReadOnlyDictionary<DateTimeOffset, int>> ReadGoldCountsAsync(
        IReadOnlyCollection<DateTimeOffset> windowStarts, CancellationToken cancellationToken)
    {
        var windowDates = windowStarts.Select(windowStart => windowStart.UtcDateTime).ToList();
        var goldFilter = Builders<BsonDocument>.Filter.In("windowStart", windowDates);

        var goldDocuments = await ReadSafelyAsync(() => _database
            .GetCollection<BsonDocument>(GoldCollection)
            .Find(goldFilter)
            .ToListAsync(cancellationToken));

        var totalByWindow = goldDocuments.ToDictionary(
            goldDocument => new DateTimeOffset(goldDocument["windowStart"].ToUniversalTime()),
            goldDocument => goldDocument["total"].ToInt32());

        // 아직 Gold 문서가 없는 구간은 0건으로 본다
        return windowStarts.ToDictionary(windowStart => windowStart, windowStart => totalByWindow.GetValueOrDefault(windowStart));
    }

    // 컬렉션에서 조건에 맞는 문서 수를 센다
    private async Task<int> CountAsync(string collectionName, FilterDefinition<BsonDocument> filter, CancellationToken cancellationToken) =>
        (int)await _database.GetCollection<BsonDocument>(collectionName)
            .CountDocumentsAsync(filter, cancellationToken: cancellationToken);

    // MongoDB에 연결하지 못했거나 읽다가 실패하면 PipelineReadException으로 바꿔 알린다
    private static async Task<T> ReadSafelyAsync<T>(Func<Task<T>> readAsync)
    {
        try
        {
            return await readAsync();
        }
        catch (Exception exception) when (exception is MongoException or TimeoutException)
        {
            throw new PipelineReadException($"MongoDB에서 읽지 못했습니다: {exception.Message}", exception);
        }
    }
}
