# Development contract

## Scope and truthfulness

Implement only in CSM-Forge. CSM and CSM-CQU are read-only research sources. Do not copy their code. Keep reference-domain tests explicitly separate from real Cities: Skylines integration. Never label a probe, interface, standalone Mono test or mocked engine boundary as successful game integration.

## Invariants

- Only the host commits authoritative world changes. Host-local edits must eventually use the same authority path.
- Authenticate the connection and approve compatibility before HostSession.RegisterPeer. Connection GUIDs are fresh, trusted transport incarnations, not peer-supplied identity claims.
- Core has one owning thread; network callbacks post bounded immutable messages. Never hold a network lock while calling game APIs.
- Transport delivery is not a committed mutation or durable save. Preserve session, request, revision, content-hash and state-hash distinctions.
- Unknown/partial world application fences the affected world. Do not swallow the failure and continue or claim game rollback.
- An old snapshot/epoch/request cannot restore readiness. A matching host barrier is required after recovery.
- Bounds are part of the protocol. Reject oversize input before allocation. No BinaryFormatter or reflection-driven wire deserialization.
- Game runtime is net35. New APIs must compile against its reference assemblies. A net8 test success alone is insufficient.
- Do not include game DLLs, saves, credentials, private logs, automatic install steps or native library replacements.

## Validation

Run `dotnet build tests/Forge.Tests/Forge.Tests.csproj -c Release`, then run the net8 test executable; on Linux also execute the net35 binary with Mono. CI is the portable verification source when no local SDK is available. Inspect the exact commit's run, not an older green result. Record unexecuted game tests explicitly.

Add deterministic regression tests for each protocol/state bug. Fault injection belongs at external world/storage/transport/time boundaries; do not rewrite production sources in tests. Any native/game-dependent change requires the separate acceptance matrix in docs/TESTING.zh-CN.md.
