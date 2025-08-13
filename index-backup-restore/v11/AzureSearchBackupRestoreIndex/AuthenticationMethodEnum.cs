using System.Runtime.Serialization;

namespace AzureSearchBackupRestoreIndex;

public enum AuthenticationMethodEnum
{
    [EnumMember(Value = "Managed Identity")]
    ManagedIdentity = 0,
    [EnumMember(Value = "API Key")]
    APIKey
}
