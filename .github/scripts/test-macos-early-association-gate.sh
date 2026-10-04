#!/usr/bin/env bash
set -euo pipefail
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
source "$script_dir/macos-early-association-gate.sh"
root=$1
test ! -e "$root"
mkdir -p "$root"
early_association_expected > "$root/expected.raw"
expected=$(< "$root/expected.raw")
total=0

check_fixture() {
  local name=$1 command_status=$2 expected_pass=$3 actual_pass=0 actual_exit=0
  local evidence="$root/$name"
  if verify_early_association_evidence "$evidence/stdout.raw" "$evidence/stderr.raw" "$command_status" "$root/expected.raw"; then
    actual_pass=1
  else
    actual_exit=$?
  fi
  printf '%s\t%s\t%s\t%s\n' "$name" "$expected_pass" "$actual_pass" "$actual_exit" >> "$root/results.tsv"
  test "$actual_pass" = "$expected_pass" || { printf 'FAIL: fixture %s\n' "$name" >&2; exit 1; }
  total=$((total + 1))
}

valid_fixture() {
  mkdir -p "$root/$1"
  printf '%s\n' "$expected" > "$root/$1/stdout.raw"
  : > "$root/$1/stderr.raw"
}

valid_fixture valid
check_fixture valid 0 1
valid_fixture empty
: > "$root/empty/stdout.raw"
check_fixture empty 0 0
valid_fixture skip
printf '%s\n' 'early_association_probe=skip; reason=unsupported' > "$root/skip/stdout.raw"
check_fixture skip 0 0
valid_fixture missing-lf
printf '%s' "$expected" > "$root/missing-lf/stdout.raw"
check_fixture missing-lf 0 0
valid_fixture two-lfs
printf '\n' >> "$root/two-lfs/stdout.raw"
check_fixture two-lfs 0 0
valid_fixture two-lines
printf '%s\n' "$expected" >> "$root/two-lines/stdout.raw"
check_fixture two-lines 0 0
valid_fixture crlf
printf '%s\r\n' "$expected" > "$root/crlf/stdout.raw"
check_fixture crlf 0 0
valid_fixture embedded-cr
printf '\r%s\n' "$expected" > "$root/embedded-cr/stdout.raw"
check_fixture embedded-cr 0 0
valid_fixture embedded-nul
printf '\000%s\n' "$expected" > "$root/embedded-nul/stdout.raw"
check_fixture embedded-nul 0 0
valid_fixture trailing-nul
printf '\000' >> "$root/trailing-nul/stdout.raw"
check_fixture trailing-nul 0 0
valid_fixture additional-bytes
printf 'suffix' >> "$root/additional-bytes/stdout.raw"
check_fixture additional-bytes 0 0
valid_fixture oversized
printf '%4096s\n' x > "$root/oversized/stdout.raw"
check_fixture oversized 0 0
valid_fixture nonempty-stderr
printf '%s\n' warning > "$root/nonempty-stderr/stderr.raw"
check_fixture nonempty-stderr 0 0
valid_fixture unknown-field
printf '%s; unknown=true\n' "$expected" > "$root/unknown-field/stdout.raw"
check_fixture unknown-field 0 0
valid_fixture reordered-fields
first=${expected%%; *}
rest=${expected#*; }
second=${rest%%; *}
printf '%s; %s; %s\n' "$second" "$first" "${rest#*; }" > "$root/reordered-fields/stdout.raw"
check_fixture reordered-fields 0 0
valid_fixture nonzero-exit
check_fixture nonzero-exit 7 0
valid_fixture unavailable-exit
check_fixture unavailable-exit unavailable 0
valid_fixture noncanonical-exit
check_fixture noncanonical-exit 00 0
valid_fixture missing-stdout
rm "$root/missing-stdout/stdout.raw"
check_fixture missing-stdout 0 0
valid_fixture missing-stderr
rm "$root/missing-stderr/stderr.raw"
check_fixture missing-stderr 0 0

remaining=$expected
while test -n "$remaining"; do
  part=${remaining%%; *}
  if test "$remaining" = "$part"; then remaining=; else remaining=${remaining#*; }; fi
  key=${part%%=*}
  value=${part#*=}
  case "$value" in
    true) changed=false ;;
    false) changed=true ;;
    *[!0-9]*) changed=invalid ;;
    *) if test "$value" = 999; then changed=998; else changed=999; fi ;;
  esac
  valid_fixture "$key-changed"
  printf '%s\n' "${expected/$part/$key=$changed}" > "$root/$key-changed/stdout.raw"
  check_fixture "$key-changed" 0 0
  valid_fixture "$key-missing"
  without=${expected/; $part/}
  if test "$without" = "$expected"; then without=${expected/$part; /}; fi
  printf '%s\n' "$without" > "$root/$key-missing/stdout.raw"
  check_fixture "$key-missing" 0 0
  valid_fixture "$key-duplicate"
  printf '%s; %s\n' "$expected" "$part" > "$root/$key-duplicate/stdout.raw"
  check_fixture "$key-duplicate" 0 0
done

printf 'PASS: %s strict raw-gate fixtures (frozen schema; no native execution).\n' "$total"

case "$(uname -s)" in
  Darwin|Linux)
    bash "$script_dir/macos-early-association-gate.sh" "$root/cli-valid" 3 1 -- \
      bash -c 'printf "%s\n" "$1"' fixture "$expected" > "$root/cli-valid.stdout" 2> "$root/cli-valid.stderr"
    cmp -s "$root/expected.raw" "$root/cli-valid.stdout"
    test ! -s "$root/cli-valid.stderr"
    if bash "$script_dir/macos-early-association-gate.sh" "$root/cli-command-nonzero" 3 1 -- \
      bash -c 'printf "%s\n" "$1"; exit 7' fixture "$expected"; then
      printf '%s\n' 'FAIL: CLI accepted command exit7 with exact Pass bytes.' >&2
      exit 1
    fi
    test "$(< "$root/cli-command-nonzero/native.command.exit.raw")" = 7
    cmp -s "$root/expected.raw" "$root/cli-command-nonzero/native.stdout.raw"
    if bash "$script_dir/macos-early-association-gate.sh" "$root/cli-skip" 3 1 -- \
      bash -c 'printf "%s\n" "early_association_probe=skip; reason=unsupported"'; then
      printf '%s\n' 'FAIL: CLI accepted Skip with command exit0.' >&2
      exit 1
    fi
    if bash "$script_dir/macos-early-association-gate.sh" "$root/cli-timeout-exits-zero" 1 1 -- \
      python3 -c 'import signal,sys,time; signal.signal(signal.SIGTERM, lambda *_: sys.exit(0)); print(sys.argv[1], flush=True); time.sleep(10)' "$expected"; then
      printf '%s\n' 'FAIL: CLI accepted timeout with exact Pass bytes and child exit0.' >&2
      exit 1
    fi
    test -f "$root/cli-timeout-exits-zero/watchdog.timeout.raw"
    test "$(< "$root/cli-timeout-exits-zero/native.command.exit.raw")" = 0
    cmp -s "$root/expected.raw" "$root/cli-timeout-exits-zero/native.stdout.raw"
    test ! -s "$root/cli-timeout-exits-zero/native.stderr.raw"
    printf '%s\n' 'PASS: 4 POSIX end-to-end CLI gate fixtures; no native execution.'
    ;;
  *) printf '%s\n' 'SKIP: POSIX CLI/watchdog fixtures do not claim Windows process-group evidence.' ;;
esac
