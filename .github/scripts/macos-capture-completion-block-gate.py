#!/usr/bin/env python3
"""Strict one-argument Block evidence gate; never proves Capture or native cleanup."""

import argparse
import json
from pathlib import Path
import re
import sys


MAX_RECORD_BYTES = 4096
SUCCESS_RECORD = (
    b"capture_completion_block_probe=pass mode=actual_one_argument_block_abi "
    b"real_completion_owner=true inert_preparation=true signature=v16@?0@8 "
    b"initial_copy_helpers=1 extra_heap_copies=1 extra_copy_helpers=0 "
    b"caller_releases=1 last_copy_releases=1 root_free_confirmed=true "
    b"native_retirement=true drain_pending_while_completed_observer_active=true "
    b"managed_drain=true abi_return_joined=true callbacks=1 "
    b"completed_notifications=1 contained_failures=0 sck_executed=false "
    b"tcc_preflight_called=false capture_proved=false v1_acceptance=false\n"
)
SUCCESS_WATCHDOG = {
    "scope": "POSIX_TASK_PROCESS_GROUP_NOT_NATIVE_CLEANUP",
    "reason": "exited",
    "signal_count": 0,
    "pending_signals_at_commit": [],
    "terminal_signals_blocked": True,
    "group_signals": [],
    "group_signal_errors": [],
    "leader_joined": True,
    "native_exit": 0,
    "failure": None,
}


class GateFailure(Exception):
    """A bounded, non-sensitive rejection reason."""


def require(condition, reason):
    if not condition:
        raise GateFailure(reason)


def read_bounded(path):
    with path.open("rb") as stream:
        value = stream.read(MAX_RECORD_BYTES + 1)
    require(len(value) <= MAX_RECORD_BYTES, "bounded_record")
    return value


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "duplicate_watchdog_key")
        result[key] = value
    return result


def require_absent(path, reason):
    require(not path.exists() and not path.is_symlink(), reason)


def verify(evidence, source_sha):
    require(re.fullmatch(r"[0-9a-f]{40}", source_sha) is not None, "source_sha_format")
    binding = source_sha.encode("ascii") + b"\n"
    for name in ("source.commit.raw", "checkout.commit.raw"):
        require(read_bounded(evidence.parent / name) == binding, "source_checkout_binding")

    # The caller may record our real terminal result only after this CLI returns.
    # A pre-existing, caller-created success cannot stand in for that execution.
    require_absent(evidence / "gate.command.exit.raw", "preexisting_gate_result")
    require(read_bounded(evidence / "native.stdout.raw") == SUCCESS_RECORD, "exact_success_record")
    require(read_bounded(evidence / "native.stderr.raw") == b"", "native_stderr")
    for name in ("gate.watchdog.exit.raw", "watchdog.exit.raw", "native.command.exit.raw"):
        require(read_bounded(evidence / name) == b"0\n", "process_exit")
    for name in ("watchdog.timeout.raw", "watchdog.interrupted.raw"):
        require_absent(evidence / name, "watchdog_not_success")

    report = json.loads(
        read_bounded(evidence / "watchdog.report.json"),
        object_pairs_hook=unique_object,
    )
    require(report == SUCCESS_WATCHDOG, "successful_watchdog_terminal_facts")
    require(
        all(type(report[key]) is type(value) for key, value in SUCCESS_WATCHDOG.items()),
        "watchdog_terminal_field_types",
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evidence", type=Path)
    parser.add_argument("source_sha")
    args = parser.parse_args()
    try:
        verify(args.evidence, args.source_sha)
    except GateFailure as error:
        sys.stderr.write("capture_completion_block_gate=fail reason=" + str(error) + "\n")
        return 1
    except (OSError, ValueError, TypeError, RecursionError):
        # Raw source, paths and exception text are not outward diagnostics.
        sys.stderr.write("capture_completion_block_gate=fail reason=unreadable_or_invalid_evidence\n")
        return 1
    sys.stdout.buffer.write(SUCCESS_RECORD)
    return 0


if __name__ == "__main__":
    sys.exit(main())
