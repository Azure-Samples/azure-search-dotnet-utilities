using Azure.Search.Documents.Indexes;

namespace Sample
{
    public class Document
    {
        [SimpleField(IsKey = true, IsFilterable = true)]
        public string Id { get; set; }

        [SimpleField(IsFilterable = true, IsSortable = true)]
        public DateTimeOffset Timestamp { get; set; }
    }
}
