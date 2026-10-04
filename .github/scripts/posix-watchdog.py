#!/usr/bin/env python3
"""Bound one task-owned POSIX process group; never claim native cleanup."""

import argparse
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("evidence")
    parser.add_argument("deadline", type=int)
    parser.add_argument("grace", type=int)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if os.name != "posix" or args.deadline <= 0 or args.grace <= 0 or not command:
        return 2
    evidence = Path(args.evidence)
    try:
        evidence.mkdir(parents=True, exist_ok=False)
    except OSError:
        return 2  # Never replace a previous evidence directory.

    interruptions = [0]

    def interrupted(signum, _frame):
        interruptions[0] += 1  # Never asynchronously kill a numeric PID.

    handled_signals = (signal.SIGINT, signal.SIGTERM, signal.SIGHUP)
    for signum in handled_signals:
        signal.signal(signum, interrupted)
    # Inherited SIG_IGN could otherwise auto-reap our child and invalidate the
    # kernel PID/PGID pin during timeout grace.
    signal.signal(signal.SIGCHLD, signal.SIG_DFL)
    child = None
    reason = "launch_failure"
    joined = False
    failure = None
    exit_code = 126
    group_signals = []
    group_signal_errors = []
    try:
        with (evidence / "native.stdout.raw").open("xb") as stdout, (
            evidence / "native.stderr.raw"
        ).open("xb") as stderr:
            if interruptions[0]:
                reason = "interrupted_before_launch"
                exit_code = 125
            else:
                child = subprocess.Popen(
                    command, stdout=stdout, stderr=stderr, start_new_session=True
                )
                (evidence / "native.pid.raw").write_text(f"{child.pid}\n")
                deadline = time.monotonic() + args.deadline
                while True:
                    if interruptions[0]:
                        reason = "interrupted"
                        exit_code = 125
                        break
                    remaining = deadline - time.monotonic()
                    if remaining <= 0:
                        reason = "timeout"
                        exit_code = 124
                        break
                    try:
                        result = child.wait(timeout=min(0.05, remaining))
                    except subprocess.TimeoutExpired:
                        continue
                    # wait returned/reaped: never issue another group signal,
                    # even if interruption lands during result assignment.
                    joined = True
                    if time.monotonic() >= deadline:
                        reason = "timeout"
                        exit_code = 124
                    else:
                        reason = "exited"
                        exit_code = 125 if interruptions[0] else (0 if result == 0 else 1)
                    break
    except Exception as error:
        failure = type(error).__name__
        reason = "supervisor_failure"
        exit_code = 126
    finally:
        if child is not None and child.returncode is None:
            # No wait/poll/communicate occurs between this ownership check and
            # final KILL. A TERM-exited leader stays unreaped, pinning PID/PGID.
            for signum in (signal.SIGTERM, signal.SIGKILL):
                group_signals.append(signum)
                try:
                    os.killpg(child.pid, signum)
                except ProcessLookupError:
                    pass
                except OSError as error:
                    group_signal_errors.append({"signal": signum, "errno": error.errno})
                    failure = type(error).__name__
                    exit_code = 127
                if signum == signal.SIGTERM:
                    grace_deadline = time.monotonic() + args.grace
                    while time.monotonic() < grace_deadline:
                        time.sleep(min(0.05, max(0, grace_deadline - time.monotonic())))
            try:
                child.wait(timeout=5)
                joined = True
            except subprocess.TimeoutExpired:
                failure = "FinalJoinTimeout"
                exit_code = 127
        # Terminal commit boundary: block handled signals before snapshotting
        # evidence. Signals already pending at this boundary still fail closed;
        # no handler can mutate the snapshot during the final writes/return.
        signal.pthread_sigmask(signal.SIG_BLOCK, handled_signals)
        pending = sorted(set(signal.sigpending()).intersection(handled_signals))
        if (interruptions[0] or pending) and exit_code == 0:
            exit_code = 125
        report = {
            "scope": "POSIX_TASK_PROCESS_GROUP_NOT_NATIVE_CLEANUP",
            "reason": reason,
            "signal_count": interruptions[0],
            "pending_signals_at_commit": pending,
            "terminal_signals_blocked": True,
            "group_signals": group_signals,
            "group_signal_errors": group_signal_errors,
            "leader_joined": joined,
            "native_exit": child.returncode if child is not None else None,
            "failure": failure,
        }
        try:
            if reason == "timeout":
                (evidence / "watchdog.timeout.raw").write_text("deadline_exceeded\n")
            if interruptions[0] or pending:
                (evidence / "watchdog.interrupted.raw").write_text("interrupted\n")
                if exit_code == 0:
                    exit_code = 125
            (evidence / "watchdog.report.json").write_text(json.dumps(report, indent=2) + "\n")
            native_exit = child.returncode if child is not None else "not_started"
            (evidence / "native.command.exit.raw").write_text(f"{native_exit}\n")
            (evidence / "watchdog.exit.raw").write_text(f"{exit_code}\n")
        except OSError:
            exit_code = 127
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
