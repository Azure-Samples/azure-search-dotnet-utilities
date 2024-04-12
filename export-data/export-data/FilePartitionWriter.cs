using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace export_data
{
    public class FilePartitionWriter : IPartitionWriter
    {
        private readonly string _directory;
        private readonly string _indexName;

        public FilePartitionWriter(string directory, string indexName)
        {
            _directory = directory;
            _indexName = indexName;
        }

        public async Task WritePartitionAsync(int partitionId, SearchResults<SearchDocument> searchResults, CancellationToken cancellationToken, int? pageSizeHint = null)
        {
            if (!Directory.Exists(_directory))
            {
                Directory.CreateDirectory(_directory);
            }

            string exportPath = Path.Combine(_directory, $"{_indexName}-{partitionId}-documents.json");
            using FileStream exportOutput = File.Open(exportPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);

            await foreach (Page<SearchResult<SearchDocument>> resultPage in searchResults.GetResultsAsync().AsPages(pageSizeHint: pageSizeHint))
            {
                foreach (SearchResult<SearchDocument> searchResult in resultPage.Values)
                {
                    JsonSerializer.Serialize(exportOutput, searchResult.Document);
                    exportOutput.WriteByte((byte)'\n');
                }
            }
        }
    }
}
