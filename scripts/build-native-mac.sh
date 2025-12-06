#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
EXTERNAL_DIR="$ROOT_DIR/external"
OUTPUT_DIR="$ROOT_DIR/native/lib/mac"

mkdir -p "$OUTPUT_DIR"

# Build PortAudio
echo "Building PortAudio..."
cd "$EXTERNAL_DIR/portaudio"
./configure && make
cp lib/.libs/libportaudio.dylib "$OUTPUT_DIR/"
echo "PortAudio built successfully"

# Build RtMidi
echo "Building RtMidi..."
cd "$EXTERNAL_DIR/rtmidi"
if [ ! -d "build" ]; then
    mkdir build
fi
cd build
cmake .. -DCMAKE_OSX_ARCHITECTURES=arm64 -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON
cmake --build . --config Release
cp librtmidi.dylib "$OUTPUT_DIR/" 2>/dev/null || cp librtmidi.*.dylib "$OUTPUT_DIR/librtmidi.dylib"
echo "RtMidi built successfully"

echo "Native libraries built to $OUTPUT_DIR"
ls -la "$OUTPUT_DIR"
