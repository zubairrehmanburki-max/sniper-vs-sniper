#!/bin/bash
# Run a command with a time limit (macOS has no `timeout`). If it overruns, print a stack sample
# of every xcodebuild process so a hang can be diagnosed from the CI log, then fail.
# Usage: watchdog.sh <seconds> <command...>
limit=$1; shift
"$@" &
pid=$!
for ((i = 0; i < limit; i += 5)); do
  sleep 5
  kill -0 "$pid" 2>/dev/null || { wait "$pid"; exit $?; }
done
echo "::error::Timed out after ${limit}s: $*"
for p in $(pgrep -x xcodebuild); do
  echo "::group::sample xcodebuild $p"
  sample "$p" 2 2>&1 | grep -vE '^\s+0x|dylib|\.framework|^$' | head -80
  echo "::endgroup::"
done
echo "::group::processes"; ps -axo pid,etime,command | grep -iE "SecurityAgent|git|xcodebuild|swift" | grep -v grep; echo "::endgroup::"
pkill -9 -x xcodebuild 2>/dev/null
kill -9 "$pid" 2>/dev/null
exit 1
