#!/usr/bin/env sh
set -eu
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PROJECT_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
OUTPUT_DIR="${1:-$PROJECT_DIR/runtimes/linux-x64/native}"
mkdir -p "$OUTPUT_DIR"
set -x
cc -shared -fPIC -o "$OUTPUT_DIR/libptyhelper.so" "$SCRIPT_DIR/ptyhelper.c" -lutil
