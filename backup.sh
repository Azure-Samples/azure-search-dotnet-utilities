#!/bin/sh

# Configuration for backup-only operation
#export SourceSearchServiceName=""
#export SourceIndexName=""

# Target configuration should be empty for backup-only mode
export BackupDirectory="index-backup"
export TargetSearchServiceName=""
export TargetIndexName=""
export TargetAdminKey=""

# Validate source configuration
if [ -z "$SourceSearchServiceName" ] || [ -z "$SourceIndexName" ]; then
  echo "Error: SourceSearchServiceName and SourceIndexName must be set for backup operation."
  exit 1
fi

# Check if SourceAdminKey is set and not empty
if [ -z "$SourceAdminKey" ]; then
  echo "Error: SourceAdminKey must be set and not empty."
  exit 1
fi

# Build the container with all environment variables and no cache
podman build --no-cache -t azure-search-utilities \
  --build-arg SourceSearchServiceName="$SourceSearchServiceName" \
  --build-arg SourceAdminKey="$SourceAdminKey" \
  --build-arg SourceIndexName="$SourceIndexName" \
  --build-arg TargetSearchServiceName="" \
  --build-arg TargetAdminKey="" \
  --build-arg TargetIndexName="" \
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
