#!/bin/bash
# xcrun wrapper for Xcode 26.3 workaround.
# Bug: xcodebuild 26.3 exits EX_USAGE (64) when called as:
#   xcodebuild -sdk /absolute/path/MacOSX.sdk -find <tool>
# xcrun uses this internally when resolving tools via MacOSX SDK.
# Fix: when MacOSX SDK is involved and a tool is being found, look directly
# in XcodeDefault.xctoolchain to bypass the broken xcodebuild -find path.
ARGS=()
PREV=""
HAS_MACOS_SDK=false
FIND_TOOL=""
for arg in "$@"; do
  if [[ "$PREV" == "-sdk" && "$arg" == */MacOSX*.sdk* ]]; then
    ARGS+=("macosx")
    HAS_MACOS_SDK=true
  elif [[ "$PREV" == "-sdk" && ("$arg" == "macosx" || "$arg" == "macosx"[0-9]*) ]]; then
    ARGS+=("$arg")
    HAS_MACOS_SDK=true
  else
    ARGS+=("$arg")
  fi
  if [[ "$PREV" == "-find" || "$PREV" == "--find" ]]; then
    FIND_TOOL="$arg"
  fi
  PREV="$arg"
done
if [[ "$HAS_MACOS_SDK" == "true" && -n "$FIND_TOOL" ]]; then
  # Determine DEVELOPER_DIR: parent of the dir containing this script (usr/bin → ..)
  SELF_DIR="$(cd "$(dirname "$0")" && pwd)"
  DEV="$(dirname "$SELF_DIR")"
  TOOLBIN="$DEV/Toolchains/XcodeDefault.xctoolchain/usr/bin"
  if [[ -f "$TOOLBIN/$FIND_TOOL" ]]; then
    echo "$TOOLBIN/$FIND_TOOL"
    exit 0
  fi
  DEVBIN="$DEV/usr/bin"
  if [[ -f "$DEVBIN/$FIND_TOOL" ]]; then
    echo "$DEVBIN/$FIND_TOOL"
    exit 0
  fi
fi
# Fall through: call the real xcrun (saved as xcrun.real during setup)
exec "$0.real" "${ARGS[@]}"
