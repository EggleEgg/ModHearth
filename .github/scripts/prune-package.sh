#!/usr/bin/env bash

set -euo pipefail

rid="${1:?missing rid}"
ext="${2:?missing ext}"

publish_dir="artifacts/${rid}"

if [ ! -d "$publish_dir" ]; then
  echo "Publish directory not found: $publish_dir"
  exit 1
fi

if [ -z "$(ls -A "$publish_dir")" ]; then
  echo "Publish directory is empty: $publish_dir"
  exit 1
fi

ls -la "$publish_dir"

mkdir -p dist
tar -czf "dist/ModHearth-${rid}.${ext}" -C "$publish_dir" .
