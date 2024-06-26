﻿using Microsoft.Extensions.Configuration;

namespace Sample
{
    public class Configuration
    {
        /// <summary>
        /// Service endpoint for the search service
        /// e.g. "https://your-search-service.search.windows.net
        /// </summary>
        [ConfigurationKeyName("AZURE_SEARCH_SERVICE_ENDPOINT")]
        public string ServiceEndpoint { get; set; }

        /// <summary>
        /// Index name in the search service
        /// e.g. sample-index
        /// </summary>
        [ConfigurationKeyName("AZURE_SEARCH_INDEX_NAME")]
        public string IndexName { get; set; }

        /// <summary>
        /// Admin API key for search service
        /// Optional, if not specified attempt to use DefaultAzureCredential
        /// </summary>
        [ConfigurationKeyName("AZURE_SEARCH_ADMIN_KEY")]
        public string AdminKey { get; set; }

        /// <summary>
        /// Directory to save the exported files in
        /// </summary>
        [ConfigurationKeyName("EXPORT_DIRECTORY")]
        public string ExportDirectory { get; set; }

        /// <summary>
        /// Validate the configuration
        /// </summary>
        /// <exception cref="ArgumentException">If any parameters are invalid</exception>
        public void Validate()
        {
            if (!Uri.TryCreate(ServiceEndpoint, UriKind.Absolute, out _))
            {
                throw new ArgumentException("Must specify service endpoint", nameof(ServiceEndpoint));
            }

            if (string.IsNullOrEmpty(IndexName))
            {
                throw new ArgumentException("Must specify index name", nameof(IndexName));
            }
        }
    }
}
