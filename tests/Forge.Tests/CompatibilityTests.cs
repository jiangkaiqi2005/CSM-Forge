using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class CompatibilityTests
    {
        private static Hash256 Hash(byte value) { return Hash256.Compute(new byte[] { value }); }
        private static ComponentFingerprint Component(string id, byte binary, byte config)
        {
            return new ComponentFingerprint(id, Hash(binary), Hash(config));
        }
        private static CompatibilityPolicy Policy()
        {
            return new CompatibilityPolicy(Hash(1), Hash(2), new ComponentFingerprint[] { Component("forge:adapter", 3, 4) },
                new ComponentFingerprint[] { Component("local:camera", 5, 6) });
        }

        [Case] public static void RequiredExactFingerprintsAreAccepted()
        {
            Assert.Equal(0, Policy().Evaluate(new CompatibilityManifest(Hash(1), Hash(2),
                new ComponentFingerprint[] { Component("forge:adapter", 3, 4) })).Length);
        }

        [Case] public static void MissingRequirementsAreReported()
        {
            Assert.Equal("missing:forge:adapter", Policy().Evaluate(new CompatibilityManifest(Hash(1), Hash(2), new ComponentFingerprint[0]))[0]);
        }

        [Case] public static void BinaryAndConfigurationMismatchesAreNotCompatible()
        {
            Assert.Equal("fingerprint-mismatch:forge:adapter", Policy().Evaluate(new CompatibilityManifest(Hash(1), Hash(2),
                new ComponentFingerprint[] { Component("forge:adapter", 3, 99) }))[0]);
            Assert.Equal("fingerprint-mismatch:forge:adapter", Policy().Evaluate(new CompatibilityManifest(Hash(1), Hash(2),
                new ComponentFingerprint[] { Component("forge:adapter", 99, 4) }))[0]);
        }

        [Case] public static void UnknownExtrasCannotSelfDeclareClientOnly()
        {
            Assert.Equal("unsupported:unknown:simulation", Policy().Evaluate(new CompatibilityManifest(Hash(1), Hash(2),
                new ComponentFingerprint[] { Component("forge:adapter", 3, 4), Component("unknown:simulation", 5, 6) }))[0]);
        }

        [Case] public static void AuditedLocalOnlyDifferenceIsAllowedButDifferentBinaryIsNot()
        {
            Assert.Equal(0, Policy().Evaluate(new CompatibilityManifest(Hash(1), Hash(2),
                new ComponentFingerprint[] { Component("forge:adapter", 3, 4), Component("local:camera", 5, 6) })).Length);
            Assert.Equal("local-fingerprint-mismatch:local:camera", Policy().Evaluate(new CompatibilityManifest(Hash(1), Hash(2),
                new ComponentFingerprint[] { Component("forge:adapter", 3, 4), Component("local:camera", 99, 6) }))[0]);
        }

        [Case] public static void GameAndSchemaAreExactCompatibilityBoundaries()
        {
            string[] errors = Policy().Evaluate(new CompatibilityManifest(Hash(9), Hash(9),
                new ComponentFingerprint[] { Component("forge:adapter", 3, 4) }));
            Assert.Equal(2, errors.Length);
            Assert.Equal("game-build-mismatch", errors[0]);
            Assert.Equal("schema-mismatch", errors[1]);
        }

        [Case] public static void DuplicateIdsAndClassificationConflictsAreRejected()
        {
            ComponentFingerprint entry = Component("forge:adapter", 3, 4);
            Assert.Throws<ArgumentException>(delegate
            {
                new CompatibilityManifest(Hash(1), Hash(2), new ComponentFingerprint[] { entry, entry });
            });
            Assert.Throws<ArgumentException>(delegate
            {
                new CompatibilityPolicy(Hash(1), Hash(2), new ComponentFingerprint[] { entry }, new ComponentFingerprint[] { entry });
            });
            Assert.Throws<ArgumentException>(delegate { Component("Forge:Adapter", 3, 4); });
        }

        [Case] public static void ManifestOrderingIsCanonicalAndArrayCannotMutateIt()
        {
            CompatibilityManifest manifest = new CompatibilityManifest(Hash(1), Hash(2),
                new ComponentFingerprint[] { Component("z", 1, 1), Component("a", 1, 1) });
            ComponentFingerprint[] copy = manifest.Entries;
            Assert.Equal("a", copy[0].Id);
            copy[0] = null;
            Assert.Equal("a", manifest.Entries[0].Id);
        }
    }
}
