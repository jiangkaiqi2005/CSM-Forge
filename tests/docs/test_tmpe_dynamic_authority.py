from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def test_tmpe_dynamic_audit_keeps_gameplay_and_compatibility_claims_honest():
    audit = (ROOT / "docs/TMPE-DYNAMIC-AUTHORITY-AUDIT-V1.zh-CN.md").read_text(encoding="utf-8")
    for marker in [
        "7d1360d39047a7fcee59743aabdc705b8fe29914",
        "CustomPathManager.CustomCreatePath",
        "Stable Segment identity + lane index/offset",
        "不包含 native VehicleId",
        "尚未 gameplay validated",
        "`blocked-mod`",
    ]:
        assert marker in audit
