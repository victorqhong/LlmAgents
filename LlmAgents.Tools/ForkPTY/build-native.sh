#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PROJECT_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
SOURCE_FILE="$SCRIPT_DIR/ptyhelper.c"

OS_NAME=$(uname -s)
ARCH_NAME=$(uname -m)

case "$OS_NAME-$ARCH_NAME" in
  Linux-x86_64)
    RID="linux-x64"
    EXT="so"
    CFLAGS="-shared -fPIC"
    LDFLAGS="-lutil"
    ;;
  Linux-aarch64)
    RID="linux-arm64"
    EXT="so"
    CFLAGS="-shared -fPIC"
    LDFLAGS="-lutil"
    ;;
  Darwin-arm64)
    RID="osx-arm64"
    EXT="dylib"
    CFLAGS="-dynamiclib"
    LDFLAGS=""
    ;;
  *)
    echo "Unsupported platform for PTY helper build: $OS_NAME-$ARCH_NAME" >&2
    exit 1
    ;;
esac

OUTPUT_DIR="${1:-$PROJECT_DIR/runtimes/$RID/native}"
mkdir -p "$OUTPUT_DIR"
OUTPUT_FILE="$OUTPUT_DIR/libptyhelper.$EXT"

# Check for a C compiler
if ! command -v cc >/dev/null 2>&1; then
  echo "WARNING: No C compiler ('cc') found on PATH." >&2
  echo "WARNING: Skipping PTY helper native library build for $RID." >&2
  echo "WARNING: Install gcc or clang to build the native library." >&2
  exit 0
fi

echo "Building PTY helper for $RID: cc $CFLAGS -o $OUTPUT_FILE $SOURCE_FILE $LDFLAGS"
cc $CFLAGS -o "$OUTPUT_FILE" "$SOURCE_FILE" $LDFLAGS
echo "Built: $OUTPUT_FILE"
