using Azure.Search.Documents.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace export_data
{
    public interface IPartitionWriter
    {
        public Task WritePartitionAsync(int partitionId, SearchResults<SearchDocument> searchResults, CancellationToken cancellationToken, int? pageSizeHint = null);
    }
}
