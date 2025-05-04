// This is a prototype tool that allows for extraction of data from a search index
// Since this tool is still under development, it should not be used for production usage

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;

namespace AzureSearchBackupRestoreIndex;

class Program
{
    private static string SourceSearchServiceName;
    private static string SourceAdminKey;
    private static string SourceIndexName;
    private static string TargetSearchServiceName;
    private static string TargetAdminKey;
    private static string TargetIndexName;
    private static string BackupDirectory;

    private static SearchIndexClient SourceIndexClient;
    private static SearchClient SourceSearchClient;
    private static SearchIndexClient TargetIndexClient;
    private static SearchClient TargetSearchClient;

    private static int MaxBatchSize = 500;          // JSON files will contain this many documents / file and can be up to 1000
    private static int ParallelizedJobs = 10;       // Output content in parallel jobs

    static void Main()
    {
        //Get source and target search service info and index names from appsettings.json file
        //Set up source and target search service clients
        ConfigurationSetup();

        bool hasSourceService = !string.IsNullOrEmpty(SourceSearchServiceName);
        bool hasSourceIndex = !string.IsNullOrEmpty(SourceIndexName);
        bool hasSourceConfig = hasSourceService && hasSourceIndex;
        bool hasTargetConfig = !string.IsNullOrEmpty(TargetSearchServiceName) && !string.IsNullOrEmpty(TargetIndexName);
        bool hasBackupFiles = Directory.Exists(BackupDirectory) && Directory.GetFiles(BackupDirectory, "*.json").Length > 0;

        if (hasSourceConfig)
        {
            // Mode 1: Backup/Export source index
            Console.WriteLine("\nSTART INDEX BACKUP");
            BackupIndexAndDocuments();
        }

        if (hasTargetConfig)
        {
            if (hasSourceConfig)
            {
                // Mode 1: Full backup and restore
                Console.WriteLine("\nSTART INDEX RESTORE");
                DeleteIndex();
                CreateTargetIndex();
                ImportFromJSON();
            }
            else if (hasBackupFiles)
            {
                // Mode 3: Only restore from existing backup
                Console.WriteLine("\nSTART INDEX RESTORE FROM EXISTING BACKUP");
                DeleteIndex();
                CreateTargetIndex();
                ImportFromJSON();
            }
            else
            {
                Console.WriteLine("\nERROR: Cannot restore - no backup files found in {0}", BackupDirectory);
                return;
            }

            Console.WriteLine("\n  Waiting 10 seconds for target to index content...");
            Console.WriteLine("  NOTE: For really large indexes it may take longer to index all content.\n");
            Thread.Sleep(10000);

            // Validate all content is in target index
            int sourceCount = GetCurrentDocCount(SourceSearchClient);
            int targetCount = GetCurrentDocCount(TargetSearchClient);
            Console.WriteLine("\nSAFEGUARD CHECK: Source and target index counts should match");
            Console.WriteLine(" Source index contains {0} docs", sourceCount);
            Console.WriteLine(" Target index contains {0} docs\n", targetCount);
        }

        Console.WriteLine("Press any key to continue...");
        Console.ReadLine();
    }

    static void ConfigurationSetup()
    {
        IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
        IConfigurationRoot configuration = builder.Build();

        // Read all configuration values
        SourceSearchServiceName = configuration["SourceSearchServiceName"];
        SourceAdminKey = configuration["SourceAdminKey"];
        SourceIndexName = configuration["SourceIndexName"];
        TargetSearchServiceName = configuration["TargetSearchServiceName"];
        TargetAdminKey = configuration["TargetAdminKey"];
        TargetIndexName = configuration["TargetIndexName"];
        BackupDirectory = configuration["BackupDirectory"];

        // Print configuration based on what's available
        Console.WriteLine("CONFIGURATION:");
        Console.WriteLine($"SourceSearchServiceName: '{SourceSearchServiceName}'");
        Console.WriteLine($"SourceIndexName: '{SourceIndexName}'");
        
        bool hasSourceService = !string.IsNullOrEmpty(SourceSearchServiceName);
        bool hasSourceIndex = !string.IsNullOrEmpty(SourceIndexName);
        
        if (hasSourceService && hasSourceIndex)
        {
            Console.WriteLine("\n  Source service and index: {0}, {1}", SourceSearchServiceName, SourceIndexName);
        }
        if (!string.IsNullOrEmpty(TargetSearchServiceName) && !string.IsNullOrEmpty(TargetIndexName))
        {
            Console.WriteLine("\n  Target service and index: {0}, {1}", TargetSearchServiceName, TargetIndexName);
        }
        Console.WriteLine("\n  Backup directory: " + BackupDirectory);
        Console.WriteLine("\nDoes this look correct? Press any key to continue, Ctrl+C to cancel.");
        Console.ReadLine();

        // Only create source clients if source configuration is available
        if (hasSourceService && hasSourceIndex && !string.IsNullOrEmpty(SourceAdminKey))
        {
            try
            {
                SourceIndexClient = new SearchIndexClient(
                    new Uri("https://" + SourceSearchServiceName + ".search.windows.net"), 
                    new AzureKeyCredential(SourceAdminKey));
                SourceSearchClient = SourceIndexClient.GetSearchClient(SourceIndexName);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error creating source clients: {0}", ex.Message);
                throw;
            }
        }

        // Only create target clients if target configuration is available
        if (!string.IsNullOrEmpty(TargetSearchServiceName) && !string.IsNullOrEmpty(TargetAdminKey) && !string.IsNullOrEmpty(TargetIndexName))
        {
            try
            {
                TargetIndexClient = new SearchIndexClient(
                    new Uri("https://" + TargetSearchServiceName + ".search.windows.net"), 
                    new AzureKeyCredential(TargetAdminKey));
                TargetSearchClient = TargetIndexClient.GetSearchClient(TargetIndexName);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error creating target clients: {0}", ex.Message);
                throw;
            }
        }
    }

    static void BackupIndexAndDocuments()
    {
        // Backup the index schema to the specified backup directory
        Console.WriteLine("\n Backing up source index schema to {0}\n", Path.Combine(BackupDirectory, SourceIndexName + ".schema"));

        File.WriteAllText(Path.Combine(BackupDirectory, SourceIndexName + ".schema"), GetIndexSchema());

        // Extract the content to JSON files
        int SourceDocCount = GetCurrentDocCount(SourceSearchClient);
        WriteIndexDocuments(SourceDocCount);     // Output content from index to json files
    }

    static void WriteIndexDocuments(int CurrentDocCount)
    {
        // Write document files in batches (per MaxBatchSize) in parallel
        int FileCounter = 0;
        for (int batch = 0; batch <= (CurrentDocCount / MaxBatchSize); batch += ParallelizedJobs)
        {

            List<Task> tasks = new List<Task>();
            for (int job = 0; job < ParallelizedJobs; job++)
            {
                FileCounter++;
                int fileCounter = FileCounter;
                if ((fileCounter - 1) * MaxBatchSize < CurrentDocCount)
                {
                    Console.WriteLine(" Backing up source documents to {0} - (batch size = {1})", Path.Combine(BackupDirectory, SourceIndexName + fileCounter + ".json"), MaxBatchSize);

                    tasks.Add(Task.Factory.StartNew(() =>
                        ExportToJSON((fileCounter - 1) * MaxBatchSize, Path.Combine(BackupDirectory, $"{SourceIndexName}{fileCounter}.json"))
                    ));
                }

            }
            Task.WaitAll(tasks.ToArray());  // Wait for all the stored procs in the group to complete
        }

        return;
    }

    static void ExportToJSON(int Skip, string FileName)
    {
        // Extract all the documents from the selected index to JSON files in batches of 500 docs / file
        try
        {
            SearchOptions options = new SearchOptions()
            {
                SearchMode = SearchMode.All,
                Size = MaxBatchSize,
                Skip = Skip
            };

            SearchResults<SearchDocument> response = SourceSearchClient.Search<SearchDocument>("*", options);
            var documents = response.GetResults().ToList();

            if (documents.Count == 0)
            {
                Console.WriteLine("  No documents found in this batch");
                return;
            }

            // Create a list to hold all document JSON strings
            var jsonDocuments = new List<string>();

            foreach (var doc in documents)
            {
                string docJson = JsonSerializer.Serialize(doc.Document);
                // Handle GeoPoint fields if they exist
                docJson = docJson.Replace("\"Latitude\":", "\"type\": \"Point\", \"coordinates\": [");
                docJson = docJson.Replace("\"Longitude\":", "");
                docJson = docJson.Replace(",\"IsEmpty\":false,\"Z\":null,\"M\":null,\"CoordinateSystem\":{\"EpsgId\":4326,\"Id\":\"4326\",\"Name\":\"WGS84\"}", "]");
                jsonDocuments.Add(docJson);
            }

            // Create the final JSON structure
            string finalJson = "{\"value\": [" + string.Join(",", jsonDocuments) + "]}";

            // Validate the JSON before writing
            try
            {
                JsonDocument.Parse(finalJson);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"  Error: Generated invalid JSON: {ex.Message}");
                return;
            }

            // Write the validated JSON to file
            File.WriteAllText(FileName, finalJson);
            Console.WriteLine("  Total documents: {0}", documents.Count);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: {0}", ex.Message);
        }
    }

    static string GetIDFieldName()
    {
        // Find the id field of this index
        string IDFieldName = string.Empty;
        try
        {
            var schema = SourceIndexClient.GetIndex(SourceIndexName);
            foreach (var field in schema.Value.Fields)
            {
                if (field.IsKey == true)
                {
                    IDFieldName = Convert.ToString(field.Name);
                    break;
                }
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: {0}", ex.Message);
        }

        return IDFieldName;
    }

    static string GetIndexSchema()
    {
        // Extract the schema for this index
        // We use REST here because we can take the response as-is

        Uri ServiceUri = new Uri("https://" + SourceSearchServiceName + ".search.windows.net");
        HttpClient HttpClient = new HttpClient();
        HttpClient.DefaultRequestHeaders.Add("api-key", SourceAdminKey);

        string Schema = string.Empty;
        try
        {
            Uri uri = new Uri(ServiceUri, "/indexes/" + SourceIndexName);
            HttpResponseMessage response = AzureSearchHelper.SendSearchRequest(HttpClient, HttpMethod.Get, uri);
            AzureSearchHelper.EnsureSuccessfulSearchResponse(response);
            Schema = response.Content.ReadAsStringAsync().Result;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: {0}", ex.Message);
        }

        return Schema;
    }

    private static bool DeleteIndex()
    {
        Console.WriteLine("\n  Delete target index {0} in {1} search service, if it exists", TargetIndexName, TargetSearchServiceName);
        // Delete the index if it exists
        try
        {
            TargetIndexClient.DeleteIndex(TargetIndexName);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  Error deleting index: {0}\n", ex.Message);
            Console.WriteLine("  Did you remember to set your SearchServiceName and SearchServiceApiKey?\n");
            return false;
        }

        return true;
    }

    static void CreateTargetIndex()
    {
        Console.WriteLine("\n  Create target index {0} in {1} search service", TargetIndexName, TargetSearchServiceName);
        // Use the schema file to create a copy of this index
        // I like using REST here since I can just take the response as-is

        // Find any .schema file in the backup directory
        var schemaFiles = Directory.GetFiles(BackupDirectory, "*.schema");
        if (schemaFiles.Length == 0)
        {
            throw new FileNotFoundException($"No schema file found in {BackupDirectory}. Please ensure a .schema file exists.");
        }
        if (schemaFiles.Length > 1)
        {
            throw new InvalidOperationException($"Multiple schema files found in {BackupDirectory}. Please ensure only one .schema file exists.");
        }

        string json = File.ReadAllText(schemaFiles[0]);

        // Do some cleaning of this file to change index name, etc
        json = "{" + json.Substring(json.IndexOf("\"name\""));
        int indexOfIndexName = json.IndexOf("\"", json.IndexOf("name\"") + 5) + 1;
        int indexOfEndOfIndexName = json.IndexOf("\"", indexOfIndexName);
        json = json.Substring(0, indexOfIndexName) + TargetIndexName + json.Substring(indexOfEndOfIndexName);

        Uri ServiceUri = new Uri("https://" + TargetSearchServiceName + ".search.windows.net");
        HttpClient HttpClient = new HttpClient();
        HttpClient.DefaultRequestHeaders.Add("api-key", TargetAdminKey);

        try
        {
            Uri uri = new Uri(ServiceUri, "/indexes");
            HttpResponseMessage response = AzureSearchHelper.SendSearchRequest(HttpClient, HttpMethod.Post, uri, json);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            Console.WriteLine("  Error: {0}", ex.Message);
            throw;
        }
    }

    static int GetCurrentDocCount(SearchClient searchClient)
    {
        // Get the current doc count of the specified index
        try
        {
            SearchOptions options = new SearchOptions
            {
                SearchMode = SearchMode.All,
                IncludeTotalCount = true
            };

            SearchResults<Dictionary<string, object>> response = searchClient.Search<Dictionary<string, object>>("*", options);
            return Convert.ToInt32(response.TotalCount);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  Error: {0}", ex.Message);
        }

        return -1;
    }

    static void ImportFromJSON()
    {
        Console.WriteLine("\n  Upload index documents from saved JSON files");
        // Take JSON file and import this as-is to target index
        Uri ServiceUri = new Uri("https://" + TargetSearchServiceName + ".search.windows.net");
        HttpClient HttpClient = new HttpClient();
        HttpClient.DefaultRequestHeaders.Add("api-key", TargetAdminKey);

        try
        {
            foreach (string fileName in Directory.GetFiles(BackupDirectory, SourceIndexName + "*.json"))
            {
                Console.WriteLine("  -Uploading documents from file {0}", fileName);
                string json = File.ReadAllText(fileName);
                
                // Validate JSON structure
                try
                {
                    var jsonDoc = JsonDocument.Parse(json);
                    if (!jsonDoc.RootElement.TryGetProperty("value", out _))
                    {
                        Console.WriteLine($"  Error: JSON file {fileName} does not contain a 'value' array");
                        continue;
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"  Error: Invalid JSON in file {fileName}: {ex.Message}");
                    continue;
                }

                Uri uri = new Uri(ServiceUri, "/indexes/" + TargetIndexName + "/docs/index");
                HttpResponseMessage response = AzureSearchHelper.SendSearchRequest(HttpClient, HttpMethod.Post, uri, json);
                
                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = response.Content.ReadAsStringAsync().Result;
                    Console.WriteLine($"  Error: Response status code does not indicate success: {(int)response.StatusCode} ({response.StatusCode})");
                    Console.WriteLine($"  Error details: {errorContent}");
                    throw new Exception($"Failed to upload documents from {fileName}. Status: {response.StatusCode}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  Error: {0}", ex.Message);
            throw;
        }
    }
}
