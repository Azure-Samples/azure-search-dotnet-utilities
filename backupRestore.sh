#!/bin/sh

#export SourceSearchServiceName="XXXXXXXXX"
#export TargetSearchServiceName="YYYYYYYYY"
#export SourceAdminKey="your-source-admin-key"  # Replace with actual key
#export TargetAdminKey="your-target-admin-key"  # Replace with actual key
#export SourceIndexName="AAAAAA"
#export TargetIndexName="AAAAAA-bk"
#export BackupDirectory="index-backup"
# Check source configuration
if [ -z "$SourceSearchServiceName" ]; then
    echo "Error: SourceSearchServiceName is not set or empty"
    exit 1
fi

if [ -z "$SourceAdminKey" ] || [ "$SourceAdminKey" = "your-source-admin-key" ]; then
    echo "Error: SourceAdminKey is not set or still has default value"
    exit 1
fi

if [ -z "$SourceIndexName" ]; then
    echo "Error: SourceIndexName is not set or empty"
    exit 1
fi

# Check target configuration
if [ -z "$TargetSearchServiceName" ]; then
    echo "Error: TargetSearchServiceName is not set or empty"
    exit 1
fi

if [ -z "$TargetAdminKey" ] || [ "$TargetAdminKey" = "your-target-admin-key" ]; then
    echo "Error: TargetAdminKey is not set or still has default value"
    exit 1
fi

if [ -z "$TargetIndexName" ]; then
    echo "Error: TargetIndexName is not set or empty"
    exit 1
fi

# Check backup directory
if [ -z "$BackupDirectory" ]; then
    echo "Error: BackupDirectory is not set or empty"
    exit 1
fi

# Create backup directory if it doesn't exist
if [ ! -d "$BackupDirectory" ]; then
    echo "Creating backup directory: $BackupDirectory"
    mkdir -p "$BackupDirectory"
fi

echo "All environment variables are properly set"
echo "Source: $SourceSearchServiceName/$SourceIndexName"
echo "Target: $TargetSearchServiceName/$TargetIndexName"
echo "Backup: $BackupDirectory"

podman build -t azure-search-utilities \
  --build-arg SourceSearchServiceName="$SourceSearchServiceName" \
  --build-arg SourceAdminKey="$SourceAdminKey" \
  --build-arg SourceIndexName="$SourceIndexName" \
  --build-arg TargetSearchServiceName="$TargetSearchServiceName" \
  --build-arg TargetAdminKey="$TargetAdminKey" \
  --build-arg TargetIndexName="$TargetIndexName" \
  --build-arg BackupDirectory="$BackupDirectmkdir -p index-backupory" .

# Check if SourceAdminKey and TargetAdminKey are set and not empty
if [ -z "$SourceAdminKey" ] || [ -z "$TargetAdminKey" ]; then
  echo "Error: Both SourceAdminKey and TargetAdminKey must be set and not empty."
  exit 1
fi
mkdir -p index-backup

podman run --rm \
  -e SourceSearchServiceName \
  -e SourceAdminKey \
  -e TargetSearchServiceName \
  -e TargetAdminKey \
  -v "$(pwd)/index-backup:/app/index-backup" \
  localhost/azure-search-utilities