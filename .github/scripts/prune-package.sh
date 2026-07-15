#!/usr/bin/env bash

# Used for removing unused runtimes. Mostly useless now but it doesnt hurt

set -euo pipefail

rid="${1:?missing rid}"
ext="${2:?missing ext}"

publish_dir="artifacts/${rid}"
runtimes_dir="${publish_dir}/runtimes"

if [ -d "$runtimes_dir" ]; then
  case "$rid" in
    linux-*)
      remove_patterns=(win* osx* maccatalyst* android* ios* tvos* browser*)
      ;;
    osx-*)
      remove_patterns=(win* linux* android* ios* tvos* browser*)
      ;;
    *)
      remove_patterns=()
      ;;
  esac

  for pattern in "${remove_patterns[@]}"; do
    rm -rf "$runtimes_dir"/$pattern
  done
fi

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
