# ADR-0001: Single-writer authority and a fail-closed replication kernel

Status: accepted for the clean-room kernel; Cities: Skylines runtime feasibility remains a release gate.

## Decision

Only the host may execute authoritative world mutations. A client sends an intent with an authenticated connection identity supplied by the transport, a session incarnation, a monotonic request ID and an expected world revision. The host validates, executes once within that peer incarnation, and records an absolute outcome with a global commit revision and before/after state digests. Host-local tools must eventually use the same route.

Replicas never turn a network tool invocation directly into an authoritative local operation. They consume consecutive host outcomes. A failed/ambiguous game operation fences the affected world; it is not repaired by clearing a flag and continuing. The kernel will distinguish rejection without mutation, gaps eligible for journal replay, and failures requiring a verified snapshot.

The game runs an old managed runtime. Production libraries target .NET Framework 3.5; the same source also targets .NET 8 for tests and development tools. Public CI compiles both, executes net8 on Windows/Linux and net35 on Mono. None of those proves operation inside the game's bundled Mono.

## Rejected alternatives

- Unproven whole-game deterministic lockstep: synchronized seeds and frame numbers do not establish deterministic scheduling, floating point, pathfinding or arbitrary third-party code.
- CSM-style distributed tool replay: reproducing calls, nested allocation order and side effects is not the same as replicating committed world state.
- Full save transfer for every edit: useful recovery mechanism, unsuitable as the normal replication path.
- Arbitrary local rollback: the game does not offer a demonstrated transaction covering all managers and mod side effects.

## Evidence

CSM master was inspected at `45c4ea3a402422e00e09c7f0840ae59eac2ed58c`, including `CommandReceiver`, `TransactionHandler`, `TickLoopHandler`, `ArrayHandler` and the net35 project. CSM-CQU dev was inspected at `c713d931f0ed87725ef320a37d83f115803c535e`, including its receiver, bounded transactions and session-consistency specification. Its existing safety and diagnostic improvements are acknowledged; no old code is copied here.

- [CSM transaction implementation](https://github.com/CitiesSkylinesMultiplayer/CSM/blob/45c4ea3a402422e00e09c7f0840ae59eac2ed58c/src/csm/Commands/TransactionHandler.cs)
- [CSM allocation replay](https://github.com/CitiesSkylinesMultiplayer/CSM/blob/45c4ea3a402422e00e09c7f0840ae59eac2ed58c/src/basegame/Injections/ArrayHandler.cs)
- [Deterministic lockstep, Glenn Fiedler](https://gafferongames.com/post/deterministic_lockstep/)
- [Microsoft reference assemblies](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/reference-assemblies)

## Hard gate before gameplay

Demonstrate that client authoritative simulation can be isolated without disabling rendering, UI, tool previews or required main/simulation-thread dispatch. Demonstrate side-effect-free result application, complete domain coverage, safe save/load and actual Harmony behavior inside the shipped game runtime. Until then, the executable integer-slot world is a reference domain, not a city, and the repository is not a playable mod.
