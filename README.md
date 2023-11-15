# C# utility code samples for Azure AI Search

This repository contains C# code samples that help you perform specific tasks, such as checking storage or exporting content from an index. 

## In this repository

| Sample | Description |
|--------|-------------|
| check-storage-usage | Checks storage usage of an Azure AI Search service on a schedule. You can modify this sample to [adjust the service's capacity](https://docs.microsoft.com/azure/search/search-capacity-planning) or send an alert when the storage usage exceeds a predefined threshold. |
| data-lake-gen2-acl-indexing | Proof-of-concept console app that demonstrates how to index a subset of your Azure Data Lake Gen2 data by using access control lists to allow certain files and directories to be accessed by an indexer in Azure AI Search. The indexer connection to Azure Data Lake Gen2 uses a managed identity and role assignments for selective data access. The sample loads data and sets up permissions programmatically, and then runs the indexer to create and load a search index. |
| export-data | A console application that exports data from an Azure AI Search service. |
| index-backup-restore | A console app that backs up an index (schema and documents) to your local computer and then uses the stored backup to recreate the index in a target search service that you specify. This sample can be helpful if you want to move an index into a different pricing tier.|
| search-aggregations | Proof-of-concept console app that demonstrates how aggregations can be computed from the random data, and how the data can be filtered using a query. |

## More resources

+ See [.NET samples in Azure AI Search](https://learn.microsoft.com/azure/search/samples-dotnet) for a comprehensive list of all Azure AI Search code samples that run on .NET.

+ See [Azure AI Search documentation](https://learn.microsoft.com/azure/search) for product documentation.
