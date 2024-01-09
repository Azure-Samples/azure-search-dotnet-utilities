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

        public async Task WritePartitionAsync(int partitionId, SearchResults<SearchDocument> searchResults, CancellationToken cancellationToken)
        {
            var partition = new Dictionary<string, SearchDocument>();
            await foreach (Page<SearchResult<SearchDocument>> resultPage in searchResults.GetResultsAsync().AsPages())
            {
                foreach (SearchResult<SearchDocument> searchResult in resultPage.Values)
                {
                    partition[_key] = searchResult.Document;
                }
            }

            _exportedPartitions[partitionId] = partition;
        }
    }
}
