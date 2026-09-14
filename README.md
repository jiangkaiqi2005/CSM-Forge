# CSM-Forge

A clean-room multiplayer system for **Cities: Skylines 1**. This is not a fork of CSM and has no protocol or source compatibility with it.

## Status

Repository initialization. Architecture research and the first executable, game-independent replication kernel are being established. **Not a playable multiplayer mod or a release.**

## Invariants

- The host is the only authority for persistent world changes; clients submit intents, not authoritative commands.
- A transport acknowledgement is not a successful world mutation.
- Failed or ambiguous application fences the affected replica; never silently continue a partially applied operation.
- Session incarnation, ordered commits, explicit snapshot baselines and bounded queues are protocol requirements.
- Game integration must prove simulation isolation and save/load safety before multiplayer is enabled.
- Game assemblies, saves, credentials and third-party proprietary binaries are never committed.

Implementation will live on `develop`; `main` is advanced only after available automated checks pass. Game-runtime acceptance is a separate requirement from those checks.
