# Independent media-route cleanup observation

Date: 2026-10-04. Test-only repair of the Linux failure in
[CI 37200897609](https://github.com/happys2333/flowspan/actions/runs/37200897609)
at `781610a16b0d2c0293c91aa44081af9236c4588b`.

## Cause and deterministic reproduction

`CompleteRegistryDisposalAsync` starts independent route-cleanup workers before
joining them. Each worker yields before its cleanup. The old fixture waited for
the first candidate stream's `DisposeStarted`, then immediately required the
second route's `SecureFrameSession.Encrypt` to throw. Those are unrelated worker
milestones; the second worker may correctly be scheduled but not yet have
disposed its session. The first signal does not establish the second fact.

The exact clean base plus an independently gated second expiry-timer disposal
reproduced the CI's `Assert.Throws() Failure: No exception was thrown` in four
fresh processes (4/4 RED). A frozen fixture TimeProvider excludes route expiry
and handshake timeout as explanations. The prior unmodified focused test
passed one initial and 25 fresh local executions, demonstrating why repeated
green reruns alone did not diagnose the race.

## Repair and negative control

Only `RemoteWindowMediaAttachmentTests.cs` changes. The fixture waits for the
second route's own cleanup to begin, proves its session remains usable while
that cleanup is explicitly gated, releases that gate, then observes session
closure through the existing public `Encrypt` API with a two-second bound.
The first acceptance remains blocked throughout; the fixture never calls the
second registration's Dispose to initiate its cleanup. All fixture gates are
released in a finally block. Production code is unchanged.

A temporary mutation made registry disposal await the first route before
starting the second. The repaired test actually failed at its second-cleanup
milestone timeout (RED), proving it still detects serial cleanup. The mutation
was reversed with `apply_patch`; a production-file diff check confirmed no
remaining change, and the restored focused test passed (GREEN).

## Local verification boundary

The isolated checkout was based on exact `781610a` with only the repaired test
file dirty. Final test-file SHA-256:
`a7167de08286d82fd73cdfbde8c57203a2611f149bfc2cc74ab43997feebe045`.
Root transfer to the main workspace produced the same hash.

On the local macOS arm64 host, Debug and Release each passed the focused case
1/1, complete Transport suite 758/758, and 20 fresh focused processes 20/20.
All 40 stress TRX files were individually parsed: total/executed/passed are one
and every non-success counter is zero. Full isolated-solution formatting and
diff checks passed. This does not yet claim a repaired hosted CI or a complete
solution test run at a later integration commit.

Temporary RED, mutation and GREEN results are in
`/tmp/flowspan-route-dispose-flake/`. Reproduce the restored test with:

```sh
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Debug --filter FullyQualifiedName~RegistryDisposeStartsEveryOwnedRouteBeforeJoiningCleanup
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Release --filter FullyQualifiedName~RegistryDisposeStartsEveryOwnedRouteBeforeJoiningCleanup
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Release
```

The preventive rule is to await the owner-specific observable milestone, not
infer cross-worker ordering from another owner's signal. No production API or
architecture expansion was needed. The failed hosted run remains in the
[Windows WARP evidence](2026-10-04-windows-warp-readback.md).
