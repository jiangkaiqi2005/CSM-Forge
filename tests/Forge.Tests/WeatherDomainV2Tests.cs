using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class WeatherDomainV2Tests
    {
        [Case]
        public static void WeatherStateRoundTripsAllCurrentAndTargetValues()
        {
            WeatherStateV2 value = new WeatherStateV2(
                0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f,
                0.7f, 0.8f, 0.9f, 1.0f, -0.25f, 0.75f);
            byte[] encoded = WeatherDomainCodecV2.Encode(value);
            Assert.Equal(WeatherDomainCodecV2.EncodedBytes, encoded.Length);
            WeatherStateV2 copy = WeatherDomainCodecV2.Decode(encoded);
            Assert.Equal(value.CurrentCloud, copy.CurrentCloud);
            Assert.Equal(value.TargetCloud, copy.TargetCloud);
            Assert.Equal(value.CurrentFog, copy.CurrentFog);
            Assert.Equal(value.TargetFog, copy.TargetFog);
            Assert.Equal(value.CurrentNorthernLights, copy.CurrentNorthernLights);
            Assert.Equal(value.TargetNorthernLights, copy.TargetNorthernLights);
            Assert.Equal(value.CurrentRain, copy.CurrentRain);
            Assert.Equal(value.TargetRain, copy.TargetRain);
            Assert.Equal(value.CurrentRainbow, copy.CurrentRainbow);
            Assert.Equal(value.TargetRainbow, copy.TargetRainbow);
            Assert.Equal(value.CurrentTemperature, copy.CurrentTemperature);
            Assert.Equal(value.TargetTemperature, copy.TargetTemperature);
        }

        [Case]
        public static void WeatherAuthorityRootTracksTargetsNotLocalInterpolation()
        {
            WeatherStateV2 first = new WeatherStateV2(
                0.1f, 0.7f, 0.2f, 0.6f, 0.3f, 0.5f,
                0.4f, 0.4f, 0.5f, 0.3f, 0.6f, 0.2f);
            WeatherStateV2 interpolated = new WeatherStateV2(
                0.65f, 0.7f, 0.55f, 0.6f, 0.45f, 0.5f,
                0.35f, 0.4f, 0.25f, 0.3f, 0.15f, 0.2f);
            Assert.Equal(first.TargetRoot, interpolated.TargetRoot);

            WeatherStateV2 newTarget = new WeatherStateV2(
                0.65f, 0.8f, 0.55f, 0.6f, 0.45f, 0.5f,
                0.35f, 0.4f, 0.25f, 0.3f, 0.15f, 0.2f);
            Assert.False(first.TargetRoot.Equals(newTarget.TargetRoot));
        }

        [Case]
        public static void WeatherPayloadFailsClosed()
        {
            Assert.Throws<ArgumentException>(delegate
            {
                new WeatherStateV2(float.NaN, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            });
            Assert.Throws<InvalidDataException>(delegate
            {
                WeatherDomainCodecV2.Decode(new byte[WeatherDomainCodecV2.EncodedBytes - 1]);
            });
        }
    }
}
