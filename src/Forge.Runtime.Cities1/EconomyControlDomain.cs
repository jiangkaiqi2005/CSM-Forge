using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal static class EconomyControlGameAccess
    {
        private const uint SnapshotMagic = 0x324C4345u; // ECL2
        private const ushort SnapshotSchema = 1;
        private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo LoansField = typeof(EconomyManager).GetField("m_loans", Fields);
        private static readonly FieldInfo BailoutAcceptedField = typeof(EconomyManager).GetField("m_bailoutAccepted", Fields);
        private static readonly FieldInfo BailoutShowingField = typeof(EconomyManager).GetField("m_bailoutShowing", Fields);
        private static readonly FieldInfo[] LoanFields = BuildLoanFields();

        private static FieldInfo[] BuildLoanFields()
        {
            FieldInfo[] values = typeof(EconomyManager.Loan).GetFields(Fields);
            Array.Sort(values, delegate(FieldInfo a, FieldInfo b) { return StringComparer.Ordinal.Compare(a.Name, b.Name); });
            if (values.Length == 0 || values.Length > 32)
                throw new InvalidOperationException("EconomyManager.Loan field layout is unsupported.");
            for (int i = 0; i < values.Length; i++) ValidatePrimitive(values[i].FieldType);
            return values;
        }

        private static EconomyManager RequireManager()
        {
            EconomyManager manager = EconomyManager.instance;
            if (manager == null) throw new InvalidOperationException("EconomyManager is unavailable.");
            if (LoansField == null || LoansField.FieldType != typeof(EconomyManager.Loan[]) ||
                BailoutAcceptedField == null || BailoutShowingField == null)
                throw new MissingFieldException("EconomyManager loan or bailout state surface is unavailable.");
            ValidatePrimitive(BailoutAcceptedField.FieldType);
            ValidatePrimitive(BailoutShowingField.FieldType);
            return manager;
        }

        public static EconomyControlStateV2 Capture()
        {
            EconomyManager manager = RequireManager();
            EconomyManager.Loan[] loans = (EconomyManager.Loan[])LoansField.GetValue(manager);
            if (loans == null || loans.Length > 32) throw new InvalidOperationException("Economy loan array is unavailable or oversized.");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(SnapshotMagic); writer.Write(SnapshotSchema);
                WriteDescriptorAndValue(writer, BailoutAcceptedField, manager);
                WriteDescriptorAndValue(writer, BailoutShowingField, manager);
                writer.Write((byte)LoanFields.Length);
                for (int i = 0; i < LoanFields.Length; i++) WriteDescriptor(writer, LoanFields[i]);
                writer.Write((byte)loans.Length);
                for (int i = 0; i < loans.Length; i++)
                {
                    object boxed = loans[i];
                    for (int f = 0; f < LoanFields.Length; f++) WritePrimitive(writer, LoanFields[f].FieldType, LoanFields[f].GetValue(boxed));
                }
                writer.Flush();
                if (stream.Length > 32768) throw new InvalidOperationException("Economy control snapshot exceeds Forge bounds.");
                return new EconomyControlStateV2(stream.ToArray());
            }
        }

        public static EconomyControlStateV2 Install(LoadIdentity load, EconomyControlStateV2 state)
        {
            Check.NotNull(state, "state");
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Economy control apply belongs to a stale load.");
            EconomyManager manager = RequireManager();
            byte[] bytes = state.Snapshot;
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            using (RuntimeScopeGuard.EnterApply(load, EconomyControlAuthorityDomain.Id))
            {
                if (reader.ReadUInt32() != SnapshotMagic || reader.ReadUInt16() != SnapshotSchema)
                    throw new InvalidDataException("Unsupported economy control snapshot.");
                ReadDescriptorAndInstall(reader, BailoutAcceptedField, manager);
                ReadDescriptorAndInstall(reader, BailoutShowingField, manager);
                byte fieldCount = reader.ReadByte();
                if (fieldCount != LoanFields.Length) throw new InvalidDataException("Economy loan schema field count mismatch.");
                for (int f = 0; f < LoanFields.Length; f++) VerifyDescriptor(reader, LoanFields[f]);
                byte loanCount = reader.ReadByte();
                EconomyManager.Loan[] current = (EconomyManager.Loan[])LoansField.GetValue(manager);
                if (current == null || current.Length != loanCount)
                    throw new InvalidDataException("Economy loan slot count mismatch.");
                for (int i = 0; i < current.Length; i++)
                {
                    object boxed = current[i];
                    for (int f = 0; f < LoanFields.Length; f++)
                        LoanFields[f].SetValue(boxed, ReadPrimitive(reader, LoanFields[f].FieldType));
                    current[i] = (EconomyManager.Loan)boxed;
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException("Unexpected trailing economy control bytes.");
                LoansField.SetValue(manager, current);
            }
            EconomyControlStateV2 actual = Capture();
            if (!actual.Root.Equals(state.Root)) throw new InvalidOperationException("Economy control projection root mismatch.");
            RefreshLoansPanel();
            return actual;
        }

        public static EconomyControlStateV2 Execute(LoadIdentity load, EconomyControlIntentV2 intent)
        {
            Check.NotNull(intent, "intent");
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Economy control intent belongs to a stale load.");
            EconomyManager manager = RequireManager();
            EconomyManager.Loan[] loans = (EconomyManager.Loan[])LoansField.GetValue(manager);
            if ((intent.Kind == EconomyControlIntentKindV2.TakeLoan || intent.Kind == EconomyControlIntentKindV2.PayLoan) &&
                (intent.Index < 0 || intent.Index >= loans.Length))
                throw new ArgumentOutOfRangeException("intent.Index");
            using (RuntimeScopeGuard.EnterApply(load, EconomyControlAuthorityDomain.Id))
            {
                if (intent.Kind == EconomyControlIntentKindV2.TakeLoan)
                {
                    IEnumerator action = manager.TakeNewLoan(intent.Index, intent.Amount, intent.Interest, intent.Length);
                    if (action != null) action.MoveNext();
                }
                else if (intent.Kind == EconomyControlIntentKindV2.PayLoan)
                {
                    IEnumerator action = manager.PayLoanNow(intent.Index);
                    if (action != null) action.MoveNext();
                }
                else if (intent.Kind == EconomyControlIntentKindV2.AcceptBailout) manager.AcceptBailout();
                else if (intent.Kind == EconomyControlIntentKindV2.RejectBailout) manager.RejectBailout();
                else throw new InvalidOperationException("Unknown economy control intent.");
            }
            RefreshLoansPanel();
            return Capture();
        }

        private static void RefreshLoansPanel()
        {
            try
            {
                FieldInfo panelField = typeof(ToolsModifierControl).GetField("m_EconomyPanel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object panel = panelField == null ? null : panelField.GetValue(null);
                if (panel == null) return;
                MethodInfo update = panel.GetType().GetMethod("UpdateLoansTab", Fields);
                if (update == null) return;
                SimulationManager.instance.m_ThreadingWrapper.QueueMainThread(delegate { update.Invoke(panel, null); });
            }
            catch { }
        }

        private static void WriteDescriptorAndValue(BinaryWriter writer, FieldInfo field, object owner)
        {
            WriteDescriptor(writer, field);
            WritePrimitive(writer, field.FieldType, field.GetValue(owner));
        }

        private static void ReadDescriptorAndInstall(BinaryReader reader, FieldInfo field, object owner)
        {
            VerifyDescriptor(reader, field);
            field.SetValue(owner, ReadPrimitive(reader, field.FieldType));
        }

        private static void WriteDescriptor(BinaryWriter writer, FieldInfo field)
        {
            WriteText(writer, field.Name);
            WriteText(writer, field.FieldType.FullName ?? field.FieldType.Name);
        }

        private static void VerifyDescriptor(BinaryReader reader, FieldInfo field)
        {
            string name = ReadText(reader);
            string type = ReadText(reader);
            if (!StringComparer.Ordinal.Equals(name, field.Name) ||
                !StringComparer.Ordinal.Equals(type, field.FieldType.FullName ?? field.FieldType.Name))
                throw new InvalidDataException("Economy control reflection schema mismatch.");
        }

        private static void WriteText(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (bytes.Length == 0 || bytes.Length > 255) throw new InvalidDataException("Economy control schema text is outside bounds.");
            writer.Write((byte)bytes.Length); writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader)
        {
            int length = reader.ReadByte();
            if (length <= 0) throw new InvalidDataException("Economy control schema text is empty.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new InvalidDataException("Truncated economy control schema text.");
            return Encoding.UTF8.GetString(bytes);
        }

        private static void ValidatePrimitive(Type type)
        {
            Type actual = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
            TypeCode code = Type.GetTypeCode(actual);
            switch (code)
            {
                case TypeCode.Boolean:
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                    return;
                default:
                    throw new InvalidOperationException("Unsupported economy control primitive field: " + type.FullName);
            }
        }

        private static void WritePrimitive(BinaryWriter writer, Type type, object value)
        {
            Type actual = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
            switch (Type.GetTypeCode(actual))
            {
                case TypeCode.Boolean: writer.Write(Convert.ToBoolean(value)); break;
                case TypeCode.Byte: writer.Write(Convert.ToByte(value)); break;
                case TypeCode.SByte: writer.Write(Convert.ToSByte(value)); break;
                case TypeCode.Int16: writer.Write(Convert.ToInt16(value)); break;
                case TypeCode.UInt16: writer.Write(Convert.ToUInt16(value)); break;
                case TypeCode.Int32: writer.Write(Convert.ToInt32(value)); break;
                case TypeCode.UInt32: writer.Write(Convert.ToUInt32(value)); break;
                case TypeCode.Int64: writer.Write(Convert.ToInt64(value)); break;
                case TypeCode.UInt64: writer.Write(Convert.ToUInt64(value)); break;
                case TypeCode.Single: writer.Write(Convert.ToSingle(value)); break;
                case TypeCode.Double: writer.Write(Convert.ToDouble(value)); break;
                default: throw new InvalidOperationException("Unsupported primitive type.");
            }
        }

        private static object ReadPrimitive(BinaryReader reader, Type type)
        {
            Type actual = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
            object value;
            switch (Type.GetTypeCode(actual))
            {
                case TypeCode.Boolean: value = reader.ReadBoolean(); break;
                case TypeCode.Byte: value = reader.ReadByte(); break;
                case TypeCode.SByte: value = reader.ReadSByte(); break;
                case TypeCode.Int16: value = reader.ReadInt16(); break;
                case TypeCode.UInt16: value = reader.ReadUInt16(); break;
                case TypeCode.Int32: value = reader.ReadInt32(); break;
                case TypeCode.UInt32: value = reader.ReadUInt32(); break;
                case TypeCode.Int64: value = reader.ReadInt64(); break;
                case TypeCode.UInt64: value = reader.ReadUInt64(); break;
                case TypeCode.Single: value = reader.ReadSingle(); break;
                case TypeCode.Double: value = reader.ReadDouble(); break;
                default: throw new InvalidOperationException("Unsupported primitive type.");
            }
            return type.IsEnum ? Enum.ToObject(type, value) : value;
        }
    }

    public sealed class EconomyControlAuthorityDomain : IAuthorityDomainV2
    {
        public const ushort Id = 6;
        private readonly LoadIdentity load;
        private Hash256 committedRoot;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return EconomyControlGameAccess.Capture().Root; } }
        internal Hash256 CommittedRoot { get { return committedRoot; } }

        public EconomyControlAuthorityDomain(LoadIdentity load)
        {
            Check.Condition(!load.IsValid, "load", "Invalid load identity.");
            this.load = load;
            committedRoot = EconomyControlGameAccess.Capture().Root;
        }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return DomainExecutionV2.Rejected();
            EconomyControlIntentV2 intent;
            try { intent = EconomyControlCodecV2.DecodeIntent(payload); }
            catch { return DomainExecutionV2.Rejected(); }
            Hash256 before = StateRoot;
            EconomyControlStateV2 actual;
            try { actual = EconomyControlGameAccess.Execute(load, intent); }
            catch { return DomainExecutionV2.Rejected(); }
            if (actual.Root.Equals(before)) return DomainExecutionV2.Rejected();
            committedRoot = actual.Root;
            return DomainExecutionV2.Success(EconomyControlCodecV2.EncodeState(actual), actual.Root);
        }

        internal void MarkObservedCommitted(Hash256 root)
        {
            Check.NotNull(root, "root");
            committedRoot = root;
        }
    }

    public sealed class EconomyControlReplicaDomain : IReplicaDomainV2
    {
        private readonly LoadIdentity load;
        private EconomyControlStateV2 committed;
        public ushort DomainId { get { return EconomyControlAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return committed.Root; } }

        public EconomyControlReplicaDomain(LoadIdentity load)
        {
            Check.Condition(!load.IsValid, "load", "Invalid load identity.");
            this.load = load;
            committed = EconomyControlGameAccess.Capture();
        }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            Check.NotNull(expectedAfterRoot, "expectedAfterRoot");
            EconomyControlStateV2 requested = EconomyControlCodecV2.DecodeState(absoluteDelta);
            EconomyControlStateV2 actual = EconomyControlGameAccess.Install(load, requested);
            committed = actual;
            if (!StateRoot.Equals(expectedAfterRoot))
                throw new InvalidOperationException("Economy control replica root mismatch.");
        }
    }
}
