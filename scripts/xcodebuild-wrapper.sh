#!/bin/bash
# xcodebuild wrapper for Xcode 26.3 workaround.
# Bug: xcodebuild 26.3 exits EX_USAGE (64) when called as:
#   xcodebuild -sdk /absolute/path/MacOSX.sdk -find <tool>
# xcrun calls this internally via: sh -c '.../xcodebuild -sdk /abs/path -find <tool>'
# Since xcrun calls xcodebuild using its absolute path, we wrap xcodebuild itself.
# When the -find tool pattern is detected with any SDK, look directly in the
# XcodeDefault toolchain directory.
ARGS=()
PREV=""
FIND_TOOL=""
HAS_FIND=false
for arg in "$@"; do
  ARGS+=("$arg")
  if [[ "$PREV" == "-find" ]]; then
    FIND_TOOL="$arg"
    HAS_FIND=true
  fi
  PREV="$arg"
done
if [[ "$HAS_FIND" == "true" && -n "$FIND_TOOL" ]]; then
  # Determine DEVELOPER_DIR from this script's location: .../Developer/usr/bin/xcodebuild
  SELF_DIR="$(cd "$(dirname "$0")" && pwd)"
  DEV="$(dirname "$(dirname "$SELF_DIR")")"
  TOOLBIN="$DEV/Toolchains/XcodeDefault.xctoolchain/usr/bin"
  if [[ -f "$TOOLBIN/$FIND_TOOL" ]]; then
    echo "$TOOLBIN/$FIND_TOOL"
    exit 0
  fi
  # Fallback: check usr/bin
  DEVBIN="$DEV/usr/bin"
  if [[ -f "$DEVBIN/$FIND_TOOL" ]]; then
    echo "$DEVBIN/$FIND_TOOL"
    exit 0
  fi
fi
# Pass through to the real xcodebuild
exec "$0.real" "${ARGS[@]}"
