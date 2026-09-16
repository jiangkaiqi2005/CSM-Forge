using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class EconomyControlDomainV2Tests
    {
        [Case]
        public static void LoanAndBailoutIntentsRoundTrip()
        {
            EconomyControlIntentV2 take = new EconomyControlIntentV2(
                EconomyControlIntentKindV2.TakeLoan, 1, 2000000, 5, 104);
            EconomyControlIntentV2 takeCopy = EconomyControlCodecV2.DecodeIntent(EconomyControlCodecV2.EncodeIntent(take));
            Assert.Equal(EconomyControlIntentKindV2.TakeLoan, takeCopy.Kind);
            Assert.Equal(1, takeCopy.Index);
            Assert.Equal(2000000, takeCopy.Amount);
            Assert.Equal(5, takeCopy.Interest);
            Assert.Equal(104, takeCopy.Length);

            EconomyControlIntentV2 pay = EconomyControlCodecV2.DecodeIntent(
                EconomyControlCodecV2.EncodeIntent(EconomyControlIntentV2.PayLoan(2)));
            Assert.Equal(EconomyControlIntentKindV2.PayLoan, pay.Kind);
            Assert.Equal(2, pay.Index);

            EconomyControlIntentV2 bailout = EconomyControlCodecV2.DecodeIntent(
                EconomyControlCodecV2.EncodeIntent(EconomyControlIntentV2.AcceptBailout()));
            Assert.Equal(EconomyControlIntentKindV2.AcceptBailout, bailout.Kind);
        }

        [Case]
        public static void AbsoluteControlStateOwnsBytesAndRoundTrips()
        {
            byte[] raw = new byte[] { 1, 2, 3, 4, 5, 6 };
            EconomyControlStateV2 state = new EconomyControlStateV2(raw);
            raw[0] = 99;
            Assert.Equal((byte)1, state.Snapshot[0]);
            EconomyControlStateV2 copy = EconomyControlCodecV2.DecodeState(EconomyControlCodecV2.EncodeState(state));
            Assert.Equal(state.Root, copy.Root);
            byte[] copyBytes = copy.Snapshot;
            Assert.Equal(6, copyBytes.Length);
            Assert.Equal((byte)6, copyBytes[5]);
        }

        [Case]
        public static void EconomyControlRejectsMalformedInputs()
        {
            Assert.Throws<ArgumentException>(delegate
            {
                new EconomyControlIntentV2(EconomyControlIntentKindV2.TakeLoan, 0, 0, 5, 10);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                EconomyControlIntentV2.PayLoan(-1);
            });
            Assert.Throws<InvalidDataException>(delegate { EconomyControlCodecV2.DecodeIntent(new byte[2]); });
            Assert.Throws<InvalidDataException>(delegate { EconomyControlCodecV2.DecodeState(new byte[2]); });
        }
    }
}
