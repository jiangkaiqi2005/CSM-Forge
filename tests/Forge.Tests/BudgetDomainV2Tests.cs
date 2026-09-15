using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class BudgetDomainV2Tests
    {
        [Case]
        public static void BudgetRoundTripPreservesServiceSubserviceAndDaypart()
        {
            BudgetStateV2 value = new BudgetStateV2(new BudgetKeyV2(4, 7, true), 123);
            BudgetStateV2 copy = BudgetDomainCodecV2.Decode(BudgetDomainCodecV2.Encode(value));
            Assert.Equal(value.Key, copy.Key);
            Assert.Equal(123, copy.Budget);
        }

        [Case]
        public static void BudgetRootIsCanonicalAcrossInsertionOrder()
        {
            BudgetStateV2 a = new BudgetStateV2(new BudgetKeyV2(1, 0, false), 90);
            BudgetStateV2 b = new BudgetStateV2(new BudgetKeyV2(2, 4, true), 110);
            BudgetStateIndexV2 first = new BudgetStateIndexV2(); first.Upsert(a); first.Upsert(b);
            BudgetStateIndexV2 second = new BudgetStateIndexV2(); second.Upsert(b); second.Upsert(a);
            Assert.Equal(first.Root, second.Root);
        }

        [Case]
        public static void BudgetRejectsInvalidValueAndMalformedPayload()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new BudgetStateV2(new BudgetKeyV2(1, 0, false), 256); });
            byte[] bytes = BudgetDomainCodecV2.Encode(new BudgetStateV2(new BudgetKeyV2(1, 0, false), 100));
            byte[] tainted = new byte[bytes.Length + 1]; Buffer.BlockCopy(bytes, 0, tainted, 0, bytes.Length);
            Assert.Throws<InvalidDataException>(delegate { BudgetDomainCodecV2.Decode(tainted); });
        }
    }
}
