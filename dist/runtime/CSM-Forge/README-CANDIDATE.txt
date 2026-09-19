CSM-Forge 1.0 code-complete candidate

This package intentionally does NOT contain Cities: Skylines, Unity, Steam, or other game-owned assemblies.
It also does not bundle Harmony's implementation DLL; install/enable CitiesHarmony separately.
It currently uses the development LiteNetLib room-key transport. Use it for controlled LAN/development testing, not as a production-authenticated Internet transport.

Windows install target:
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\CSM-Forge

Copy the CONTENTS of this package's CSM-Forge folder so that CSM.Forge.Runtime.Cities1.dll is directly inside that directory. Enable CSM-Forge and CitiesHarmony in Content Manager, then restart the game.
Do not keep the original Cities: Skylines Multiplayer mod in a sibling Mods\CSM directory. The original CSM and CSM-Forge own competing multiplayer, Steam callback, networking and Harmony lifecycles and cannot coexist. VERIFY-INSTALL.ps1 rejects this layout.

Before testing on each machine:
1. Run VERIFY-INSTALL.ps1 from the installed CSM-Forge directory.
2. Confirm it reports PASS.
3. Confirm source_commit and manifest_sha256 are identical on every Host/Client machine.

Candidate scope:
- supported: host/join snapshot flow, roads/networks, buildings, zoning, districts and policies, tax/budgets/cash/loans, area unlock, pause/speed, transport lines, stable names/city name, demand/weather authority, and Tree/Prop create/move/delete through Forge Stable IDs;
- recovery: journal catch-up, fixed replay barrier, activation grant, snapshot rebaseline for one lagging client;
- diagnostic-only projection audit logs projection-drift/local drift without automatically kicking/resyncing a client;
- Tree/Prop and Terrain absolute height shards are code-complete for their authority batches but still require real multi-machine gameplay validation; Terrain remains one of the Host-only tools during multiplayer;
- still outside this framework milestone: real multi-machine Citizen/Vehicle/Path/Terrain validation, production-authenticated Internet transport, and broad real-world Mod/DLC soak certification.

Ultimate Framework code coverage in this candidate:
- official gameplay DLC are classified to core authority or dedicated absolute-state adapters; see docs/DLC-COVERAGE-V1.zh-CN.md in the repository;
- Parklife / Industries / Campus / Airports / pedestrian-area systems use stable DistrictPark identities, sharded park-grid state, controls, Campus deep state, and conservative deep value-state projection;
- Match Day / Concerts use Event Stable IDs, Building Stable-ID references, local Event slot materialization and Host absolute lifecycle/result projection;
- Natural Disasters use Disaster Stable IDs and local Disaster slot materialization; Client persistent disaster creation/release/random-start paths are fail-closed;
- third-party Mods can declare ExactMatch / ClientOnly / ForgeSynchronized / Blocked and can register absolute/sharded/interactive Forge adapters;
- ForgeSynchronized declarations without a state adapter fail closed; unknown simulation-changing Mods remain exact-match by default.

First two-machine test:
1. Back up the Host city and install the exact same ZIP + CitiesHarmony on both machines.
2. Run VERIFY-INSTALL.ps1 on both machines and compare source_commit + manifest_sha256.
3. Host enters the city to share, presses Esc, then chooses FORGE 澶氫汉鑱旀満 -> 鍒涘缓鎴块棿锛堝綋鍓嶅煄甯備綔涓烘埧涓伙級.
4. Host opens CSM-Forge multiplayer from the pause menu and chooses 澶嶅埗鐩磋繛閭€璇峰苟鎵撳紑 Steam. Forge copies the direct-connect invitation and opens the official Steam friends overlay; paste the invitation to the friend. Automatic Steam click-to-join is disabled because two real CS1 runs hit native access violations in the manually declared Steam ABI. This is direct UDP discovery only and does not provide NAT traversal or relay.
5. Client stays at the main menu, chooses FORGE 鑱旀満, pastes the invitation text, then chooses 鍔犲叆鎴块棿. Forge downloads and loads the Host snapshot automatically; the Client must not load a placeholder city first.
6. Wait until Client status is ClientLive before editing.
7. Test pause/speed, one road, one building, zoning, district brush/policy, tax/budget, area unlock and one transport line.
8. Then test installed DLC in small isolated steps: park/campus/industry/airport area edits, Event controls/results, and a Host-started disaster.
9. Test Host and Client Tree/Prop create, move and delete in small isolated steps; record any identity, slot-reuse or projection failure. These paths are not yet gameplay-validated by CI.
10. On the Host only, test a small Terrain brush and undo, then verify Terrain absolute height shards and Net/Building collateral on every Client. Client Terrain tools remain blocked. These paths are not yet gameplay-validated by CI.
11. Test a second client join/rejoin while the first client and Host remain live, then repeat a Tree/Prop edit after hot join.
12. Open 鐜╁鍒楄〃 and 澶氫汉鑱婂ぉ, press T to open chat, and confirm remote player tool cursors/names are visible during edits.
13. On any failure or projection warning, click 鍐欏叆璇婃柇鏃ュ織 in CSM-Forge settings before leaving the city, then run COLLECT-DIAGNOSTICS.ps1 on every involved machine.
14. Record every scenario in E3-E4-TEST-RECORD.md. Leave outcomes as NOT RUN until the named machine actually completes them.

A successful build proves compilation/package integrity and source-level authority contracts, not multi-hour gameplay acceptance. Player roster/chat/tool cursors and all two-machine behavior remain real-gameplay unverified until the E4/RC multiplayer gates are run. Automatic Steam click-to-join is not part of this package. BUILD_INFO.json intentionally says gameplay_validation=NOT RUN BY CI. Keep using backed-up saves until those gates pass.
