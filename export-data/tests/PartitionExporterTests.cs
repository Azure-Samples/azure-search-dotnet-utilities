using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using export_data;
using Microsoft.Extensions.Configuration;

namespace tests
{
    [TestClass]
    public class PartitionExporterTests
    {
        private const string TestIndexName = "partition-exporter-test";
        private const int TestDocumentCount = 1000;
        private const int TestPartitionSize = 125;
        private static SearchIndexClient SearchIndexClient { get; set; }
        private static SearchClient SearchClient { get; set; }
        private static SearchField BoundField { get; } = new SearchField(name: "timestamp", SearchFieldDataType.DateTimeOffset);
        private static string KeyField { get; } = "id";
        private static readonly DateTimeOffset startDate = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);

        [ClassInitialize]
        public static void ClassInitialize(TestContext _)
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile("config.json")
                .Build();
            var credential = new DefaultAzureCredential();
            SearchIndexClient = new SearchIndexClient(new Uri(configuration["searchEndpoint"]), credential);
            SearchIndexClient.CreateOrUpdateIndex(GetTestIndexDefinition());
            SearchClient = new SearchClient(SearchIndexClient.Endpoint, TestIndexName, credential);
            foreach (IEnumerable<SearchDocument> batch in SetupTestData())
            {
                SearchClient.UploadDocuments(batch);
            }

            // Wait for updates to propogate
            Task.Delay(TimeSpan.FromSeconds(3)).Wait();
        }

        [ClassCleanup]
        public static void ClassCleanup()
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
                partitionIdsToInclude: Enumerable.Range(0, partitions.Count).ToArray(),
                partitionIdsToExclude: null,
                fieldsToInclude: null,
                fieldsToExclude: null);

            await partitionExporter.ExportAsync();

            IReadOnlyDictionary<int, IReadOnlyDictionary<string, SearchDocument>> exportedPartitions = mockPartitionWriter.GetExportedPartitions();
            Assert.AreEqual(8, exportedPartitions.Count, $"Got {exportedPartitions.Count} partitions, expected 11");
            for (int i = 0; i < 8; i++)
            {
                IReadOnlyDictionary<string, SearchDocument> partition = exportedPartitions[i];
                Assert.AreEqual(125, partition.Count, $"Unexpected partition length {partition.Count} for partition {i}");

                for (int j = 0; j < partition.Count; j++)
                {
                    int expectedId = (i * 125) + j;
                    SearchDocument partitionedDocument = partition.GetValueOrDefault(expectedId.ToString());
                    Assert.IsNotNull(partitionedDocument, $"Missing document {expectedId} in partition {i}");
                    string actualTimestamp = partitionedDocument["timestamp"].ToString();
                    string expectedTimestamp = DateTimeOffsetForDocument(expectedId).ToString("yyyy-MM-ddTHH:mm:ssZ");
                    Assert.AreEqual(expectedTimestamp, actualTimestamp);
                }
            }
        }

        private static SearchIndex GetTestIndexDefinition() =>
            new SearchIndex(TestIndexName)
            {
                Fields =
                {
                    new SearchField(name: "id", SearchFieldDataType.String) { IsKey = true },
                    new SearchField(name: "timestamp", SearchFieldDataType.DateTimeOffset)
                }
            };

        private static IEnumerable<IEnumerable<SearchDocument>> SetupTestData()
        {
            var batch = new List<SearchDocument>();
            for (int i = 0; i < TestDocumentCount; i++)
            {
                var timestamp = DateTimeOffsetForDocument(i);
                batch.Add(new SearchDocument(new Dictionary<string, object>
                {
                    ["id"] = i.ToString(),
                    ["timestamp"] = timestamp
                }));
                if (batch.Count >= 1000)
                {
                    yield return batch;
                    batch = new List<SearchDocument>();
                }
            }

            if (batch.Count > 0)
            {
                yield return batch;
            }
        }

        // Document's timestamp is the start time + its id so partitions are ordered by document id
        private static DateTimeOffset DateTimeOffsetForDocument(int documentId) =>
            startDate.AddDays(documentId);
    }
}