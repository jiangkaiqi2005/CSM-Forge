from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"
CORE = REPO / "src" / "Forge.Core"


class UltimateExtensionContractTests(unittest.TestCase):
    def test_extension_api_requires_absolute_state_and_stable_identity(self) -> None:
        source = (RUNTIME / "ForgeExtensionApi.cs").read_text(encoding="utf-8-sig")
        for value in [
            "IForgeStateAdapterV2", "IForgeInteractiveStateAdapterV1", "IForgeAdapterContextV1",
            "EntityIdentityV2", "GetOrAllocateIdentity", "BindKnownIdentity", "TrySubmitIntent",
        ]:
            self.assertIn(value, source)
        self.assertNotIn("CommandReceiver", source)
        self.assertNotIn("CommandReplay", source)

    def test_extension_domain_uses_authority_batch_path(self) -> None:
        source = (RUNTIME / "ExtensionStateDomain.cs").read_text(encoding="utf-8-sig")
        self.assertIn("IAuthorityDomainV2", source)
        self.assertIn("IReplicaDomainV2", source)
        self.assertIn("AuthorityOriginKind.Simulation", source)
        self.assertIn("PublishObserved", source)
        self.assertIn("ExtensionStateCodecV2.EncodeDelta", source)

    def test_named_identity_maps_are_persisted(self) -> None:
        source = (RUNTIME / "SaveMetadata.cs").read_text(encoding="utf-8-sig")
        self.assertIn("CSM-Forge.V3.ExtensionEntityMaps", source)
        self.assertIn("ExtensionIdentityServices.Maps", source)
        core = (CORE / "NamedEntityMapSaveV1.cs").read_text(encoding="utf-8-sig")
        self.assertIn("EntityIdentityV2", core)
        self.assertIn("HighestIssuedId", core)

    def test_districtpark_never_serializes_native_byte_as_identity(self) -> None:
        source = (RUNTIME / "BuiltInDlcAdapters.cs").read_text(encoding="utf-8-sig")
        for value in ["builtin.districtpark", "EntityIdentityV2", "GetOrAllocateIdentity", "BindKnownIdentity"]:
            self.assertIn(value, source)
        self.assertIn("CreatePark", source)
        self.assertIn("ReleasePark", source)


if __name__ == "__main__":
    unittest.main()
