using Azure.Search.Documents;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using System.Collections.Concurrent;

namespace export_data
{
    /// <summary>
    /// Export documents partitioned by a sortable and filterable field in the index
    /// </summary>
    public class PartitionExporter : Exporter
    {
        private readonly PartitionFile _partitionFile;
        private readonly SearchClient _searchClient;
        private readonly IPartitionWriter _partitionWriter;
        private readonly int _concurrentPartitions;
        private readonly int _pageSize;
        private readonly int[] _partitionIdsToInclude;
        private readonly ISet<int> _partitionIdsToExclude;

        public PartitionExporter(PartitionFile partitionFile, IPartitionWriter partitionWriter, SearchClient searchClient, SearchIndex index, int concurrentPartitions, int pageSize, int[] partitionIdsToInclude = null, ISet<int> partitionIdsToExclude = null, string[] fieldsToInclude = null, ISet<string> fieldsToExclude = null) : base(index, fieldsToInclude, fieldsToExclude)
        {
            _partitionFile = partitionFile;
            _partitionWriter = partitionWriter;
            _searchClient = searchClient;
            _concurrentPartitions = concurrentPartitions;
            _pageSize = pageSize;
            _partitionIdsToInclude = partitionIdsToInclude;
            _partitionIdsToExclude = partitionIdsToExclude;
        }

        public override async Task ExportAsync()
        {
            var cancellationTokenSource = new CancellationTokenSource();
            var partitions = new ConcurrentQueue<PartitionToExport>();
            if (_partitionIdsToInclude != null && _partitionIdsToInclude.Length > 0)
            {
                foreach (int id in _partitionIdsToInclude)
                {
                    partitions.Enqueue(new PartitionToExport { Id = id, Partition = _partitionFile.Partitions[id] });
                }
            }
            else
            {
                for (int id = 0; id < _partitionFile.Partitions.Count; id++)
                {
                    if (_partitionIdsToExclude == null ||
                        (_partitionIdsToExclude != null && !_partitionIdsToExclude.Contains(id)))
                    {
                        partitions.Enqueue(new PartitionToExport { Id = id, Partition = _partitionFile.Partitions[id] });
                    }
                }
            }

            var exporters = new Task[_concurrentPartitions];
            for (int i = 0; i < exporters.Length; i++)
            {
                exporters[i] = Task.Run(async () =>
                {
                    while (!cancellationTokenSource.IsCancellationRequested &&
                            partitions.TryDequeue(out PartitionToExport nextPartition))
                    {
                        Console.WriteLine($"Starting partition {nextPartition.Id}");
                        try
                        {
                            await ExportPartitionAsync(nextPartition.Id, nextPartition.Partition, cancellationTokenSource.Token);
                            Console.WriteLine($"Ended partition {nextPartition.Id}");
                        }
                        catch (Exception e)
                        {
                            Console.Error.Write(e.ToString());
                            cancellationTokenSource.Cancel();
                        }
                    }
                });
            }

            await Task.WhenAll(exporters);
        }

        private async Task ExportPartitionAsync(int partitionId, Partition partition, CancellationToken cancellationToken)
        {
            // Partitions being exported should have already been sub-partitioned into sizes less than 100k
            // This check exists because DocumentCount on a partition can theoretically be larger than the int max size
            int searchMaxSize = partition.DocumentCount > int.MaxValue ? int.MaxValue : (int)partition.DocumentCount;
            var options = new SearchOptions
            {
                Filter = partition.Filter,
                Size = searchMaxSize,
                Skip = 0
            };
            AddSelect(options);
            options.OrderBy.Add($"{_partitionFile.FieldName} asc");
            SearchResults<SearchDocument> searchResults = await _searchClient.SearchAsync<SearchDocument>(searchText: string.Empty, options: options, cancellationToken: cancellationToken);

            await _partitionWriter.WritePartitionAsync(partitionId, searchResults, cancellationToken);
        }

        private record PartitionToExport
        {
            public int Id { get; init; }

            public Partition Partition { get; init; }
        }
    }
}
