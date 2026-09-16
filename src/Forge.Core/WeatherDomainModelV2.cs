using System;
using System.IO;

namespace CsmForge.Core
{
    public sealed class WeatherStateV2
    {
        public float CurrentCloud { get; private set; }
        public float TargetCloud { get; private set; }
        public float CurrentFog { get; private set; }
        public float TargetFog { get; private set; }
        public float CurrentNorthernLights { get; private set; }
        public float TargetNorthernLights { get; private set; }
        public float CurrentRain { get; private set; }
        public float TargetRain { get; private set; }
        public float CurrentRainbow { get; private set; }
        public float TargetRainbow { get; private set; }
        public float CurrentTemperature { get; private set; }
        public float TargetTemperature { get; private set; }

        public WeatherStateV2(float currentCloud, float targetCloud, float currentFog, float targetFog,
            float currentNorthernLights, float targetNorthernLights, float currentRain, float targetRain,
            float currentRainbow, float targetRainbow, float currentTemperature, float targetTemperature)
        {
            CheckFinite(currentCloud); CheckFinite(targetCloud); CheckFinite(currentFog); CheckFinite(targetFog);
            CheckFinite(currentNorthernLights); CheckFinite(targetNorthernLights); CheckFinite(currentRain); CheckFinite(targetRain);
            CheckFinite(currentRainbow); CheckFinite(targetRainbow); CheckFinite(currentTemperature); CheckFinite(targetTemperature);
            CurrentCloud = currentCloud; TargetCloud = targetCloud;
            CurrentFog = currentFog; TargetFog = targetFog;
            CurrentNorthernLights = currentNorthernLights; TargetNorthernLights = targetNorthernLights;
            CurrentRain = currentRain; TargetRain = targetRain;
            CurrentRainbow = currentRainbow; TargetRainbow = targetRainbow;
            CurrentTemperature = currentTemperature; TargetTemperature = targetTemperature;
        }

        public Hash256 TargetRoot { get { return WeatherDomainCodecV2.TargetRoot(this); } }

        private static void CheckFinite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("Weather values must be finite.");
        }
    }

    public static class WeatherDomainCodecV2
    {
        private const uint FullMagic = 0x32545746u; // FWT2
        private const uint RootMagic = 0x32525746u; // FWR2
        public const int EncodedBytes = 52;

        public static byte[] Encode(WeatherStateV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(FullMagic);
                writer.Write(value.CurrentCloud); writer.Write(value.TargetCloud);
                writer.Write(value.CurrentFog); writer.Write(value.TargetFog);
                writer.Write(value.CurrentNorthernLights); writer.Write(value.TargetNorthernLights);
                writer.Write(value.CurrentRain); writer.Write(value.TargetRain);
                writer.Write(value.CurrentRainbow); writer.Write(value.TargetRainbow);
                writer.Write(value.CurrentTemperature); writer.Write(value.TargetTemperature);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static WeatherStateV2 Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length != EncodedBytes)
                throw new InvalidDataException("Invalid weather payload length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != FullMagic) throw new InvalidDataException("Unknown weather payload magic.");
                WeatherStateV2 value = new WeatherStateV2(
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                if (reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException("Unexpected trailing weather bytes.");
                return value;
            }
        }

        public static Hash256 TargetRoot(WeatherStateV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(RootMagic);
                writer.Write(value.TargetCloud);
                writer.Write(value.TargetFog);
                writer.Write(value.TargetNorthernLights);
                writer.Write(value.TargetRain);
                writer.Write(value.TargetRainbow);
                writer.Write(value.TargetTemperature);
                writer.Flush(); return Hash256.Compute(stream.ToArray());
            }
        }
    }
}
