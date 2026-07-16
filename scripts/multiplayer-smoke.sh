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
  args=(--headless --path . --)
else
  args=(--headless --)
fi

HOME="$host_home" timeout 25s "$game" "${args[@]}" --multiplayer-smoke-host >"$host_log" 2>&1 &
host_pid=$!

for _ in $(seq 1 100); do
  grep -q "AFC_MP_HOST_READY" "$host_log" && break
  kill -0 "$host_pid" 2>/dev/null || break
  sleep 0.1
done

if ! grep -q "AFC_MP_HOST_READY" "$host_log"; then
  sed -n '1,240p' "$host_log"
  exit 1
fi

HOME="$client_home" timeout 25s "$game" "${args[@]}" --multiplayer-smoke-client >"$client_log" 2>&1
wait "$host_pid"

sed -n '1,240p' "$host_log"
sed -n '1,240p' "$client_log"
grep -q "AFC_MP_HOST_PASS" "$host_log"
grep -q "AFC_MP_CLIENT_PASS" "$client_log"
! grep -qE "ERROR:|Unhandled exception|InvalidOperationException" "$host_log" "$client_log"
