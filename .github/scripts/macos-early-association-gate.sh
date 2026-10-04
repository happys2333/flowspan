#!/usr/bin/env bash
# Exact raw-byte gate. The watchdog is POSIX-only and is not native cleanup.

early_association_expected() {
  printf '%s\n' 'early_association_probe=pass; mode=foundation; permanent_bridges=1; process_routers=1; process_coordinators=1; probe_gc_handles=0; method_signatures=4; tag_budget=16; maximum_tags_in_use=2; tags_allocated=4; tag_owners_released=4; tag_dealloc_entries=4; tag_super_deallocations=4; tags_in_use=0; quarantined_tags=0; source_owners_acquired=6; source_owners_released=6; source_reference_retains=11; source_reference_releases=11; tag_reference_retains=7; tag_reference_releases=7; association_reads=26; protocol_reads=18; nil_protocol_reads=11; native_callbacks=10; charged_ownership=0; uncertain_ownership=0; native_admission_closed=false; pending_replay_notifications=1; publication_race_notifications=1; replacement_notifications=0; ambiguity_notifications=0; ambiguity_nil_reads=3; quarantined_initializers=2; same_source_pending_proved=true; publication_race_proved=true; ambiguity_poison_proved=true; forced_gc_rounds=2; contained_failures=0; capture_executed=false; SCStream_creations=0; AppKit_initialized=false; permissions_requested=0; pixel_reads=0'
}

verify_early_association_evidence() {
  local stdout_path=$1 stderr_path=$2 exit_code=$3 expected_path=$4
  test -f "$stdout_path" && test -f "$stderr_path" && test -f "$expected_path" || return 1
  test "$exit_code" = 0 || return 1
  test ! -s "$stderr_path" || return 1
  test "$(wc -c < "$stdout_path")" -le 4096 || return 1
  # This comparison includes the sole final LF. NUL/CR, extra LF/bytes,
  # changed/missing/duplicate/unknown/reordered fields all fail literally.
  cmp -s "$expected_path" "$stdout_path"
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then
  set -euo pipefail
  test "$#" -ge 5
  evidence=$1 deadline=$2 grace=$3
  shift 3
  test "$1" = --
  shift
  test ! -e "$evidence"
  script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
  if python3 "$script_dir/posix-watchdog.py" "$evidence" "$deadline" "$grace" -- "$@"; then
    status=0
  else
    status=$?
  fi
  # A failed launch/duplicate-directory invocation owns no post-run writes.
  # Its watchdog already retains any raw evidence it successfully created.
  test "$status" = 0 || exit "$status"
  early_association_expected > "$evidence/expected.stdout.raw"
  printf '%s\n' "$status" > "$evidence/gate.command.exit.raw"
  test ! -e "$evidence/watchdog.timeout.raw"
  test ! -e "$evidence/watchdog.interrupted.raw"
  test "$(< "$evidence/native.command.exit.raw")" = 0
  verify_early_association_evidence "$evidence/native.stdout.raw" "$evidence/native.stderr.raw" "$status" "$evidence/expected.stdout.raw"
  cat "$evidence/native.stdout.raw"
fi
