# Development contract

## Scope and current truth

Write only to jiangkaiqi2005/CSM-Forge. CSM and CSM-CQU are read-only research references; do not copy their source or continue development there.

The accepted target is real-time host-authoritative cooperative play, independent parallel hot joins, and fault-contained recovery. Paused two-player editing is an internal proof slice, not the final product. Current runtime code is still the M0 reference kernel/v1 codec and an unverified ICities probe. Documentation does not implement v2 capabilities.

Read docs/TECHNICAL-SPEC.zh-CN.md, the relevant docs/spec document, and docs/ROADMAP.zh-CN.md before changes. docs/spec/spec-index.json tracks 20 requirements, 12 work packages and their acceptance evidence. History documents are not current normative requirements. Keep status and evidence honest.

## Critical invariants

- One host writer for persistent world facts, including host-local tools and natural simulation outcomes. Never rely on whole-game client determinism or distributed tool replay.
- Authenticated transport connections, logical members, world epochs, joins, transfers, loads, operations and commit revisions are different identities.
- Parallel joins have independent contexts/cancellation/progress. Immutable checkpoints may be shared; mutable stream positions and game worlds may not.
- Validate Ready against retained fixed historical roots; do not remove an equality check without implementing the full H/A barrier and activation protocol.
- Atomically bridge snapshot baseline to retained log and history to live stream. Do not wait for all remote players before the host resumes play.
- Only the owning game thread writes world state. Network callbacks post bounded immutable DTOs and check incarnation/generation before queueing AND execution.
- Received, queued, mirrored, game-published, applied-acked and checkpoint-durable are distinct states. A game projection failure cannot be hidden by a correct mirror hash.
- Unknown partial application fences the smallest trustworthy scope. Client failure does not automatically stop the room; host world corruption prevents further authority writes and snapshot publication.
- Failed/unknown requests are not automatically retried as new operations. In-memory receipts are not cross-crash durable exactly-once.
- Protocol and resource limits are mandatory; validate before allocation. No BinaryFormatter or peer-controlled reflection dispatch, file paths, privileges or compatibility classifications.
- net35 compilation remains required. Do not copy game/Steam/Harmony DLLs or credentials into the repository, replace game native libraries, or add automatic install steps.

## Workflow and verification

Use independent feature branches for parallel work. Fixed shared DTO/schema ownership prevents competing edits to the same registry. Package dependencies and runtime release gates are in the roadmap; do not equate parallel development with multithreaded world mutation.

Run:

```sh
python scripts/check_docs.py
python -m unittest discover -s tests/docs -p "test_*.py"
dotnet build tests/Forge.Tests/Forge.Tests.csproj -c Release --nologo
dotnet run --project tests/Forge.Tests/Forge.Tests.csproj -c Release -f net8.0 --no-build
```

On Linux also run the net35 executable on Mono. Inspect the exact commit's CI. Document E0/E1/E2/E3/E4 evidence separately; never substitute mocked boundaries, standard Mono or a probe for real-game acceptance. Add deterministic failure regressions before fixes. Re-read the target branch before publishing changes; never force-push over concurrent work.

Budget values in docs/spec/budgets.json are proposed v2 targets, not actual M0 Limits or measured performance. The documentation checker verifies structure, dependency and budget consistency, not protocol correctness or gameplay implementation.
