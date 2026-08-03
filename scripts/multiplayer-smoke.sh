#!/usr/bin/env bash
set -euo pipefail

game="${1:?Usage: multiplayer-smoke.sh GAME_EXECUTABLE [editor] [cooperative|versus] [reconnect]}"
mode="${2:-exported}"
match_mode="${3:-cooperative}"
scenario="${4:-standard}"
host_log="$(mktemp)"
client_log="$(mktemp)"
resume_log="$(mktemp)"
host_home="$(mktemp -d)"
client_home="$(mktemp -d)"
host_pid=""
client_token="11111111111111111111111111111111"
host_token="22222222222222222222222222222222"

case "$match_mode" in
  cooperative) expected_mode="Cooperative" ;;
  versus) expected_mode="Versus" ;;
  *) echo "Match mode must be cooperative or versus" >&2; exit 2 ;;
esac

case "$scenario" in
  standard|reconnect) ;;
  *) echo "Scenario must be standard or reconnect" >&2; exit 2 ;;
esac

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

run_game() {
  if [[ "$mode" == "editor" ]]; then
    timeout "${process_timeout}s" "$game" "${args[@]}"
  else
    # Exported Godot .NET builds can stall before managed startup when a
    # background process has no terminal. A disposable PTY keeps both peers on
    # the exact same path as an interactive desktop launch.
    escaped="$(printf '%q ' timeout "${process_timeout}s" "$game" "${args[@]}")"
    script -qefc "$escaped" /dev/null
  fi
}

wait_for_log() {
  local pattern="$1"
  local log="$2"
  for _ in $(seq 1 "$readiness_attempts"); do
    grep -q "$pattern" "$log" && return 0
    kill -0 "$host_pid" 2>/dev/null || break
    sleep 0.1
  done
  return 1
}

# Godot's exported .NET host can stall before C# initialization when stdout is a
# regular file. Keep stdout as a pipe, as it is in the regular release smoke,
# while tee captures the log without flooding CI output.
AFC_MULTIPLAYER_SMOKE_ROLE=host AFC_MULTIPLAYER_SMOKE_MODE="$match_mode" \
  AFC_MULTIPLAYER_SMOKE_RECONNECT="$([[ "$scenario" == "reconnect" ]] && echo 1 || echo 0)" \
  AFC_RECONNECT_TOKEN="$host_token" HOME="$host_home" \
  run_game \
  </dev/null >"$host_log" 2>&1 &
host_pid=$!

if ! wait_for_log "AFC_MP_HOST_READY" "$host_log"; then
  echo "Multiplayer host did not become ready after $((readiness_attempts / 10)) seconds." >&2
  sed -n '1,240p' "$host_log"
  exit 1
fi

set +e
AFC_MULTIPLAYER_SMOKE_ROLE=client AFC_MULTIPLAYER_SMOKE_MODE="$match_mode" \
  AFC_MULTIPLAYER_SMOKE_RECONNECT_PHASE="$([[ "$scenario" == "reconnect" ]] && echo disconnect || true)" \
  AFC_RECONNECT_TOKEN="$client_token" HOME="$client_home" \
  run_game \
  </dev/null >"$client_log" 2>&1
client_status=$?

resume_status=0
if [[ "$scenario" == "reconnect" && "$client_status" -eq 0 ]]; then
  set -e
  if ! wait_for_log "AFC_MP_HOST_DROPPED" "$host_log"; then
    echo "Host did not reserve the disconnected captain's seat." >&2
    sed -n '1,240p' "$host_log"
    sed -n '1,240p' "$client_log"
    exit 1
  fi
  set +e
  AFC_MULTIPLAYER_SMOKE_ROLE=client AFC_MULTIPLAYER_SMOKE_MODE="$match_mode" \
    AFC_MULTIPLAYER_SMOKE_RECONNECT_PHASE=resume AFC_RECONNECT_TOKEN="$client_token" HOME="$client_home" \
    run_game \
    </dev/null >"$resume_log" 2>&1
  resume_status=$?
fi

wait "$host_pid"
host_status=$?
set -e

sed -n '1,240p' "$host_log"
sed -n '1,240p' "$client_log"
if [[ "$scenario" == "reconnect" ]]; then sed -n '1,240p' "$resume_log"; fi
test "$host_status" -eq 0
test "$client_status" -eq 0
test "$resume_status" -eq 0
grep -q "AFC_MP_HOST_PASS mode=$expected_mode" "$host_log"
if [[ "$scenario" == "reconnect" ]]; then
  grep -q "AFC_MP_CLIENT_DISCONNECTED" "$client_log"
  grep -q "AFC_MP_HOST_REJOINED" "$host_log"
  grep -q "AFC_MP_CLIENT_PASS mode=$expected_mode" "$resume_log"
else
  grep -q "AFC_MP_CLIENT_PASS mode=$expected_mode" "$client_log"
fi
if grep -qE "ERROR:|Unhandled exception|InvalidOperationException" "$host_log" "$client_log" "$resume_log"; then
  echo "Multiplayer smoke emitted an engine or managed error." >&2
  exit 1
fi
if grep -qE "above the MTU|higher packet loss" "$host_log" "$client_log" "$resume_log"; then
  echo "Multiplayer smoke emitted a packet-size warning." >&2
  exit 1
fi
