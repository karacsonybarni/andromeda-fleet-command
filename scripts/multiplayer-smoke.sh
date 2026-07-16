#!/usr/bin/env bash
set -euo pipefail

game="${1:?Usage: multiplayer-smoke.sh GAME_EXECUTABLE [editor]}"
mode="${2:-exported}"
host_log="$(mktemp)"
client_log="$(mktemp)"
host_home="$(mktemp -d)"
client_home="$(mktemp -d)"
host_pid=""

cleanup() {
  if [[ -n "$host_pid" ]] && kill -0 "$host_pid" 2>/dev/null; then
    kill "$host_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

if [[ "$mode" == "editor" ]]; then
  args=(--headless --path .)
  process_timeout=25
  readiness_attempts=100
else
  args=(--headless)
  process_timeout=150
  readiness_attempts=900
fi

# Godot's exported .NET host can stall before C# initialization when stdout is a
# regular file. Keep stdout as a pipe, as it is in the regular release smoke,
# while tee captures the log without flooding CI output.
AFC_MULTIPLAYER_SMOKE_ROLE=host HOME="$host_home" timeout "${process_timeout}s" "$game" "${args[@]}" \
  </dev/null > >(tee "$host_log" >/dev/null) 2>&1 &
host_pid=$!

for _ in $(seq 1 "$readiness_attempts"); do
  grep -q "AFC_MP_HOST_READY" "$host_log" && break
  kill -0 "$host_pid" 2>/dev/null || break
  sleep 0.1
done

if ! grep -q "AFC_MP_HOST_READY" "$host_log"; then
  echo "Multiplayer host did not become ready after $((readiness_attempts / 10)) seconds." >&2
  sed -n '1,240p' "$host_log"
  exit 1
fi

set +e
AFC_MULTIPLAYER_SMOKE_ROLE=client HOME="$client_home" timeout "${process_timeout}s" "$game" "${args[@]}" \
  </dev/null > >(tee "$client_log" >/dev/null) 2>&1
client_status=$?
wait "$host_pid"
host_status=$?
set -e

sed -n '1,240p' "$host_log"
sed -n '1,240p' "$client_log"
test "$host_status" -eq 0
test "$client_status" -eq 0
grep -q "AFC_MP_HOST_PASS" "$host_log"
grep -q "AFC_MP_CLIENT_PASS" "$client_log"
! grep -qE "ERROR:|Unhandled exception|InvalidOperationException" "$host_log" "$client_log"
