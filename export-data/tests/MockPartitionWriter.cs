using Azure;
using Azure.Search.Documents.Models;
using export_data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tests
{
    public class MockPartitionWriter : IPartitionWriter
    {
        private readonly string _key;
        private readonly ConcurrentDictionary<int, Dictionary<string, SearchDocument>> _exportedPartitions = new();

        public MockPartitionWriter(string key)
        {
            _key = key;
        }

        public async Task WritePartitionAsync(int partitionId, SearchResults<SearchDocument> searchResults, CancellationToken cancellationToken, int? pageSizeHint = null)
        {
            var partition = new Dictionary<string, SearchDocument>();
            await foreach (Page<SearchResult<SearchDocument>> resultPage in searchResults.GetResultsAsync().AsPages(pageSizeHint: pageSizeHint))
            {
                foreach (SearchResult<SearchDocument> searchResult in resultPage.Values)
                {
                    partition[searchResult.Document[_key].ToString()] = searchResult.Document;
                }
            }

            _exportedPartitions[partitionId] = partition;
        }

        public IReadOnlyDictionary<int, IReadOnlyDictionary<string, SearchDocument>> GetExportedPartitions()
        {
            var results = new Dictionary<int, IReadOnlyDictionary<string, SearchDocument>>();
            foreach (KeyValuePair<int, Dictionary<string, SearchDocument>> partition in _exportedPartitions)
            {
                results[partition.Key] = partition.Value;
            }
            return results;
        }
    }
}
