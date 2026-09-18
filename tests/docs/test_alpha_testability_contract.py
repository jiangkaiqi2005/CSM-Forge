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

    def test_alpha_package_contains_verifier_and_collector(self):
        build = (ROOT / "scripts/build-runtime.ps1").read_text(encoding="utf-8")
        self.assertIn("VERIFY-ALPHA-INSTALL.ps1", build)
        self.assertIn("COLLECT-ALPHA-DIAGNOSTICS.ps1", build)
        self.assertIn("README-DEV.txt", build)
        self.assertLess(build.index("README-DEV.txt"), build.rindex("SHA256SUMS.txt"))

    def test_runtime_package_runs_real_startup_probe(self):
        build = (ROOT / "scripts/build-runtime.ps1").read_text(encoding="utf-8")
        probe = (ROOT / "tools/Forge.RuntimeStartupProbe/Program.cs").read_text(encoding="utf-8")
        self.assertIn("Forge.RuntimeStartupProbe.csproj", build)
        self.assertIn("RuntimeHelpers.RunClassConstructor", probe)
        self.assertIn("DistrictParkDeepScalarAdapter", probe)

    def test_host_join_controls_use_task_oriented_labels(self):
        panel = (ROOT / "src/Forge.Runtime.Cities1/ForgeSettingsPanel.cs").read_text(encoding="utf-8")
        self.assertIn("创建房间（本机作为房主）", panel)
        self.assertIn("加入房间并加载房主快照", panel)
        self.assertIn("Esc → 选项 → CSM-Forge", panel)
        mod = (ROOT / "src/Forge.Runtime.Cities1/ForgeMod.cs").read_text(encoding="utf-8")
        self.assertIn("Esc → 选项 → CSM-Forge", mod)

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
