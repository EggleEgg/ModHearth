#!/usr/bin/env bash
set -euo pipefail

RID="linux-x64"
EXT="tar.gz"
VERSION="local-dev"
RUN_TESTS=true

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --skip-tests) RUN_TESTS=false; shift ;;
    *) echo "Unknown arg: $1"; exit 1 ;;
  esac
done

echo "== Restoring solution =="
dotnet restore ModHearth.sln

echo "== Restoring publish runtime assets (${RID}) =="
dotnet restore ModHearth.csproj -r "$RID"

echo "== Building solution =="
dotnet build ModHearth.sln -c Release --no-restore

if [ "$RUN_TESTS" = true ]; then
  echo "== Running tests under Xvfb =="
  if ! command -v xvfb-run >/dev/null 2>&1; then
    echo "xvfb-run not found. Install with: sudo apt-get install -y xvfb"
    exit 1
  fi
  xvfb-run -a dotnet test ModHearth.sln -c Release --no-build --no-restore
else
  echo "== Skipping tests (--skip-tests) =="
fi

echo "== Building publish runtime target (${RID}, version=${VERSION}) =="
dotnet build ModHearth.csproj -c Release -r "$RID" --no-restore \
  /p:MoveOutputDlls=false /p:InformationalVersion="$VERSION"

echo "== Publishing =="
dotnet publish ModHearth.csproj -c Release -r "$RID" --self-contained false \
  /p:UseAppHost=true /p:MoveOutputDlls=false --no-build --no-restore

echo "== Pruning + packaging =="
bash ./.github/scripts/prune-package.sh "$RID" "$EXT"

echo "== Done: dist/ModHearth-${RID}.${EXT} =="