#!/bin/bash
# Developer helper: build and run DashX360 headless under Xvfb with automation.
#   tools/devrun.sh <seconds> [screenshot times ms] [actions]
set -e
cd "$(dirname "$0")/.."
export PATH=$HOME/.dotnet:$PATH DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
OUT=${SHOTS:-/tmp/dashx360-shots}
mkdir -p "$OUT" "${USERDATA:-/tmp/dashx360-userdata}"
rm -f "$OUT"/*.png
dotnet build -nologo -v:q 2>&1 | grep -E ' error |rror\(s\)' | sort -u
export DASHX360_DEBUG=1 DASHX360_USER_DATA_FOLDER=${USERDATA:-/tmp/dashx360-userdata} DASHX360_SCREENSHOT=shot DASHX360_SCREENSHOT_DIR=$OUT
export DASHX360_SCREENSHOT_TIMES="${2:-4000}" DASHX360_TEST_ACTIONS="${3:-}"
cd bin/Debug/net10.0/linux-x64
timeout "${1:-20}" xvfb-run -a -s "-screen 0 1280x720x24" dotnet DashX360.dll 2>&1 | grep -av 'libEGL\|DRI3' | cut -c1-400 | head -${LINES_MAX:-60} || true
ls "$OUT"
