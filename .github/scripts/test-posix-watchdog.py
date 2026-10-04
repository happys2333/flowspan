#!/usr/bin/env python3
"""Process-only watchdog contracts, not macOS API or native-cleanup evidence."""

import json
import importlib.util
import os
from pathlib import Path
import signal
import subprocess
import sys
import time


def fixture(name, evidence):
    evidence = Path(evidence)
    if name in ("ignore-term", "stopped", "descendant"):
        signal.signal(signal.SIGTERM, signal.SIG_IGN)
    if name in ("exit-zero-on-term", "descendant"):
        signal.signal(signal.SIGTERM, lambda *_: sys.exit(0))
    if name == "descendant":
        pid = os.fork()
        if pid == 0:
            signal.signal(signal.SIGTERM, signal.SIG_IGN)
            while True:
                time.sleep(1)
        (evidence / "fixture.descendant.pid.raw").write_text(f"{pid}\n")
    print("fixture_pass", flush=True)
    (evidence / "fixture.ready.raw").write_text("ready\n")
    if name == "normal":
        return 0
    if name == "nonzero":
        return 7
    if name == "stderr":
        print("fixture_warning", file=sys.stderr, flush=True)
        return 0
    if name == "stopped":
        os.kill(os.getpid(), signal.SIGSTOP)
    while True:
        time.sleep(1)


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "--inject-write-failure":
        sys.dont_write_bytecode = True
        evidence = sys.argv[2]
        watchdog_path = Path(__file__).with_name("posix-watchdog.py")
        spec = importlib.util.spec_from_file_location("watchdog_io_fixture", watchdog_path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        original_write = Path.write_text

        def failing_write(path, *args, **kwargs):
            if path.name == "native.pid.raw":
                raise OSError("Injected evidence write failure after Popen")
            return original_write(path, *args, **kwargs)

        Path.write_text = failing_write
        sys.argv = [str(watchdog_path), evidence, "1", "1", "--", sys.executable,
                    str(Path(__file__).resolve()), "--fixture", "ignore-term", evidence]
        return module.main()
    if len(sys.argv) > 1 and sys.argv[1] == "--fixture":
        return fixture(sys.argv[2], sys.argv[3])
    if os.name != "posix":
        print("SKIP: POSIX process-group watchdog contracts are not Windows evidence.")
        return 0
    root = Path(sys.argv[1])
    root.mkdir(parents=True, exist_ok=False)
    script = Path(__file__).resolve()
    watchdog = script.with_name("posix-watchdog.py")
    results = []

    def run_case(name, mode, expected, interrupted=False, inherited_sigchld_ignore=False):
        evidence = root / name
        command = [sys.executable, str(watchdog), str(evidence), "1", "1", "--",
                   sys.executable, str(script), "--fixture", mode, str(evidence)]
        options = {"stdout": subprocess.PIPE, "stderr": subprocess.PIPE}
        if inherited_sigchld_ignore:
            options["preexec_fn"] = lambda: signal.signal(signal.SIGCHLD, signal.SIG_IGN)
        watcher = subprocess.Popen(command, **options)
        if interrupted:
            ready_deadline = time.monotonic() + 3
            while not (evidence / "fixture.ready.raw").exists():
                assert time.monotonic() < ready_deadline, name
                time.sleep(0.01)
            for signum in (signal.SIGTERM, signal.SIGHUP, signal.SIGINT):
                os.kill(watcher.pid, signum)
                time.sleep(0.02)
        stdout, stderr = watcher.communicate(timeout=10)
        if expected == 124:
            # Darwin may report EPERM for final KILL of an unreaped zombie-only
            # group. Preserve that as fail-closed127, never balanced success.
            assert watcher.returncode in (124, 127), (name, watcher.returncode, stdout, stderr)
        else:
            assert watcher.returncode == expected, (name, watcher.returncode, stdout, stderr)
        report = json.loads((evidence / "watchdog.report.json").read_text())
        if watcher.returncode == 127:
            assert report["failure"] == "PermissionError", (name, report)
            assert report["group_signal_errors"] == [{"signal": 9, "errno": 1}], (name, report)
        assert report["leader_joined"], (name, report)
        assert (evidence / "native.stdout.raw").read_bytes() == b"fixture_pass\n", name
        assert report["group_signals"] == ([] if expected in (0, 1) else [15, 9]), (name, report)
        if expected == 124:
            assert (evidence / "watchdog.timeout.raw").exists(), name
        if interrupted:
            assert report["signal_count"] == 3, (name, report)
            assert (evidence / "watchdog.interrupted.raw").exists(), name
        if mode == "exit-zero-on-term" or mode == "descendant":
            assert report["native_exit"] == 0, (name, report)
        if mode == "descendant":
            descendant = int((evidence / "fixture.descendant.pid.raw").read_text())
            gone_deadline = time.monotonic() + 3
            while True:
                try:
                    os.kill(descendant, 0)
                except ProcessLookupError:
                    break
                assert time.monotonic() < gone_deadline, (name, "descendant_survived")
                time.sleep(0.02)
        results.append({"name": name, "exit": watcher.returncode, "report": report})

    run_case("normal", "normal", 0)
    run_case("nonzero", "nonzero", 1)
    run_case("stderr-retained", "stderr", 0)
    run_case("timeout-term-ignored", "ignore-term", 124)
    run_case("timeout-leader-exits-zero", "exit-zero-on-term", 124)
    run_case("timeout-descendant", "descendant", 124)
    run_case("timeout-stopped-child", "stopped", 124)
    run_case("supervisor-repeated-interruptions", "ignore-term", 125, interrupted=True)
    run_case("inherited-sigchld-ignore", "descendant", 124, inherited_sigchld_ignore=True)

    launch_evidence = root / "launch-failure"
    completed = subprocess.run(
        [sys.executable, str(watchdog), str(launch_evidence), "1", "1", "--", str(root / "nonexistent")],
        capture_output=True, timeout=3)
    assert completed.returncode == 126
    assert json.loads((launch_evidence / "watchdog.report.json").read_text())["failure"] == "FileNotFoundError"
    results.append({"name": "launch-failure", "exit": completed.returncode})

    io_evidence = root / "evidence-write-failure-after-spawn"
    completed = subprocess.run([sys.executable, str(script), "--inject-write-failure", str(io_evidence)],
                               capture_output=True, timeout=10)
    assert completed.returncode in (126, 127), (completed.returncode, completed.stderr)
    io_report = json.loads((io_evidence / "watchdog.report.json").read_text())
    assert io_report["reason"] == "supervisor_failure", io_report
    assert io_report["leader_joined"], io_report
    assert io_report["group_signals"] == [15, 9], io_report
    results.append({"name": "evidence-write-failure-after-spawn", "exit": completed.returncode,
                    "report": io_report})

    previous = root / "existing-evidence"
    previous.mkdir()
    (previous / "sentinel.raw").write_bytes(b"preserve_previous_evidence\n")
    completed = subprocess.run([sys.executable, str(watchdog), str(previous), "1", "1", "--", "false"],
                               capture_output=True, timeout=3)
    assert completed.returncode == 2
    assert sorted(path.name for path in previous.iterdir()) == ["sentinel.raw"]
    assert (previous / "sentinel.raw").read_bytes() == b"preserve_previous_evidence\n"
    results.append({"name": "existing-evidence-preserved", "exit": completed.returncode})
    (root / "results.json").write_text(json.dumps(results, indent=2) + "\n")
    print(f"PASS: {len(results)} POSIX watchdog contracts; no native execution or native cleanup claim.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
