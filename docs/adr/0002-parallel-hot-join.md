# ADR-0002: Independent parallel joins with retained historical barriers

Status: accepted design; not implemented. Baseline: 225fd25151fb6ec0a8dae3e95ca2945fb4270462.

## Context

The M0 HostSession.CompleteJoin compares the acknowledgement revision with the current host head. With a continuously changing world, a correct acknowledgement can become stale in transit. A global joining flag or waiting for all loading clients would additionally couple independent joins and stall existing players.

## Decision

Create per-member JoinContext identities/generations and per-transfer cursors. Share immutable checkpoints through reference-counted, bounded leases. Atomically acquire a checkpoint baseline and retain the following commit range. Replay and live delivery use one ordered state stream per connection with no subscription gap.

Validate BarrierAck against the saved root of a fixed historical revision H, not the current head. Issue a separate generation-bound ActivationGrant at fixed state boundary A on that same stream. The client activates only after applying and verifying A; the host enables edits after Activated on the ordered client control stream. Read-set/object versions still validate each later intent. Enforce explicit lag, lease and resource bounds.

One native checkpoint producer may serialize work on the host without serializing client download/load/catch-up. Snapshot acquisition may require a measured local safe-point pause; waiting for remote participants is never part of that pause. Slow or cancelled joiners lose only their own lease and permissions.

## Consequences

Historical roots and log ranges require bounded retention. Continuous joins cannot be guaranteed for a client whose application/bandwidth capacity is below world generation rate. Such clients must receive a bounded recovery/rejection outcome, not pin logs indefinitely.

The change requires explicit protocol v2 types and coordinator work; it is not safe to simply remove the existing equality check. Snapshot/world completeness and game simulation isolation remain integration gates.

## Supersession

This refines ADR-0001, replaces the current-head Ready design as the target, and makes real-time parallel joining a product requirement. Paused editing remains an internal proof slice, not the final multiplayer MVP. Implementation order and evidence requirements are in the current roadmap.

See [parallel-join specification](../spec/PARALLEL-JOIN.zh-CN.md) and [technical master](../TECHNICAL-SPEC.zh-CN.md).
