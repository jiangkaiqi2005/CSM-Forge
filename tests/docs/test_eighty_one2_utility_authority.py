import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
RUNTIME = ROOT / "src" / "Forge.Runtime.Cities1"


class EightyOne2UtilityAuthorityContractTests(unittest.TestCase):
    def test_exact_105_surface_is_required_before_registration(self):
        bridge = (RUNTIME / "EightyOne2Bridge.cs").read_text(encoding="utf-8-sig")
        for marker in [
            'ExpectedAssemblyName = "EightyOne2"', "new Version(1, 0, 5, 0)",
            "ValidateSurface", "ResolveCompatibleAssembly", "GetSetMethod(true)",
        ]:
            self.assertIn(marker, bridge)

    def test_expanded_utilities_are_host_owned_absolute_rows(self):
        source = (RUNTIME / "EightyOne2UtilityAdapters.cs").read_text(encoding="utf-8-sig")
        registry = (RUNTIME / "KnownModBridgeRegistry.cs").read_text(encoding="utf-8-sig")
        threading = (RUNTIME / "ForgeMod.cs").read_text(encoding="utf-8-sig")
        for marker in [
            "GridResolution = 462", "IForgeShardedStateAdapterV1",
            'Adapter = "bridge.eightyone2.electricity"',
            'Adapter = "bridge.eightyone2.water"',
            "ClientLoading", "ClientRecovering", "ClientReplicaLive",
            "MatchesElectricityStep", "MatchesWaterStep", "parameters.Length != 18",
            'AccessTools.Method(typeof(ElectricityManager), "SimulationStepImpl"',
            'AccessTools.Method(typeof(WaterManager), "SimulationStepImpl"',
        ]:
            self.assertIn(marker, source)
        self.assertIn("EightyOne2ElectricityGridAdapter", registry)
        self.assertIn("EightyOne2WaterGridAdapter", registry)
        self.assertIn("EightyOne2UtilityAuthority.RestoreClientProjection()", threading)

    def test_native_pipe_segment_cache_never_crosses_wire(self):
        source = (RUNTIME / "EightyOne2UtilityAdapters.cs").read_text(encoding="utf-8-sig")
        self.assertNotIn("writer.Write(cell.m_closestPipeSegment)", source)
        self.assertNotIn("writer.Write(cell.m_closestPipeSegment2)", source)
        self.assertNotIn("reader.ReadUInt16(); // native", source)
        self.assertIn("same stable Net identity in different slots", source)

    def test_audit_keeps_gameplay_and_hot_join_unverified(self):
        audit = (ROOT / "docs" / "81-TILES-2-AUDIT-V1.zh-CN.md").read_text(encoding="utf-8")
        for marker in [
            "04fa96241fab752b5d90a47a311246613e1af66d", "未验证", "hot join",
            "不能写成 gameplay validated", "m_closestPipeSegment", "EntityIdentityV2",
        ]:
            self.assertIn(marker, audit)


if __name__ == "__main__":
    unittest.main()
