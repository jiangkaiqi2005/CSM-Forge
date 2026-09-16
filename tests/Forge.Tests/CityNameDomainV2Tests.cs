using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class CityNameDomainV2Tests
    {
        [Case]
        public static void CityNameRoundTripsUnicode()
        {
            CityNameStateV2 value = new CityNameStateV2("青萍州");
            CityNameStateV2 copy = CityNameCodecV2.Decode(CityNameCodecV2.Encode(value));
            Assert.Equal("青萍州", copy.Name);
            Assert.Equal(value.Root, copy.Root);
        }

        [Case]
        public static void CityNameAllowsEmptyButRejectsOversizedAndMalformedPayloads()
        {
            CityNameStateV2 empty = CityNameCodecV2.Decode(CityNameCodecV2.Encode(new CityNameStateV2(string.Empty)));
            Assert.Equal(string.Empty, empty.Name);
            Assert.Throws<ArgumentException>(delegate { new CityNameStateV2(new string('x', 513)); });
            Assert.Throws<InvalidDataException>(delegate { CityNameCodecV2.Decode(new byte[1]); });
        }

        [Case]
        public static void DifferentCityNamesProduceDifferentRoots()
        {
            Assert.True(!new CityNameStateV2("A").Root.Equals(new CityNameStateV2("B").Root));
        }
    }
}
