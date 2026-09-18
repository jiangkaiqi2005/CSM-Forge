from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]


class AlphaTestabilityContractTests(unittest.TestCase):
    def test_runtime_exposes_manual_diagnostic_dump(self):
        diagnostics = (ROOT / "src/Forge.Runtime.Cities1/RuntimeDiagnostics.cs").read_text(encoding="utf-8")
        panel = (ROOT / "src/Forge.Runtime.Cities1/ForgeSettingsPanel.cs").read_text(encoding="utf-8")
        self.assertIn("diagnostic dump begin", diagnostics)
        self.assertIn("RuntimeServices.Events.Read()", diagnostics)
        self.assertIn("写入诊断日志", panel)
        self.assertIn("RuntimeDiagnostics.DumpToGameLog", panel)

    def test_candidate_package_contains_verifier_collector_and_evidence_record(self):
        build = (ROOT / "scripts/build-runtime.ps1").read_text(encoding="utf-8")
        self.assertIn("VERIFY-INSTALL.ps1", build)
        self.assertIn("COLLECT-DIAGNOSTICS.ps1", build)
        self.assertIn("E3-E4-TEST-RECORD.md", build)
        self.assertIn("README-CANDIDATE.txt", build)
        self.assertLess(build.index("README-CANDIDATE.txt"), build.rindex("SHA256SUMS.txt"))

    def test_install_verifier_rejects_original_csm_coexistence(self):
        verifier = (ROOT / "scripts/verify-alpha-install.ps1").read_text(encoding="utf-8")
        self.assertIn("CSM.dll", verifier)
        self.assertIn("original CSM", verifier)
        self.assertIn("cannot coexist", verifier)
        self.assertIn("Split-Path -Parent $rootPath", verifier)

    def test_runtime_package_runs_real_startup_probe(self):
        build = (ROOT / "scripts/build-runtime.ps1").read_text(encoding="utf-8")
        probe = (ROOT / "tools/Forge.RuntimeStartupProbe/Program.cs").read_text(encoding="utf-8")
        self.assertIn("Forge.RuntimeStartupProbe.csproj", build)
        self.assertIn("RuntimeHelpers.RunClassConstructor", probe)
        self.assertIn("DistrictParkDeepScalarAdapter", probe)

    def test_host_join_controls_use_task_oriented_labels(self):
        panel = (ROOT / "src/Forge.Runtime.Cities1/ForgeSettingsPanel.cs").read_text(encoding="utf-8")
        multiplayer = (ROOT / "src/Forge.Runtime.Cities1/ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        self.assertIn("创建房间（当前城市作为房主）", multiplayer)
        self.assertIn("加入房间", multiplayer)
        self.assertIn("主菜单", panel)
        self.assertIn("暂停菜单", panel)
        mod = (ROOT / "src/Forge.Runtime.Cities1/ForgeMod.cs").read_text(encoding="utf-8")
        self.assertIn("CSM-Forge 1.0 Candidate", mod)
        self.assertIn("主菜单加入房间", mod)

    def test_e3_e4_record_starts_unverified_and_keeps_tmpe_blocked(self):
        record = (ROOT / "docs/E3-E4-TEST-RECORD-TEMPLATE.zh-CN.md").read_text(encoding="utf-8")
        self.assertIn("默认状态全部是 `NOT RUN`", record)
        self.assertIn("CI green 不能替代", record)
        self.assertIn("TM:PE 必须保持未启用和 `blocked-mod`", record)
        self.assertIn("1 Host + 2 Clients", record)
        self.assertIn("24 小时 RC soak", record)

    def test_ci_finalizes_manifest_after_build_info(self):
        for relative in (".github/workflows/ci.yml", ".github/workflows/package-runtime.yml"):
            text = (ROOT / relative).read_text(encoding="utf-8")
            info = text.index("BUILD_INFO.json")
            manifest = text.index("SHA256SUMS.txt", info)
            archive = text.index("Compress-Archive", manifest)
            self.assertLess(info, manifest)
            self.assertLess(manifest, archive)

    def test_push_package_reacts_to_all_packaged_scripts(self):
        workflow = (ROOT / ".github/workflows/package-runtime.yml").read_text(encoding="utf-8")
        self.assertIn("'scripts/**'", workflow)


if __name__ == "__main__":
    unittest.main()
