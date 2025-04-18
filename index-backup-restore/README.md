---
page_type: sample
languages:
  - csharp
name: Back up and restore an Azure AI Search index
description: "This application backs up a 'source' index schema and its documents to JSON files on your computer, and then uses those files to recreate a 'target' index copy in the 'target' search service that you specify. Depending on your needs, you can use all or part of this application to backup your index files and/or move an index from one search service to another."
products:
  - azure
  - azure-cognitive-search
urlFragment: azure-search-backup-restore-index
---

# Back up and restore an Azure AI Search index

![MIT license badge](https://img.shields.io/badge/license-MIT-green.svg)

**This unofficial code sample is offered \"as-is\" and might not work for all customers and scenarios. If you run into difficulties, you should manually recreate and reload your search index on a new search service.**

This application copies an index from one service to another, creating JSON files on your computer with the index schema and documents. This tool is useful if you've been using the Free pricing tier to develop your index and want to move to the Basic or higher tier for production use. It's also useful if you want to back up your index to your computer and restore the index at a later time.

> **Note**: Azure AI Search now supports [service upgrades](https://learn.microsoft.com/azure/search/search-how-to-upgrade) and [pricing tier changes](https://learn.microsoft.com/azure/search/search-capacity-planning#change-your-pricing-tier). If you're backing up and restoring your index for migration to a higher capacity service, you now have other options.

## IMPORTANT - PLEASE READ

Search indexes are different from other datastores because they are constantly ranking and scoring results and data may shift. If you page through search results or even use continuation tokens as this tool does, it is possible to miss some data during data extraction.

As an example, assume that you are searching for documents and a document with ID 101 is part of page 5 of the search results. Then, as you are extracting data from page to page, and move from page 4 to page 5, it is possible that now ID 101 is actually part of page 4. This means that when you look at page 5, it is no longer there and you have missed that document. As a result, it is best if there are no changes being made to the search index when you use run this tool.

For this reason, this tool compares the number of index documents in the original index and the index copy. If the numbers don't match, the copy may be missing data. Although this safeguard does not provide a perfect solution, it does help you help prevent you from missing data.

**If your index has more than 100,000 documents**, this sample, as written, will not work. This is because the REST API $skip feature, that is used for paging, has a 100K document limit. However, you can work around this limitation by adding code to iterate over, and filter on, a facet with less that 100K documents per facet value.

## Prerequisites

- [Visual Studio](https://visualstudio.microsoft.com/downloads/)
- [Azure AI Search service](https://docs.microsoft.com/azure/search/search-create-service-portal)

## Setup

1. Clone or download this sample repository.
1. Extract contents if the download is a zip file. Make sure the files are read-write.

This sample is available in two versions:

1. **v11** (recommended): uses the newer [Azure.Search.Documents](https://docs.microsoft.com/dotnet/api/overview/azure/search.documents-readme) client library. This is the library recommended for use on all new projects.
2. **v10**: uses the deprecated [Microsoft.Azure.Search](https://learn.microsoft.com/dotnet/api/microsoft.azure.search) client libraries.

## Run the sample

> [!NOTE]
> In this application the term "source" identifies the search service and index and that you are backing up. The term "target" identifies the search service and index that will contain the restored (copied) index.

1. Open the AzureSearchBackupRestoreIndex.sln project in Visual Studio.

1. By default, this application will copy the source index to the target search service using the target index name you provide.
    - If you only want to back up the index and not restore it immediately, do this:
        - Comment out the code in the **Main** method after the **BackupIndexAndDocuments** method call.
        - Comment out the last two lines of the **ConfigurationSetup** method that set the _TargetSearchClient_ and _TargetIndexClient_.
    - If you want to restore a index that you previously backed up, do this:
        - Make sure that the the _BackupDirectory_ in the appsettings.json file is pointing to to the backup location.
        - Comment out the **BackupIndexAndDocuments** method call and the the line that checks the _targetCount_ in the **Main** method.
        - Comment out the lines in **ConfigurationSetup** method that set the _SourceSearchClient_ and _SourceIndexClient_.

1. Open the appsettings.json and replace the placeholder strings with all applicable values:

    - The source search service name (SourceSearchServiceName) and key (SourceAdminKey) and the name of the index that you want to restore/copy.
    - The target search service name (TargetSearchServiceName) and key (TargetAdminKey) and the name of the restored/copied index in the target service.
    - The location on your computer where you want to store the backup index schema and documents (BackupDirectory). The location must be have non-admin write permission. Include escape characters in directory paths. Examples:
      - Windows: `C:\\users\<your-account-name>\indexBackup\\` (Windows)
      - MacOS: `/Users/<your-account-name>/indexBackup` (MacOS)
      - Relative to `.csproj` file location: `index-backup`

1. If necessary, update the _APIVersionString_ value in `AzureSearchHelper.cs`.

1. Compile and Run the project.

## Next steps

You can learn more about Azure AI Search on the [official documentation site](https://docs.microsoft.com/azure/search).
