using System;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Tests
{
    public static class ProtocolMessageV2Tests
    {
        [Case]
        public static void ManifestPagesAndWelcomeRoundTrip()
        {
            ComponentFingerprint[] entries = new ComponentFingerprint[]
            {
                new ComponentFingerprint("mod:1:test", Hash256.Compute(new byte[] { 1 }), Hash256.Compute(new byte[] { 2 })),
                new ComponentFingerprint("asset:2:tree", Hash256.Compute(new byte[] { 3 }), Hash256.Compute(new byte[] { 4 }))
            };
            CompatibilityManifest manifest = new CompatibilityManifest(Hash256.Compute(new byte[] { 8 }),
                Hash256.Compute(new byte[] { 9 }), entries);
            ManifestPageV2[] pages = BootstrapMessagesV2.CreateManifestPages(manifest);
            Assert.Equal(1, pages.Length);
            ManifestPageV2 decoded = BootstrapMessagesV2.DecodeManifestPage(BootstrapMessagesV2.EncodeManifestPage(pages[0]));
            Assert.Equal((ushort)0, decoded.Index);
            Assert.Equal((ushort)1, decoded.Total);
            Assert.Equal(2, decoded.Entries.Length);
            Assert.True(decoded.Entries[0].Matches(entries[0]));

            SessionWelcomeV2 welcome = new SessionWelcomeV2(new SessionStamp(Guid.NewGuid(), 7), Guid.NewGuid(),
                new MemberIdentity(Guid.NewGuid(), 2), 3, 12, Hash256.Compute(new byte[] { 6 }));
            SessionWelcomeV2 welcomeDecoded = BootstrapMessagesV2.DecodeWelcome(BootstrapMessagesV2.EncodeWelcome(welcome));
            Assert.Equal(welcome.Stamp, welcomeDecoded.Stamp);
            Assert.Equal(welcome.ConnectionBinding, welcomeDecoded.ConnectionBinding);
            Assert.Equal(welcome.Member, welcomeDecoded.Member);
            Assert.Equal(welcome.Root, welcomeDecoded.Root);
        }

        [Case]
        public static void HelloCarriesExplicitDevelopmentAuthenticationMode()
        {
            HelloV2 hello = new HelloV2(Guid.NewGuid(), "tester", Hash256.Compute(new byte[] { 1 }),
                Hash256.Compute(new byte[] { 2 }), 1, BootstrapAuthMode.DevelopmentRoomKeyOnly);
            HelloV2 decoded = BootstrapMessagesV2.DecodeHello(BootstrapMessagesV2.EncodeHello(hello));
            Assert.Equal(BootstrapAuthMode.DevelopmentRoomKeyOnly, decoded.AuthMode);
            Assert.Equal("tester", decoded.DisplayName);
            Assert.Equal((ushort)1, decoded.ManifestPageCount);
        }

        [Case]
        public static void PlayerIntentAndAuthorityBatchRoundTrip()
        {
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 5);
            MemberIdentity member = new MemberIdentity(Guid.NewGuid(), 1);
            PlayerIntentV2 intent = new PlayerIntentV2(stamp, member, 3, 4, 7,
                Hash256.Compute(new byte[] { 1 }), new byte[] { 10, 11 });
            PlayerIntentV2 decodedIntent = SessionMessagesV2.DecodeIntent(stamp, SessionMessagesV2.EncodeIntent(intent));
            Assert.Equal(intent.Member, decodedIntent.Member);
            Assert.Equal(intent.OperationCounter, decodedIntent.OperationCounter);
            Assert.Equal(intent.Fingerprint, decodedIntent.Fingerprint);

            AuthorityBatch batch = new AuthorityBatch(stamp, 8, AuthorityOriginKind.PlayerIntent,
                member.MemberId, member.Generation, 3, 7, Hash256.Compute(new byte[] { 1 }),
                Hash256.Compute(new byte[] { 2 }), new byte[] { 20, 21 });
            AuthorityBatch decodedBatch = SessionMessagesV2.DecodeBatch(stamp, SessionMessagesV2.EncodeBatch(batch));
            Assert.Equal(batch.Revision, decodedBatch.Revision);
            Assert.Equal(batch.OriginKind, decodedBatch.OriginKind);
            Assert.Equal(batch.Fingerprint, decodedBatch.Fingerprint);
        }

        [Case]
        public static void AppliedAckRoundTripsWithBindingFromTrustedFrameContext()
        {
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 2);
            Guid binding = Guid.NewGuid();
            AppliedAck ack = new AppliedAck(stamp, binding, 10, Hash256.Compute(new byte[] { 9 }), 2);
            AppliedAck decoded = SessionMessagesV2.DecodeAppliedAck(stamp, binding, SessionMessagesV2.EncodeAppliedAck(ack));
            Assert.Equal(binding, decoded.ConnectionBinding);
            Assert.Equal((ulong)10, decoded.Revision);
            Assert.Equal(2, decoded.PendingBatches);
        }
    }
}
