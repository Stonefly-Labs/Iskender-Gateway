# ISK-0008 developer topology: the readiness comparison of the smoke script (local-environment contract, Smoke
# assertions 3; research R13), as functions with no side effects, sourced by smoke.sh and by its tests. They write only
# check names and reasons, which are code-defined words, never values. Written for bash 3.2.

# isk_ready_expected [worker]: prints the readiness body each plane answers before ISK-0021, with status 503, or with
# worker the worker's (contract version 2, ISK-0009), in which only coordination differs, since its PostgreSQL lease
# coordinator cannot connect before ISK-0021.
isk_ready_expected() {
  if [ "${1:-}" = worker ]; then
    printf '%s' '{"version":1,"status":"not_ready","checks":[{"name":"lifecycle","status":"ready","reason":"started"},{"name":"persistence","status":"not_ready","reason":"authentication_failed"},{"name":"coordination","status":"not_ready","reason":"authentication_failed"},{"name":"single_instance","status":"ready","reason":"not_applicable"},{"name":"capability","status":"ready","reason":"none_refused"}],"refused":[]}'
    return 0
  fi
  printf '%s' '{"version":1,"status":"not_ready","checks":[{"name":"lifecycle","status":"ready","reason":"started"},{"name":"persistence","status":"not_ready","reason":"authentication_failed"},{"name":"coordination","status":"ready","reason":"ok"},{"name":"single_instance","status":"ready","reason":"not_applicable"},{"name":"capability","status":"ready","reason":"none_refused"}],"refused":[]}'
}

# isk_ready_reason <check> <body>: prints the check's reason, or nothing when the body has none.
isk_ready_reason() {
  printf '%s\n' "$2" | sed -n "s/.*{\"name\":\"$1\",\"status\":\"[a-z_]*\",\"reason\":\"\([a-z_]*\)\"}.*/\1/p" | head -n 1
}

# isk_ready_compare <status> <body> [worker]: returns 0 when the status is 503 and the body is exactly the expected
# one, the worker's with worker. Otherwise writes a line for each check whose reason differs, for the status, and for a
# non-empty refused list, and returns 1.
isk_ready_compare() {
  local status="$1" body="$2" expected check want found refused named=false
  expected="$(isk_ready_expected "${3:-}")"
  if [ "$status" = 503 ] && [ "$body" = "$expected" ]; then
    return 0
  fi
  for check in lifecycle persistence coordination single_instance capability; do
    want="$(isk_ready_reason "$check" "$expected")"
    found="$(isk_ready_reason "$check" "$body")"
    if [ "$want" != "$found" ]; then
      printf '%s: expected %s, found %s\n' "$check" "$want" "${found:-none}"
      named=true
    fi
  done
  if [ "$status" != 503 ]; then
    printf 'status: expected 503, found %s\n' "$(printf '%s\n' "$status" | sed 's/[^0-9]//g')"
    named=true
  fi
  refused="$(printf '%s\n' "$body" | sed -n 's/.*"refused":\[\([^]]*\)\].*/\1/p' | sed 's/[^a-z_,."]//g')"
  if [ -n "$refused" ]; then
    printf 'refused: expected none, found %s\n' "$refused"
    named=true
  fi
  if [ "$named" != true ]; then
    printf 'body: differs from the expected answer\n'
  fi
  return 1
}

# isk_ready_starting <body>: returns 0 when the lifecycle check's reason is starting.
isk_ready_starting() {
  [ "$(isk_ready_reason lifecycle "$1")" = starting ]
}
