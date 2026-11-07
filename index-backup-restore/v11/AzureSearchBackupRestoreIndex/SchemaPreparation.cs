using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AzureSearchBackupRestoreIndex;

internal sealed class VectorizerCredentialBinding
{
    public string ApiKeyEnvironmentVariable { get; set; }
    public bool UseManagedIdentity { get; set; }
}

internal static class SchemaPreparation
{
    // New-target POST restore preparation, not existing-index PUT preservation.
    // This helper has no file, network, logging, or environment access of its own.
    internal static string Prepare(
        string schema,
        string targetIndexName,
        IReadOnlyDictionary<string, VectorizerCredentialBinding> bindings,
        Func<string, string> readEnvironmentVariable)
    {
        try
        {
            return PrepareCore(schema, targetIndexName, bindings, readEnvironmentVariable);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Schema preflight failed: invalid JSON.");
        }
    }

    private static string PrepareCore(
        string schema,
        string targetIndexName,
        IReadOnlyDictionary<string, VectorizerCredentialBinding> bindings,
        Func<string, string> readEnvironmentVariable)
    {
        if (string.IsNullOrWhiteSpace(targetIndexName))
            throw new InvalidOperationException("Schema preflight failed: target index name is required.");
        if (bindings == null || readEnvironmentVariable == null)
            throw new InvalidOperationException("Schema preflight failed: binding inputs are required.");

        JsonObject root = JsonNode.Parse(schema) as JsonObject
            ?? throw new InvalidOperationException("Schema preflight failed: expected an index object.");
        root["name"] = targetIndexName;
        root.Remove("@odata.context");
        root.Remove("@odata.etag");
        if (root["encryptionKey"] is JsonObject encryptionKey &&
            encryptionKey["accessCredentials"] is JsonObject accessCredentials)
            RejectUnsupportedRedaction(accessCredentials["applicationSecret"],
                "encryptionKey.accessCredentials.applicationSecret");

        JsonArray vectorizers = null;
        if (root["vectorSearch"] != null)
        {
            if (root["vectorSearch"] is not JsonObject search)
                throw new InvalidOperationException("Schema preflight failed: invalid vectorSearch object.");
            if (search["vectorizers"] != null)
                vectorizers = search["vectorizers"] as JsonArray
                    ?? throw new InvalidOperationException("Schema preflight failed: invalid vectorizers array.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        if (vectorizers != null)
        {
            for (int i = 0; i < vectorizers.Count; i++)
            {
                string path = "vectorSearch.vectorizers[" + i + "].azureOpenAIParameters.apiKey";
                if (vectorizers[i] is not JsonObject vectorizer ||
                    !TryString(vectorizer["name"], out string name) || string.IsNullOrWhiteSpace(name) ||
                    !seen.Add(name))
                    throw new InvalidOperationException("Schema preflight failed: invalid or duplicate vectorizer name.");

                bindings.TryGetValue(name, out VectorizerCredentialBinding binding);
                bool isAzureOpenAI = TryString(vectorizer["kind"], out string kind) && kind == "azureOpenAI";
                if (kind == "aml" && vectorizer["amlParameters"] is JsonObject amlParameters)
                    RejectUnsupportedRedaction(amlParameters["key"],
                        "vectorSearch.vectorizers[" + i + "].amlParameters.key");
                if (kind == "aiServicesVision" && vectorizer["aiServicesVisionParameters"] is JsonObject visionParameters)
                    RejectUnsupportedRedaction(visionParameters["apiKey"],
                        "vectorSearch.vectorizers[" + i + "].aiServicesVisionParameters.apiKey");
                if (kind == "customWebApi" && vectorizer["customWebApiParameters"] is JsonObject customParameters)
                {
                    if (TryString(customParameters["uri"], out string uri) &&
                        Uri.UnescapeDataString(uri).Contains("<redacted>", StringComparison.Ordinal))
                        throw new InvalidOperationException("Schema preflight failed at vectorSearch.vectorizers[" +
                            i + "].customWebApiParameters.uri: unsupported redacted URI credential.");
                    if (customParameters["httpHeaders"] is JsonObject headers)
                        foreach (var header in headers)
                            if (TryString(header.Value, out string headerValue) &&
                                (headerValue.Trim() == "<redacted>" ||
                                 Regex.IsMatch(headerValue.Trim(), @"\A(?i:Bearer)\s+<redacted>\z")))
                                throw new InvalidOperationException("Schema preflight failed at vectorSearch.vectorizers[" +
                                    i + "].customWebApiParameters.httpHeaders: unsupported redacted header.");
                }
                if (!isAzureOpenAI)
                {
                    if (binding != null)
                        throw new InvalidOperationException("Schema preflight failed: binding requires kind azureOpenAI.");
                    continue;
                }

                JsonObject parameters = vectorizer["azureOpenAIParameters"] as JsonObject;
                if (parameters == null)
                    throw new InvalidOperationException("Schema preflight failed: invalid Azure OpenAI parameters.");
                if (parameters["apiKey"] != null && !TryString(parameters["apiKey"], out _))
                    throw new InvalidOperationException("Schema preflight failed at " + path + ": expected a string.");

                bool redacted = TryString(parameters["apiKey"], out string key) && key == "<redacted>";
                if (binding == null)
                {
                    if (redacted)
                        throw new InvalidOperationException("Schema preflight failed at " + path + ": explicit target credential binding required.");
                    continue;
                }

                used.Add(name);
                bool hasReference = !string.IsNullOrWhiteSpace(binding.ApiKeyEnvironmentVariable);
                if (hasReference == binding.UseManagedIdentity)
                    throw new InvalidOperationException("Schema preflight failed: select exactly one credential binding mode.");

                if (binding.UseManagedIdentity)
                {
                    // The caller must separately validate target identity and resource RBAC.
                    // Preserve authIdentity (including user-assigned identity), and all other parameters.
                    parameters.Remove("apiKey");
                }
                else
                {
                    if (!Regex.IsMatch(binding.ApiKeyEnvironmentVariable, @"\A[A-Za-z_][A-Za-z0-9_]*\z"))
                        throw new InvalidOperationException("Schema preflight failed: invalid environment-variable reference.");
                    if (parameters["authIdentity"] != null)
                        throw new InvalidOperationException("Schema preflight failed: API-key binding conflicts with an explicit authIdentity.");

                    string value;
                    try
                    {
                        value = readEnvironmentVariable(binding.ApiKeyEnvironmentVariable);
                    }
                    catch (ArgumentException)
                    {
                        throw new InvalidOperationException("Schema preflight failed: credential reference could not be resolved.");
                    }
                    catch (System.Security.SecurityException)
                    {
                        throw new InvalidOperationException("Schema preflight failed: credential reference could not be resolved.");
                    }
                    if (string.IsNullOrWhiteSpace(value) || value.Trim() == "<redacted>")
                        throw new InvalidOperationException("Schema preflight failed at " + path + ": credential reference is missing or invalid.");
                    parameters["apiKey"] = value;
                }
            }
        }

        if (used.Count != bindings.Count)
            throw new InvalidOperationException("Schema preflight failed: a configured binding has no supported named vectorizer.");
        return root.ToJsonString();
    }

    private static bool TryString(JsonNode node, out string value)
    {
        value = null;
        return node is JsonValue json && json.TryGetValue(out value);
    }

    private static void RejectUnsupportedRedaction(JsonNode node, string path)
    {
        if (TryString(node, out string value) && value == "<redacted>")
            throw new InvalidOperationException("Schema preflight failed at " + path + ": unsupported redacted credential.");
    }
}
