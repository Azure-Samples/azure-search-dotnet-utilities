using System.Runtime.Serialization;

namespace AzureSearchBackupRestoreIndex;

public enum IndexCopyModeEnum
{

    [EnumMember(Value = "Single")]
    Single = 0,
    [EnumMember(Value = "All")]
    All
}
