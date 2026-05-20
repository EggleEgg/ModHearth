#!/usr/bin/env bash
set -euo pipefail

rid="${1:?missing rid}"
ext="${2:?missing ext}"

publish_dir="bin/Release/net8.0/${rid}/publish"
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

native_dir="${publish_dir}/native"
if [ -d "$native_dir" ]; then
  case "$rid" in
    linux-*) find "$native_dir" -maxdepth 1 -type f -name "*.dylib" -delete ;;
    osx-*) find "$native_dir" -maxdepth 1 -type f -name "*.so" -delete ;;
  esac
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
