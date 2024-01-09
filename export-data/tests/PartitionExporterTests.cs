using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using export_data;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace tests
{
    [TestClass]
    public class PartitionExporterTests
    {
        // Add seed to random for deterministic tests
        private static readonly Random random = new Random(0);
        private const string TestIndexName = "ParititionExporterTest";
        private const int TestDocumentCount = 525;
        private const int TestPartitionSize = 50;
        private static SearchIndexClient SearchIndexClient { get; set; }
        private static SearchClient SearchClient { get; set; }
        private static SearchField BoundField { get; } = new SearchField(name: "timestamp", SearchFieldDataType.DateTimeOffset);
        private static string KeyField { get; } = "id";
        private static readonly DateTimeOffset startDate = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset[] DocumentIdToTimestamp = new DateTimeOffset[TestDocumentCount];

        [ClassInitialize]
        public static void ClassInitialize(TestContext _)
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile("config.json")
                .Build();
            var credential = new DefaultAzureCredential();
            SearchIndexClient = new SearchIndexClient(new Uri(configuration["searchEndpoint"]), credential);
            SearchIndexClient.CreateOrUpdateIndex(GetTestIndexDefinition());
            var searchClient = new SearchClient(SearchIndexClient.Endpoint, TestIndexName, credential);
            foreach (SearchDocument[] batch in SetupTestData())
            {
                searchClient.UploadDocuments(batch);
            }
        }

        [ClassCleanup]
        public static void ClassCleanup(TestContext _)
        {
            SearchIndexClient.DeleteIndex(TestIndexName);
        }

        [TestMethod]
        public async Task TestPartitionExporter()
        {
            var lowerBound = await Bound.FindLowerBoundAsync(BoundField, SearchClient);
            var upperBound = await Bound.FindUpperBoundAsync(BoundField, SearchClient);


            List<Partition> partitions = await new PartitionGenerator(SearchClient, BoundField, lowerBound, upperBound, partitionMaximumDocumentCount: TestPartitionSize).GeneratePartitions();
            var partitionFile = new PartitionFile
            {
                Endpoint = SearchIndexClient.Endpoint.AbsoluteUri,
                IndexName = TestIndexName,
                FieldName = BoundField.Name,
                TotalDocumentCount = partitions.Sum(partition => partition.DocumentCount),
                Partitions = partitions
            };

            var mockPartitionWriter = new MockPartitionWriter(KeyField);
            var partitionExporter = new PartitionExporter(
                partitionFile,
                mockPartitionWriter,
                SearchClient,
                GetTestIndexDefinition(),
                concurrentPartitions: 2,
                pageSize: 1000,
                partitionIdsToInclude: null,
                partitionIdsToExclude: null,
                fieldsToInclude: null,
                fieldsToExclude: null);

            await partitionExporter.ExportAsync();
        }

        private static SearchIndex GetTestIndexDefinition() =>
            new SearchIndex(TestIndexName)
            {
                Fields =
                {
                    new SearchField(name: "id", SearchFieldDataType.String),
                    new SearchField(name: "timestamp", SearchFieldDataType.DateTimeOffset),
                    new SearchField(name: "value", SearchFieldDataType.String)
                }
            };

        private static IEnumerable<SearchDocument[]> SetupTestData()
        {
            int batchSize = 10000;
            for (int i = 0; i < TestDocumentCount; i += batchSize)
            {
                var documents = new SearchDocument[batchSize];
                for (int j = 0; j < batchSize; j++)
                {
                    int id = i + j;
                    var timestamp = DateTimeOffsetForDocument(id);
                    DocumentIdToTimestamp[id] = timestamp;
                    documents[j] = new SearchDocument(new Dictionary<string, object>
                    {
                        ["id"] = id.ToString(),
                        ["timestamp"] = timestamp
                    });

                }
                yield return documents;
            }
        }

        // Document's timestamp is the start time + its id so partitions are ordered by document id
        private static DateTimeOffset DateTimeOffsetForDocument(int documentId) =>
            startDate.AddDays(documentId);
    }
}