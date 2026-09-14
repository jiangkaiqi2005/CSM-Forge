# Implemented wire protocol: kernel v1.0

Status: reference-domain framing and typed Intent/Commit codecs are implemented. This document is **not** a claim that room bootstrap, authenticated sockets, snapshot wire orchestration or a game adapter exists.

## Frame

Unsigned integers use little-endian encoding. UUIDs use the RFC 4122 byte order, not the mixed-endian byte order returned by .NET Guid.ToByteArray. Header length is exactly 48 bytes.

| Offset | Bytes | Field |
| --- | ---: | --- |
| 0 | 4 | ASCII CSFG (`0x47465343` as a little-endian uint) |
| 4 | 2 | Major: 1 |
| 6 | 2 | Minor: 0 |
| 8 | 2 | Message kind |
| 10 | 2 | Reserved flags: must be zero |
| 12 | 16 | World UUID: must not be empty |
| 28 | 8 | Session Epoch: must be nonzero |
| 36 | 8 | Kind-specific sequence |
| 44 | 4 | Payload byte length, at most 65536 |
| 48 | variable | Payload |
| 48 + payload length | 32 | SHA-256 of header and payload |

Maximum total size is 65616 bytes. The decoder rejects undersize/oversize frames, version mismatches, unknown kinds/flags, invalid identities, inconsistent length, trailing bytes and digest failures. It checks bounds before payload-dependent allocations.

**SHA-256 is an unkeyed integrity digest, not authentication.** The transport must authenticate the actual connection and protect the channel. Neither a matching digest nor a UUID proves the sender is the host. Network parsing must never map an arbitrary payload field to the authenticated connection argument in HostSession or ReplicaSession.

## Typed messages

Intent kind 1 uses frame sequence as a nonzero RequestId. Its payload is `ExpectedRevision:u64` followed by 1..4096 bytes of domain intent. There is no user-controlled sender identity field. The reference domain encodes `slot:u16, absoluteValue:i32`; this is not a Cities command schema.

Commit kind 2 uses frame sequence as a nonzero global host Revision. Payload: origin connection UUID (16), original RequestId (8), before-state digest (32), after-state digest (32), and 1..4096 bytes of absolute domain result. Fixed prefix length is 88 bytes. Origin is attribution only; a client must accept the packet solely from its authenticated host connection.

Kinds 3 SnapshotChunk, 4 ReadyBarrier and 5 Heartbeat have allocated enum values and can be framed. Their typed payload codecs, authenticated handlers and network coordination are **not implemented yet**. A generic successful Decode must not imply that any such payload is safe to execute. Current typed decoders accept only their corresponding implemented kinds.

Bootstrap happens before a world Stamp is assigned and needs a separate fixed, tightly bounded authenticated negotiation. That bootstrap codec and manifest wire schema are not implemented. Do not turn a unknown message into a dynamically registered command to bypass negotiation.

## Sequence and acceptance rules

Request sequence is scoped to one freshly authenticated connection incarnation. Missing request IDs are rejected rather than reordered inside the authority. Processed successes, domain rejections and version conflicts receive bounded stable receipts. Requests older than the retained 128 receipts cannot execute again while that peer incarnation remains registered. NotReady/ReadOnly/unauthenticated inputs do not consume accepted request sequence.

Commit sequence is scoped to WorldId + Epoch. The client applies only Revision + 1 with a matching before-state root. A gap enters CatchingUp, requiring consecutive journal replay and a fresh matching Ready barrier. Recent conflicting duplicates require snapshot recovery. Older duplicates outside the digest-retention window are ignored without mutation, not claimed to be forensically verified.

Commit, transport packet, game tick, operation request and checkpoint revisions are separate concepts. There is no cross-crash exactly-once guarantee. Reconnect must allocate a new transport connection incarnation; a restore/new room must use a new Epoch.

## Reference state encoding and diagnostics fingerprints

ParameterWorld snapshot schema FPW1: four-byte magic `0x31575046`, unsigned 32-bit item count, then strictly increasing unsigned 16-bit keys and signed 32-bit values. There are at most 16 keys; keys are 1..16; values are -1000000..1000000. Absent and explicitly zero-valued slots are distinct canonical states.

Frame and state digests are stable encodings. `object.GetHashCode` and string.GetHashCode are never used as protocol/state checksums. Internal request/commit fingerprints are not separately serialized protocol fields. The current internal commit fingerprint uses .NET's documented Guid byte layout; cross-language diagnostic tools must reproduce that layout or use the canonical frame digest instead.

## Resource and recovery limits

The current laboratory limits are eight peer incarnations per host, 128 receipts per peer, 512 retained commits, maximum 4 KiB intent/result and a 64 MiB reference snapshot. SnapshotAssembler allows at most 8192 chunks, each at most 32 KiB, and takes exclusive ownership of a supplied temporary stream. It accepts no network-provided filename and performs no decompression.

A snapshot transfer is bound to World/Epoch/TransferId and an explicit baseline Revision. Chunk hashes detect corruption; duplicate chunks must be byte-identical; final publication requires complete receipt and the whole-content digest. World loading and semantic StateHash checks are separate. Cancellation releases storage and invalidates further use. A coordinator must authenticate the snapshot source and enforce per-peer byte/time quotas.

Changing these bounds or schemas is an explicit protocol evolution with tests, not a hidden permissive fallback. Real city checkpoints may require larger disk-backed streams and a different immutable checkpoint handle; the in-memory WorldImage is currently a bounded reference-domain API, not a final large-city save format.
