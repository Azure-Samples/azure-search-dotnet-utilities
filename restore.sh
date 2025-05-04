#!/bin/sh

# Configuration for restore-only operation
export SourceSearchServiceName=""
export SourceAdminKey=""
export SourceIndexName=""
#export TargetSearchServiceName=""
#export TargetIndexName=""
export BackupDirectory="index-backup"

# Validate target configuration
if [ -z "$TargetSearchServiceName" ] || [ -z "$TargetIndexName" ]; then
  echo "Error: TargetSearchServiceName and TargetIndexName must be set for restore operation."
  exit 1
fi

# Validate backup directory exists
if [ ! -d "$BackupDirectory" ]; then
  echo "Error: Backup directory '$BackupDirectory' does not exist."
  exit 1
fi

# Validate backup files exist
BACKUP_FILES=$(ls "$BackupDirectory"/*.json 2>/dev/null | wc -l)
if [ "$BACKUP_FILES" -eq 0 ]; then
  echo "Error: No backup files found in '$BackupDirectory'."
  exit 1
fi

# Check if TargetAdminKey is set and not empty
if [ -z "$TargetAdminKey" ]; then
  echo "Error: TargetAdminKey must be set and not empty."
  exit 1
fi

# Build the container with all environment variables and no cache
podman build --no-cache -t azure-search-utilities \
  --build-arg SourceSearchServiceName="$SourceSearchServiceName" \
  --build-arg SourceAdminKey="$SourceAdminKey" \
  --build-arg SourceIndexName="$SourceIndexName" \
  --build-arg TargetSearchServiceName="$TargetSearchServiceName" \
  --build-arg TargetAdminKey="$TargetAdminKey" \
  --build-arg TargetIndexName="$TargetIndexName" \
  --build-arg BackupDirectory="$BackupDirectory" .

# Run the container with all environment variables
podman run --rm \
  -e SourceSearchServiceName \
  -e SourceAdminKey \
  -e SourceIndexName \
  -e TargetSearchServiceName \
  -e TargetAdminKey \
  -e TargetIndexName \
  -e BackupDirectory \
  -v "$(pwd)/index-backup:/app/index-backup" \
  localhost/azure-search-utilities
