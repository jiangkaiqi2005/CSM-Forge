from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
RUNTIME = ROOT / "src" / "Forge.Runtime.Cities1"


def test_terrain_adapter_is_absolute_sharded_and_registered():
    source = (RUNTIME / "TerrainStateAdapter.cs").read_text(encoding="utf-8-sig")
    registration = (RUNTIME / "ForgeMod.cs").read_text(encoding="utf-8-sig")
    for marker in [
        'Adapter = "builtin.terrain-heights"', "IForgeShardedStateAdapterV1",
        "RowsPerShard = 8", "TerrainManager.RAW_RESOLUTION + 1", "manager.RawHeights",
        "manager.DirtBuffer", "TerrainModify.UpdateArea", "Limits.FramePayloadBytes",
    ]:
        assert marker in source
    assert "new TerrainStateAdapter()" in registration


def test_terrain_wire_has_no_entity_or_tool_replay_identity():
    source = (RUNTIME / "TerrainStateAdapter.cs").read_text(encoding="utf-8-sig")
    for forbidden in ["EntityIdentityV2", "NativeId", "m_mousePosition", "m_brushSize", "m_strength", "Command"]:
        assert forbidden not in source


def test_terrain_audit_is_source_grounded_and_keeps_gameplay_unverified():
    audit = (ROOT / "docs" / "P8-TERRAIN-AUTHORITY-V1.zh-CN.md").read_text(encoding="utf-8-sig")
    for marker in [
        "RawHeights", "1081×1081", "FinalHeights", "TerrainModify.UpdateArea",
        "136 shards", "DirtBuffer", "不含任何 native Building/Net/Tree/Prop ID",
        "真实双机/多机未验证", "CI green 不能写成 gameplay validated", "不能据此宣称 1.0 RC",
    ]:
        assert marker in audit
