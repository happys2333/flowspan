#!/usr/bin/env python3
"""Portable gate fixtures: controlled raw records, not native Block execution."""

import argparse
import hashlib
import io
import json
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import unittest


SOURCE_SHA = "1ab251135f3617bc18e39d5f67e38885b53e85a7"
EXPECTED = (
    b"capture_completion_block_probe=pass mode=actual_one_argument_block_abi "
    b"real_completion_owner=true inert_preparation=true signature=v16@?0@8 "
    b"initial_copy_helpers=1 extra_heap_copies=1 extra_copy_helpers=0 "
    b"caller_releases=1 last_copy_releases=1 root_free_confirmed=true "
    b"native_retirement=true drain_pending_while_completed_observer_active=true "
    b"managed_drain=true abi_return_joined=true callbacks=1 "
    b"completed_notifications=1 contained_failures=0 sck_executed=false "
    b"tcc_preflight_called=false capture_proved=false v1_acceptance=false\n"
)
WATCHDOG = {
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
ROOT = None
RESULTS = []
SCRIPT = Path(__file__).resolve()
GATE = SCRIPT.with_name("macos-capture-completion-block-gate.py")


class GateContracts(unittest.TestCase):
    def run_case(self, name, changes=None, missing=(), expected_exit=1, source_sha=SOURCE_SHA):
        case = ROOT / name
        evidence = case / "capture-completion"
        evidence.mkdir(parents=True, exist_ok=False)
        records = {
            "../source.commit.raw": SOURCE_SHA.encode("ascii") + b"\n",
            "../checkout.commit.raw": SOURCE_SHA.encode("ascii") + b"\n",
            "native.stdout.raw": EXPECTED,
            "native.stderr.raw": b"",
            "gate.watchdog.exit.raw": b"0\n",
            "watchdog.exit.raw": b"0\n",
            "native.command.exit.raw": b"0\n",
            "watchdog.report.json": json.dumps(WATCHDOG, indent=2).encode("ascii") + b"\n",
        }
        records.update(changes or {})
        for name, value in records.items():
            if name not in missing:
                (evidence / name).write_bytes(value)
        command = [sys.executable, "-B", str(GATE), str(evidence), source_sha]
        completed = subprocess.run(command, capture_output=True, timeout=10)
        (case / "gate.stdout.raw").write_bytes(completed.stdout)
        (case / "gate.stderr.raw").write_bytes(completed.stderr)
        (case / "invocation.command.raw").write_bytes(
            (json.dumps(command) + "\n").encode("utf-8")
        )
        terminal = evidence / "gate.command.exit.raw"
        if terminal.exists():
            actual_terminal = case / "actual.gate.command.exit.raw"
            actual_terminal.write_bytes(
                f"{completed.returncode}\n".encode("ascii")
            )
        else:
            actual_terminal = terminal
            with terminal.open("xb") as stream:
                stream.write(f"{completed.returncode}\n".encode("ascii"))
        RESULTS.append({
            "name": case.name,
            "expected_exit": expected_exit,
            "actual_exit": completed.returncode,
            "stdout_sha256": hashlib.sha256(completed.stdout).hexdigest(),
            "stderr_sha256": hashlib.sha256(completed.stderr).hexdigest(),
            "fixture_scope": "CONTROLLED_RAW_RECORDS_NOT_NATIVE_EXECUTION",
        })
        self.assertEqual(completed.returncode, expected_exit, case.name)
        self.assertEqual(actual_terminal.read_bytes(), f"{expected_exit}\n".encode("ascii"), case.name)
        if expected_exit == 0:
            self.assertEqual(completed.stdout, EXPECTED, case.name)
            self.assertEqual(completed.stderr, b"", case.name)
            self.assertEqual(terminal.read_bytes(), b"0\n", case.name)
        else:
            self.assertEqual(completed.stdout, b"", case.name)
            self.assertTrue(completed.stderr.startswith(b"capture_completion_block_gate=fail reason="), case.name)
            self.assertLessEqual(len(completed.stderr), 256, case.name)
            self.assertEqual(completed.stderr.count(b"\n"), 1, case.name)
        return case

    def test_watchdog_native_exit_boolean_is_rejected(self):
        report = dict(WATCHDOG, native_exit=False)
        self.run_case("watchdog-native-exit-bool", {
            "watchdog.report.json": json.dumps(report).encode("ascii") + b"\n",
        })

    def test_deep_watchdog_json_has_one_bounded_rejection_line(self):
        nested_json = b"[" * 1500 + b"0" + b"]" * 1500
        self.assertEqual(len(nested_json), 3001)
        case = self.run_case("watchdog-deep-json", {
            "watchdog.report.json": nested_json,
        })
        # Python 3.14 may parse this depth, while older supported stdlibs raise
        # RecursionError. Both paths must reject with one exact bounded line.
        self.assertIn(
            (case / "gate.stderr.raw").read_bytes(),
            (
                b"capture_completion_block_gate=fail reason=unreadable_or_invalid_evidence\n",
                b"capture_completion_block_gate=fail reason=successful_watchdog_terminal_facts\n",
            ),
        )

    def test_exact_success_is_accepted(self):
        self.run_case("exact-success", expected_exit=0)

    def test_every_success_token_is_literal(self):
        tokens = EXPECTED.rstrip(b"\n").split(b" ")
        for index, token in enumerate(tokens):
            for operation in ("changed", "missing", "duplicate", "reordered"):
                with self.subTest(token=token, operation=operation):
                    altered = list(tokens)
                    if operation == "changed":
                        altered[index] = token.split(b"=", 1)[0] + b"=changed"
                    elif operation == "missing":
                        del altered[index]
                    elif operation == "duplicate":
                        altered.insert(index, token)
                    else:
                        other = index + 1 if index + 1 < len(tokens) else index - 1
                        altered[index], altered[other] = altered[other], altered[index]
                    self.run_case(f"stdout-token-{index:02d}-{operation}", {
                        "native.stdout.raw": b" ".join(altered) + b"\n",
                    })

    def test_stdout_byte_mutations_are_rejected(self):
        variants = {
            "unknown-token": EXPECTED[:-1] + b" unknown=true\n",
            "skip-exit-zero": b"capture_completion_block_probe=skip reason=unsupported_host native_pass=false\n",
            "missing-lf": EXPECTED[:-1],
            "crlf": EXPECTED[:-1] + b"\r\n",
            "nul-leading": b"\0" + EXPECTED,
            "nul-middle": EXPECTED[:10] + b"\0" + EXPECTED[10:],
            "nul-trailing": EXPECTED + b"\0",
            "extra-line": EXPECTED + b"\n",
            "extra-result": EXPECTED + EXPECTED,
            "oversize": b"x" * 4096 + b"\n",
            "leading-bom": b"\xef\xbb\xbf" + EXPECTED,
            "trailing-space": EXPECTED[:-1] + b" \n",
            "empty": b"",
        }
        for name, value in variants.items():
            with self.subTest(name=name):
                self.run_case("stdout-" + name, {"native.stdout.raw": value})

    def test_native_stderr_is_empty(self):
        for name, value in {"warning": b"warning\n", "lf": b"\n", "nul": b"\0", "oversize": b"x" * 4097}.items():
            with self.subTest(name=name):
                self.run_case("stderr-" + name, {"native.stderr.raw": value})

    def test_all_process_exit_raws_are_canonical_zero(self):
        values = [b"1\n", b"7\n", b"-9\n", b"00\n", b"+0\n", b"-0\n", b" 0\n",
                  b"0", b"0\r\n", b"0\n\n", b"false\n", b"0\0\n", b"0\n7\n",
                  b"not_started\n", b"", b"0" * 4097]
        for record in ("gate.watchdog.exit.raw", "watchdog.exit.raw", "native.command.exit.raw"):
            for index, value in enumerate(values):
                with self.subTest(record=record, value=value[:20]):
                    self.run_case(f"exit-{record}-{index:02d}", {record: value})

    def test_watchdog_has_exact_keys_values_and_types(self):
        changed = {
            "scope": "OTHER_SCOPE",
            "reason": "timeout",
            "signal_count": 1,
            "pending_signals_at_commit": [15],
            "terminal_signals_blocked": False,
            "group_signals": [15],
            "group_signal_errors": [{"signal": 9, "errno": 1}],
            "leader_joined": False,
            "native_exit": 7,
            "failure": "InjectedError",
        }
        wrong_type = {
            "scope": 0,
            "reason": False,
            "signal_count": False,
            "pending_signals_at_commit": {},
            "terminal_signals_blocked": 1,
            "group_signals": {},
            "group_signal_errors": {},
            "leader_joined": 1,
            "native_exit": 0.0,
            "failure": False,
        }
        for key in WATCHDOG:
            for operation in ("missing", "changed", "wrong-type", "duplicate"):
                with self.subTest(key=key, operation=operation):
                    report = dict(WATCHDOG)
                    if operation == "missing":
                        del report[key]
                    elif operation == "changed":
                        report[key] = changed[key]
                    elif operation == "wrong-type":
                        report[key] = wrong_type[key]
                    raw = json.dumps(report).encode("ascii")
                    if operation == "duplicate":
                        raw = raw[:-1] + b", " + json.dumps(key).encode("ascii") + b": " + json.dumps(WATCHDOG[key]).encode("ascii") + b"}"
                    self.run_case(f"watchdog-{key}-{operation}", {"watchdog.report.json": raw + b"\n"})

    def test_watchdog_malformed_or_unknown_metadata_is_rejected(self):
        variants = {
            "unknown-key": json.dumps(dict(WATCHDOG, unknown=True)).encode("ascii"),
            "invalid-json": b"{",
            "null": b"null",
            "list": b"[]",
            "string": b'"success"',
            "integer": b"0",
            "boolean": b"true",
            "duplicate-object": json.dumps(WATCHDOG).encode("ascii") + json.dumps(WATCHDOG).encode("ascii"),
            "utf8-error": b"\xff",
            "nul": json.dumps(WATCHDOG).encode("ascii") + b"\0",
            "native-exit-string": json.dumps(dict(WATCHDOG, native_exit="0")).encode("ascii"),
            "native-exit-null": json.dumps(dict(WATCHDOG, native_exit=None)).encode("ascii"),
            "signal-count-float": json.dumps(dict(WATCHDOG, signal_count=0.0)).encode("ascii"),
            "signal-count-string": json.dumps(dict(WATCHDOG, signal_count="0")).encode("ascii"),
            "signal-count-null": json.dumps(dict(WATCHDOG, signal_count=None)).encode("ascii"),
            "native-exit-nan": json.dumps(dict(WATCHDOG, native_exit=float("nan"))).encode("ascii"),
            "native-exit-infinity": json.dumps(dict(WATCHDOG, native_exit=float("inf"))).encode("ascii"),
        }
        canonical = json.dumps(WATCHDOG).encode("ascii")
        variants["oversize"] = canonical + b" " * (4097 - len(canonical))
        for name, value in variants.items():
            with self.subTest(name=name):
                self.run_case("watchdog-" + name, {"watchdog.report.json": value})

    def test_watchdog_mapping_order_and_boundary_size_are_not_stdout_schema(self):
        reversed_report = dict(reversed(list(WATCHDOG.items())))
        self.run_case("watchdog-reordered-mapping", {
            "watchdog.report.json": json.dumps(reversed_report).encode("ascii") + b"\n",
        }, expected_exit=0)
        raw = json.dumps(WATCHDOG).encode("ascii")
        self.run_case("watchdog-exact-4096-bytes", {
            "watchdog.report.json": raw + b" " * (4096 - len(raw)),
        }, expected_exit=0)

    def test_timeout_and_interruption_markers_are_rejected_even_if_empty(self):
        for record in ("watchdog.timeout.raw", "watchdog.interrupted.raw"):
            for index, value in enumerate((b"", b"timeout\n", b"interrupted\n")):
                with self.subTest(record=record, value=value):
                    self.run_case(f"marker-{record}-{index}", {record: value})

    def test_required_raw_records_cannot_be_missing(self):
        for record in ("../source.commit.raw", "../checkout.commit.raw", "native.stdout.raw",
                       "native.stderr.raw", "gate.watchdog.exit.raw", "watchdog.exit.raw",
                       "native.command.exit.raw", "watchdog.report.json"):
            with self.subTest(record=record):
                self.run_case("missing-" + Path(record).name, missing=(record,))

    def test_source_checkout_binding_is_exact_and_external_expected_sha_is_required(self):
        canonical = SOURCE_SHA.encode("ascii")
        variants = [b"b" * 40 + b"\n", canonical.upper() + b"\n", canonical + b"\r\n",
                    canonical[:-1] + b"\n", canonical, canonical + b"\n\n", canonical + b"\0\n",
                    b" " + canonical + b"\n", b""]
        for record in ("../source.commit.raw", "../checkout.commit.raw"):
            for index, value in enumerate(variants):
                with self.subTest(record=record, value=value[:45]):
                    self.run_case(f"binding-{Path(record).name}-{index:02d}", {record: value})
        self.run_case("both-bindings-wrong", {
            "../source.commit.raw": b"b" * 40 + b"\n",
            "../checkout.commit.raw": b"b" * 40 + b"\n",
        })
        for index, invalid in enumerate((SOURCE_SHA.upper(), SOURCE_SHA[:-1], SOURCE_SHA + "\n", "", "not-a-sha")):
            with self.subTest(source_sha=invalid):
                self.run_case(f"expected-sha-invalid-{index}", source_sha=invalid)

    def test_caller_supplied_expected_stdout_cannot_change_success_schema(self):
        skip = b"capture_completion_block_probe=skip reason=unsupported_host native_pass=false\n"
        self.run_case("caller-expected-stdout-skip", {
            "native.stdout.raw": skip,
            "expected.stdout.raw": skip,
        })
        self.run_case("caller-expected-stdout-ignored", {
            "expected.stdout.raw": b"untrusted_success\n",
        }, expected_exit=0)

    def test_preexisting_gate_terminal_result_is_not_execution(self):
        for index, value in enumerate((b"0\n", b"1\n", b"00\n")):
            with self.subTest(value=value):
                case = self.run_case(f"preexisting-gate-result-{index}", {"gate.command.exit.raw": value})
                self.assertEqual((case / "capture-completion/gate.command.exit.raw").read_bytes(), value)
                self.assertEqual((case / "actual.gate.command.exit.raw").read_bytes(), b"1\n")


def main():
    global ROOT
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evidence", type=Path)
    parser.add_argument("--tracer-only", action="store_true")
    parser.add_argument("--deep-json-tracer-only", action="store_true")
    args = parser.parse_args()
    ROOT = args.evidence.resolve()
    ROOT.mkdir(parents=True, exist_ok=False)
    sources = ROOT / "sources"
    sources.mkdir()
    repository = SCRIPT.parents[2]
    saved_ci = SCRIPT.with_name("ci.yml")
    ci_source = saved_ci if saved_ci.is_file() else repository / ".github/workflows/ci.yml"
    paths = [GATE, SCRIPT, GATE.with_name("posix-watchdog.py"), ci_source]
    manifest = []
    for path in paths:
        target = sources / path.name
        shutil.copyfile(path, target)
        data = target.read_bytes()
        manifest.append({"path": str(path), "saved": str(target), "bytes": len(data),
                         "sha256": hashlib.sha256(data).hexdigest()})
    (ROOT / "source.manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (ROOT / "campaign.command.raw").write_bytes((json.dumps(sys.argv) + "\n").encode("utf-8"))
    if args.deep_json_tracer_only:
        suite = unittest.TestSuite([GateContracts("test_deep_watchdog_json_has_one_bounded_rejection_line")])
    elif args.tracer_only:
        suite = unittest.TestSuite([GateContracts("test_watchdog_native_exit_boolean_is_rejected")])
    else:
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(GateContracts)
    diagnostics = io.StringIO()
    result = unittest.TextTestRunner(stream=diagnostics, verbosity=2).run(suite)
    (ROOT / "unittest.stderr.raw").write_bytes(diagnostics.getvalue().encode("utf-8"))
    sys.stderr.write(diagnostics.getvalue())
    (ROOT / "results.json").write_text(json.dumps({
        "scope": "PORTABLE_GATE_FIXTURES_NOT_NATIVE_BLOCK_OR_CAPTURE_EXECUTION",
        "python": platform.python_version(),
        "host": platform.platform(),
        "tests_run": result.testsRun,
        "failed": len(result.failures),
        "errors": len(result.errors),
        "cases": RESULTS,
    }, indent=2) + "\n", encoding="utf-8")
    print(f"gate_fixture_campaign={'pass' if result.wasSuccessful() else 'fail'} "
          f"tests={result.testsRun} cases={len(RESULTS)} native_execution=false")
    exit_code = 0 if result.wasSuccessful() else 1
    (ROOT / "campaign.exit.raw").write_bytes(f"{exit_code}\n".encode("ascii"))
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
