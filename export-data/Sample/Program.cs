using Azure.Search.Documents;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Sample;
using Azure.Search.Documents.Indexes;
using Azure;
using Azure.Search.Documents.Indexes.Models;
using export_data;

// Before running this sample
// 1. Copy local.settings-example.json to local.settings.json
// 2. Fill in the sample values with actual values
var configuration = new Configuration();
new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("local.settings.json")
    .Build()
    .Bind(configuration);
configuration.Validate();

var endpoint = new Uri(configuration.ServiceEndpoint);
var defaultCredential = new DefaultAzureCredential();
var adminKey = !string.IsNullOrEmpty(configuration.AdminKey) ? new AzureKeyCredential(configuration.AdminKey) : null;
var searchIndexClient = adminKey != null ? new SearchIndexClient(endpoint, adminKey) : new SearchIndexClient(endpoint, defaultCredential);

var fieldBuilder = new FieldBuilder();
var searchFields = fieldBuilder.Build(typeof(Document));
var indexDefinition = new SearchIndex(configuration.IndexName, searchFields);
await searchIndexClient.CreateOrUpdateIndexAsync(indexDefinition);

var searchClient = searchIndexClient.GetSearchClient(indexDefinition.Name);

// Upload randomly generated documents
using (var bufferedSender = new SearchIndexingBufferedSender<Document>(searchClient))
{
    const int DocumentCount = 500000;
    DateTimeOffset start = new DateTimeOffset(2023, 01, 01, 0, 0, 0, TimeSpan.Zero);
    DateTimeOffset end = new DateTimeOffset(2024, 01, 01, 0, 0, 0, TimeSpan.Zero);
    var random = new Random();
    for (int i = 0; i < DocumentCount; i++)
    {
        bufferedSender.UploadDocuments(
            new[] {
                new Document {
                    Id = Convert.ToString(i),
                    // Get the next date by adding a random amount of ticks between the end and start dates
                    Timestamp = start + (random.NextDouble() * (end-start))
                }
            });
    }
}

// Demonstrate how to use partition export
SearchField timestampField = searchFields
    .Where(field => field.Type == SearchFieldDataType.DateTimeOffset)
    .Single();
object lowerBound = await Bound.FindLowerBoundAsync(timestampField, searchClient);
object upperBound = await Bound.FindUpperBoundAsync(timestampField, searchClient);
List<Partition> partitions = await new PartitionGenerator(searchClient, timestampField, lowerBound, upperBound).GeneratePartitions();

var partitionFile = new PartitionFile
{
    Endpoint = endpoint.AbsoluteUri,
    IndexName = indexDefinition.Name,
    FieldName = timestampField.Name,
    TotalDocumentCount = partitions.Sum(partition => partition.DocumentCount),
    Partitions = partitions
};

if (!Directory.Exists(configuration.ExportDirectory))
{
    Directory.CreateDirectory(configuration.ExportDirectory);
}
var partitionFilePath = Path.Combine(configuration.ExportDirectory, $"{indexDefinition.Name}-partitions.json");
partitionFile.SerializeToFile(partitionFilePath);

var partitionWriter = new FilePartitionWriter(configuration.ExportDirectory, indexDefinition.Name);
await new PartitionExporter(
    partitionFile,
    partitionWriter,
    searchClient,
    indexDefinition,
    concurrentPartitions: 2,
    pageSize: 1000).ExportAsync();