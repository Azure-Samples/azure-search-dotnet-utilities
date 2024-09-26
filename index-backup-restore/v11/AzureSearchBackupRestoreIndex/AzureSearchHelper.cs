//Copyright 2019 Microsoft

//Licensed under the Apache License, Version 2.0 (the "License");
//you may not use this file except in compliance with the License.
//You may obtain a copy of the License at

//       http://www.apache.org/licenses/LICENSE-2.0

//Unless required by applicable law or agreed to in writing, software
//distributed under the License is distributed on an "AS IS" BASIS,
//WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//See the License for the specific language governing permissions and
//limitations under the License.

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Polly;
using Polly.Retry;

namespace AzureSearchBackupRestoreIndex;

public class AzureSearchHelper
{
    // Azure AI Search requires that we use at least the API version "2024-03-01-Preview" to access all index features.
    // Hence, this API version string is used for requests to Azure AI Search.
    private const string ApiVersionString = "api-version=2024-03-01-Preview";

    private static readonly JsonSerializerOptions JsonOptions;
    
    // Retry policy to improve document migration resilience
    // AI Search may fail to process large batches
    private static readonly AsyncRetryPolicy<HttpResponseMessage> RetryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode) 
            .Or<Exception>()
            .WaitAndRetryAsync(4, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

    static AzureSearchHelper()
    {
        JsonOptions = new JsonSerializerOptions { };

        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static HttpResponseMessage SendSearchRequest(HttpClient client, HttpMethod method, Uri uri, string json = null)
    {
        UriBuilder builder = new UriBuilder(uri);
        string separator = string.IsNullOrWhiteSpace(builder.Query) ? string.Empty : "&";
        builder.Query = builder.Query.TrimStart('?') + separator + ApiVersionString;
        
        HttpResponseMessage response = RetryPolicy.ExecuteAsync(async () =>
        {
            var request = new HttpRequestMessage(method, builder.Uri);

            if (json != null)
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return await client.SendAsync(request);
        }).GetAwaiter().GetResult();

        return response;
    }

    public static void EnsureSuccessfulSearchResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            string error = response.Content?.ReadAsStringAsync().Result;
            throw new Exception("Search request failed: " + error);
        }
    }
}