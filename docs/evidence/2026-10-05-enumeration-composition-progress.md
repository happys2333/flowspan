# macOS enumeration staged composition — 2026-10-05

This is a local MEP1-MEP8 checkpoint, not complete slice, production,
cross-platform, or v1 acceptance. The long-term Goal remains active.
Base: `2678dd59eaaf201af3c58703c085dea78f20f50e`.
Immutable stages: `/tmp/flowspan-enumeration-composition-20261005/`.

## Actual vertical behaviors

Each pair executes one exact focused behavior through production
`EnumerateCoreAsync`, the original ownership pool and staged Block primitive.
Only system effects are controlled; actual primitive ABI helpers run.
The complete test file is byte-identical within each pair.

| RED → GREEN | Actual RED behavior | Result |
| --- | --- | --- |
| 01 → 02 | Root after-effect fault loses the primitive shell from the original batch graph after fixture roots are removed | 1 Failed → 1 Passed |
| 03 → 04 | Caller release returns enumeration/batch while an extra native copy still exists | 1 Failed → 1 Passed |
| 05 → 06 | Later last-copy root-free fault leaves retirement wait pending; expected fatal becomes timeout | 1 Failed → 1 Passed |
| 08 → 09 | Known source cleanup waits behind delayed native retirement | 1 Failed → 1 Passed |
| 14 → 15 | Throwing failure sink prevents result publication; expected fatal becomes timeout | 1 Failed → 1 Passed |
| 17 → 18 | Source filter notification skips window release and replaces earlier fatal | 1 Failed → 1 Passed |
| 19 → 20 | Source factory notification replaces original unknown-retain fatal | 1 Failed → 1 Passed |

Eight additional composition behaviors pass directly, with no claimed RED:
copy after-effect graph retention; native retirement before later managed
observer exit; reentrant settlement during caller release; access revoked
before dispatch; unknown copied dispatch with a late real callback; healthy
nonempty original source-charge transfer; content cleanup with throwing sink;
and earlier factory notification fatal before later filter cleanup fatal.
Stage21 focused Debug executes15 composition,26 enumeration effects,
19 staged primitive and38 source-producer cases: **98 Passed**.

The actual completion now performs inert `Prepare` → same-batch attachment →
`AcquireCopy`. Legacy Capture `Create` is unchanged. Caller release precedes
native retirement waits. Native retirement and terminal managed drain remain
independent from first-idle content use and final ABI return. Preallocated failure
signaling avoids impossible waits after unknown effects. No retry, finalizer,
timeout or GC returns unknown root/copy/dispatch/release/content/pool debt.
Confirmed sources clean up before delayed native retirement. Notification faults
cannot skip result publication or known independent cleanup; first-observed
nested fatal survives enumeration, source owner and source factory paths.
Batch settlement names the complete enumeration lifecycle, not content exit.

## Frozen local gates

Stages22/23 actually pass locked restore and solution builds with0 warnings/errors.
Debug/Release each pass **12 TRX / 2948 tests**, including macOS377, with all
non-success counters0. Each stage binds770 selected source inputs and1540 complete
bin runtime files before/after/current. Full canonical D/R identities are equal;
all2933 prior identities remain, and exactly15 composition identities are added.
The source selected lists and hash mappings are equal D/R.
Quality candidate02 runs locked restore and `format --verify-no-changes
--no-restore` for both solution and standalone BlockProbe: all actual exits0.
Its770 selected inputs remain unchanged;316 obj outputs and24 exact coverage
mapping text files are separately classified, not claimed tested runtime.

Recorder SHA256:
`b3408ddea54a4206e1398ea599a34ad463d0006f3c59486a6db9f1217923950b`.
Stage auditor SHA256:
`4b7faa078c0767450b7d13282f59bf39ad108d0654d98424532eb0daf82942e9`.
Gate identity auditor SHA256:
`f1f3ce4d71570fc0814954371c7d29368fda6688e29bdd9e0e8909609e9fc79e`.
Gate report SHA256:
`3bc319611ea46a350c083880abbf425e29daa6ab1ecf0c6a23c290023ff71add`.
Quality report SHA256:
`b7ec70f640549e8e25d38cdda8a5abf3737cd7eb97c7a89ea5ba8fc3b227d706`.

Stages12/13 (2943 each) and quality01 are preserved **superseded checkpoints**,
not final-source gates. Quality01's first auditor rejected generated coverage
mapping files; its failed report remains. Corrected classification is saved-data
audit only, not a new format/test execution. Root saved-data replay of final
quality and gate evidence actually exits0, `violations=[]`; both reports are
byte-identical to the independent worker reports.

## Native and review boundary

Final-run01 under `/tmp/flowspan-enumeration-composition-native-20261005/`
actually executes both Block modes and selected task-owned healthy SCK D/R:
six fresh runs, all outer/observer/watchdog/native exits0, empty stderr,0 Skip.
Four isolated locked restores/builds pass with0 warnings/errors. Each complete
bin runtime contains57 files; all770 frozen files and285 selected live production/
probe/config files bind before/after each run. Saved-data and current-immutable
audits, plus actual root replays, each exit0 with no violations; corresponding
reports are byte-identical. Source inventories are byte binding, not proof that
every selected file was a compiler input. Main solution and each probe have their
own independently recorded runtime inventories.
The initial pre-review candidate retains two successful Block builds and two
Capture builds with9 unresolved-reference errors each; no native mode ran.
Final canonical relative-path builds pass, but changed source means this is not
a same-source isolation proof for the `/tmp` versus `/private/tmp` hypothesis.

Native audit script SHA256:
`a4a11b0eca5a4abefcda2acc2c6d86a1d6dd5069582035a59feb556fde5057a2`.
Execution-time recorder SHA256:
`eb2f35d883451617c9d6f9c7c3caa16f5df0e06ad0687145476bf9b20329238f`.
Saved report SHA256:
`154edbb84b0c60b54b87474cfda362f1363b895d465ca83a8b9c92a9be74dcfa`.
Current immutable binding report SHA256:
`4c2fd3121700b89d2602c2a037257106955a0e24339258b6d7de6824439f2873`.
Complete raw campaign details and exact exclusive-create replay arguments:
`/tmp/flowspan-enumeration-composition-native-20261005/final-run01/SUMMARY.md`.
Debug/Release capture marker counts are2/1 and1/1; their raw logs differ and are
not claimed byte-identical. Finite600ms zero-late-delivery observations and final
zero roots/owners are selected tool facts, not all-native-callback/cleanup proof.

`BlockProbe --run-enumeration` uses real macOS Block operations, original pool
and production core, but controlled access/application/content/list/pool effects.
It cannot establish actual SCK dispatch/content/list ownership. Selected
`NativeCaptureProbe --run` is separate healthy SCK regression, not native fault
injection, protection, or complete native lifetime acceptance. Skip is not pass;
watchdog termination never proves cleanup or returns product capacity.

Both review axes exposed the inherited fallible-sink defects. Actual RED behavior
preceded repair. Final Standards and Spec static re-reviews each report0 unresolved
concrete findings. Review is not an additional test/native execution.
Documentary edits after freezing do not support full live-tree equality; only
explicitly compared compiler/probe/test inputs may be called freeze-identical.

## Open acceptance

Tasks2/3/4/5/6 and full MEP remain open until
fresh exact-commit Windows/macOS/Linux CI/CodeQL are verified. No GitHub message
was posted; exact target/text approval remains required for issue/PR/comment/
review/discussion. Production remains `delegate=0`, macOS14.2/Arm64 candidate,
Protection Unknown and sharing unavailable. Global Capture admission, secure
input/protection, Emergency Stop, physical LAN, minimum-platform/accessibility,
independent security review, signed install/update/uninstall and full v1 remain
unverified.
