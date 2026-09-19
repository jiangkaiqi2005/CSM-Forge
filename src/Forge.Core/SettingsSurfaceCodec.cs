using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace CsmForge.Core
{
    /// <summary>A property that must sit at an exact value for multiplayer; violations share a label.</summary>
    public sealed class FixedValueRule
    {
        public string PropertyName { get; private set; }
        public long RequiredValue { get; private set; }
        public string Label { get; private set; }

        public FixedValueRule(string propertyName, long requiredValue, string label)
        {
            if (string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(label))
                throw new ArgumentException("Invalid fixed-value rule.");
            PropertyName = propertyName; RequiredValue = requiredValue; Label = label;
        }
    }

    /// <summary>
    /// WP-3.2: generic settings-surface codec shared by known-mod bridges. Owns the surface
    /// selection (readable+writable primitive properties minus local-only, canonical name order),
    /// the memoized wire capture (magic + schema fingerprint + count + values), the strict apply
    /// (schema/count/trailing checks), and the aggregated supported-configuration validation.
    /// surfaceName is embedded in every message so each bridge keeps its own identity.
    /// </summary>
    public sealed class SettingsSurfaceCodec
    {
        private const int MaximumProperties = 128;
        private const int MinimumStateBytes = 10; // magic(4) + fingerprint(4) + count(2)

        private readonly PropertyInfo[] properties;
        private readonly string[] blockedBooleanSettings;
        private readonly FixedValueRule[] fixedValueRules;
        private readonly uint magic;
        private readonly int maximumStateBytes;
        private readonly string surfaceName;

        public SettingsSurfaceCodec(PropertyInfo[] candidateProperties, string[] localOnlySettings,
            string[] blockedBooleanSettings, FixedValueRule[] fixedValueRules, uint magic,
            int maximumStateBytes, string surfaceName)
        {
            if (candidateProperties == null) throw new ArgumentNullException("candidateProperties");
            if (localOnlySettings == null || blockedBooleanSettings == null || fixedValueRules == null)
                throw new ArgumentNullException("surface policy");
            Check.Condition(string.IsNullOrEmpty(surfaceName), "surfaceName", "Invalid surface name.");
            if (maximumStateBytes < MinimumStateBytes) throw new ArgumentOutOfRangeException("maximumStateBytes");

            List<PropertyInfo> selected = new List<PropertyInfo>();
            List<string> selectedNames = new List<string>();
            foreach (PropertyInfo property in candidateProperties)
            {
                if (property == null || !property.CanRead || !property.CanWrite || !SupportedType(property.PropertyType)) continue;
                if (IsNamed(localOnlySettings, property.Name)) continue;
                if (selectedNames.Contains(property.Name))
                    throw new ArgumentException("Duplicate surface property name.", "candidateProperties");
                selected.Add(property); selectedNames.Add(property.Name);
            }
            if (selected.Count == 0 || selected.Count > MaximumProperties)
                throw new InvalidOperationException(surfaceName + " shared settings surface is invalid.");

            selected.Sort(delegate(PropertyInfo a, PropertyInfo b) { return StringComparer.Ordinal.Compare(a.Name, b.Name); });
            properties = selected.ToArray();
            this.blockedBooleanSettings = (string[])blockedBooleanSettings.Clone();
            this.fixedValueRules = (FixedValueRule[])fixedValueRules.Clone();
            this.magic = magic;
            this.maximumStateBytes = maximumStateBytes;
            this.surfaceName = surfaceName;
        }

        public int PropertyCount { get { return properties.Length; } }
        public string SurfaceName { get { return surfaceName; } }

        /// <summary>The canonical selected surface (sorted by name); bridges patch these setters.</summary>
        public PropertyInfo[] SharedProperties { get { return (PropertyInfo[])properties.Clone(); } }

        public bool HasProperty(string name)
        {
            if (name == null) return false;
            for (int i = 0; i < properties.Length; i++)
                if (properties[i].Name == name) return true;
            return false;
        }

        /// <summary>Order-independent fingerprint over the canonical surface (name:type pairs).</summary>
        public uint SchemaFingerprint
        {
            get
            {
                uint value = 2166136261u;
                for (int i = 0; i < properties.Length; i++)
                {
                    string text = properties[i].Name + ":" + properties[i].PropertyType.FullName + ";";
                    for (int p = 0; p < text.Length; p++) { value ^= text[p]; value *= 16777619u; }
                }
                return value;
            }
        }

        /// <summary>Aggregates every blocked-boolean/fixed-value violation into one message (S2).</summary>
        public void ValidateSupported(object instance)
        {
            long[] values = new long[properties.Length];
            for (int i = 0; i < properties.Length; i++)
                values[i] = ToBits(properties[i].PropertyType, properties[i].GetValue(instance, null));
            ValidateValues(values);
        }

        /// <summary>Captures the live instance; validates the supported configuration first.</summary>
        public byte[] Capture(object instance)
        {
            ValidateSupported(instance);
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(magic); writer.Write(SchemaFingerprint); writer.Write((ushort)properties.Length);
                for (int i = 0; i < properties.Length; i++)
                    WriteValue(writer, properties[i].PropertyType, properties[i].GetValue(instance, null));
                writer.Flush();
                if (stream.Length > maximumStateBytes)
                    throw new InvalidOperationException(surfaceName + " bridge state exceeded its fixed budget.");
                return stream.ToArray();
            }
        }

        /// <summary>Strict apply: schema/count/trailing checks, aggregated validation, then writes.</summary>
        public long[] ValidateState(byte[] state)
        {
            if (state == null || state.Length < MinimumStateBytes || state.Length > maximumStateBytes)
                throw new InvalidDataException("Invalid " + surfaceName + " bridge state.");
            long[] values = new long[properties.Length];
            using (MemoryStream stream = new MemoryStream(state, false))
            {
                BinaryReader reader = new BinaryReader(stream);
                if (reader.ReadUInt32() != magic || reader.ReadUInt32() != SchemaFingerprint)
                    throw new InvalidDataException(surfaceName + " bridge schema mismatch.");
                if (reader.ReadUInt16() != properties.Length)
                    throw new InvalidDataException(surfaceName + " bridge property count mismatch.");
                for (int i = 0; i < properties.Length; i++) values[i] = reader.ReadInt64();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing " + surfaceName + " bridge bytes.");
            }
            ValidateValues(values);
            return values;
        }

        public void WriteValues(object instance, long[] values)
        {
            if (values == null || values.Length != properties.Length)
                throw new InvalidDataException("Invalid " + surfaceName + " shared setting values.");
            for (int i = 0; i < properties.Length; i++)
                properties[i].SetValue(instance, FromBits(properties[i].PropertyType, values[i]), null);
        }

        private void ValidateValues(long[] values)
        {
            List<string> violations = new List<string>();
            foreach (string name in blockedBooleanSettings)
            {
                PropertyInfo property = Find(name);
                if (property == null) continue;
                int index = IndexOf(property);
                if (index < 0 || values[index] != 0L) violations.Add(name);
            }
            foreach (FixedValueRule rule in fixedValueRules)
            {
                int index = IndexOf(rule.PropertyName);
                if (index < 0) throw new InvalidOperationException(surfaceName + " bridge required property is unavailable: " + rule.PropertyName);
                if (values[index] != rule.RequiredValue) violations.Add(rule.Label);
            }
            if (violations.Count != 0)
                throw new InvalidOperationException(surfaceName + " options are not supported in Forge multiplayer: " +
                    string.Join(", ", violations.ToArray()));
        }

        private PropertyInfo Find(string name)
        {
            for (int i = 0; i < properties.Length; i++)
                if (properties[i].Name == name) return properties[i];
            return null;
        }

        private int IndexOf(PropertyInfo property)
        {
            for (int i = 0; i < properties.Length; i++)
                if (properties[i].Equals(property)) return i;
            return -1;
        }

        private int IndexOf(string name)
        {
            if (name == null) return -1;
            for (int i = 0; i < properties.Length; i++)
                if (properties[i].Name == name) return i;
            return -1;
        }

        private static bool IsNamed(string[] names, string name)
        {
            for (int i = 0; i < names.Length; i++)
                if (StringComparer.Ordinal.Equals(names[i], name)) return true;
            return false;
        }

        private static bool SupportedType(Type type)
        {
            return type.IsEnum || type == typeof(bool) || type == typeof(int) ||
                type == typeof(uint) || type == typeof(long) || type == typeof(float);
        }

        private static void WriteValue(BinaryWriter writer, Type type, object value)
        {
            writer.Write(ToBits(type, value));
        }

        private static long ToBits(Type type, object value)
        {
            if (type.IsEnum) return Convert.ToInt64(value);
            if (type == typeof(bool)) return (bool)value ? 1L : 0L;
            if (type == typeof(float)) return BitConverter.ToInt32(BitConverter.GetBytes((float)value), 0);
            if (type == typeof(uint)) return (long)(uint)value;
            if (type == typeof(long)) return (long)value;
            return Convert.ToInt64(value);
        }

        private static object FromBits(Type type, long bits)
        {
            if (type.IsEnum) return Enum.ToObject(type, bits);
            if (type == typeof(bool)) return bits != 0;
            if (type == typeof(float)) return BitConverter.ToSingle(BitConverter.GetBytes(unchecked((int)bits)), 0);
            if (type == typeof(uint)) return checked((uint)bits);
            if (type == typeof(long)) return bits;
            if (type == typeof(int)) return checked((int)bits);
            throw new InvalidOperationException("Unsupported settings type: " + type.FullName);
        }
    }
}
