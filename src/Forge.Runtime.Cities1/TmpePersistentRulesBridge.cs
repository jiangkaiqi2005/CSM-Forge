using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Exact-version TM:PE persistent-rule projection. Native node, segment and lane slots never
    /// enter the payload: lanes are represented by Stable Segment identity plus prefab lane index.
    /// TM:PE remains compatibility-blocked until the separate Vehicle/Path closure is installed.
    /// </summary>
    internal sealed class TmpePersistentRulesAdapter : IForgeStateAdapterV2
    {
        internal const string Adapter = "bridge.tmpe-persistent-rules";
        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }
        public byte[] CaptureAbsolute(IForgeAdapterContextV1 context) { return TmpePersistentRulesBridge.Capture(context); }
        public void ApplyAbsolute(IForgeAdapterContextV1 context, byte[] state) { TmpePersistentRulesBridge.Apply(context, state); }
    }

    internal static class TmpePersistentRulesBridge
    {
        private const uint Magic = 0x31504d54u; // TMP1
        private static readonly Version ExactVersion = new Version(11, 9, 4, 25100);
        private static Surface surface;

        internal static bool IsAvailable { get { return Resolve() != null; } }

        internal static byte[] Capture(IForgeAdapterContextV1 context)
        {
            Check.NotNull(context, "context");
            Surface s = Resolve();
            if (s == null) throw new InvalidOperationException("Exact TM:PE 11.9.4.1 persistent-rule surface is unavailable.");
            Snapshot value = s.Capture(context);
            return Encode(value);
        }

        internal static void Apply(IForgeAdapterContextV1 context, byte[] bytes)
        {
            Check.NotNull(context, "context");
            Surface s = Resolve();
            if (s == null) throw new InvalidOperationException("Exact TM:PE 11.9.4.1 persistent-rule surface is unavailable.");
            s.Apply(context, Decode(bytes));
        }

        private static Surface Resolve()
        {
            if (surface != null) return surface;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                AssemblyName name = assemblies[i].GetName();
                if (!StringComparer.Ordinal.Equals(name.Name, "TrafficManager") || !ExactVersion.Equals(name.Version)) continue;
                try { surface = new Surface(assemblies[i]); }
                catch { surface = null; }
                break;
            }
            return surface;
        }

        private sealed class Surface
        {
            private readonly Assembly assembly;
            private readonly Type configuration;
            private readonly Type priorityType;
            private readonly Type junctionType;
            private readonly Type timedType;
            private readonly Type timedStepType;
            private readonly Type segmentLightsType;
            private readonly Type lightType;
            private readonly Type arrowsType;
            private readonly Type connectionType;
            private readonly Type speedType;
            private readonly Type vehicleType;
            private readonly Type parkingType;
            private readonly object priority;
            private readonly object junction;
            private readonly object timed;
            private readonly object arrows;
            private readonly object connections;
            private readonly object speed;
            private readonly object vehicles;
            private readonly object parking;
            private readonly object options;
            private readonly Type flagsType;

            internal Surface(Assembly value)
            {
                assembly = value;
                Type mod = NeedType("TrafficManager.Lifecycle.TrafficManagerMod");
                if (mod.GetInterface("ICities.IUserMod") == null) throw new MissingMemberException("TM:PE IUserMod surface drifted.");
                configuration = NeedType("TrafficManager.Configuration");
                priorityType = Nested("PrioritySegment"); junctionType = Nested("SegmentNodeConf");
                timedType = Nested("TimedTrafficLights"); timedStepType = Nested("TimedTrafficLightsStep");
                segmentLightsType = Nested("CustomSegmentLights"); lightType = Nested("CustomSegmentLight");
                arrowsType = Nested("LaneArrowData"); connectionType = Nested("LaneConnection");
                speedType = Nested("LaneSpeedLimit"); vehicleType = Nested("LaneVehicleTypes"); parkingType = Nested("ParkingRestriction");
                priority = Instance("TrafficManager.Manager.Impl.TrafficPriorityManager");
                junction = Instance("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                timed = Instance("TrafficManager.Manager.Impl.TrafficLightSimulationManager");
                arrows = Instance("TrafficManager.Manager.Impl.LaneArrowManager");
                connections = Instance("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager");
                speed = Instance("TrafficManager.Manager.Impl.SpeedLimitManager");
                vehicles = Instance("TrafficManager.Manager.Impl.VehicleRestrictionsManager");
                parking = Instance("TrafficManager.Manager.Impl.ParkingRestrictionsManager");
                options = Instance("TrafficManager.Manager.Impl.OptionsManager");
                flagsType = NeedType("TrafficManager.State.Flags");
                ValidateSurface();
            }

            internal Snapshot Capture(IForgeAdapterContextV1 context)
            {
                Snapshot result = new Snapshot();
                IList values = SaveFacade(priority.GetType(), "AsCustomPrioritySegmentsDM", priority);
                for (int i = 0; i < values.Count; i++) result.Priority.Add(new PriorityRule {
                    Segment = StableSegment(context, U16(values[i], "segmentId")), Node = StableNode(context, U16(values[i], "nodeId")),
                    Priority = I32(values[i], "priorityType") });

                values = SavePublic(junction, ListOf(junctionType));
                for (int i = 0; i < values.Count; i++) result.Junctions.Add(new JunctionRule {
                    Segment = StableSegment(context, U16(values[i], "segmentId")), Start = ReadFlags(Field(values[i], "startNodeFlags")),
                    End = ReadFlags(Field(values[i], "endNodeFlags")) });

                values = SavePublic(timed, ListOf(timedType));
                for (int i = 0; i < values.Count; i++) result.Timed.Add(ReadTimed(context, values[i]));

                values = SaveFacade(arrows.GetType(), "AsLaneArrowsDM", arrows);
                for (int i = 0; i < values.Count; i++) result.Arrows.Add(new LaneValueRule {
                    Lane = StableLane(context, U32(values[i], "laneId")), Value = U32(values[i], "arrows") });

                values = SavePublic(connections, ListOf(connectionType));
                for (int i = 0; i < values.Count; i++) result.Connections.Add(new ConnectionRule {
                    Source = StableLane(context, U32(values[i], "sourceLaneId")), Target = StableLane(context, U32(values[i], "targetLaneId")),
                    SourceStart = Bool(values[i], "sourceStartNode"), Group = EnumI32(Field(values[i], "group")) });

                values = SaveFacade(speed.GetType(), "AsLaneSpeedLimitsDM", speed);
                for (int i = 0; i < values.Count; i++) result.Speed.Add(new LaneValueRule {
                    Lane = StableLane(context, U32(values[i], "laneId")), Value = U16(values[i], "speedLimit") });

                IDictionary defaults = (IDictionary)SaveFacadeRaw(speed.GetType(), "AsCustomDefaultSpeedLimitsDM", speed);
                foreach (DictionaryEntry entry in defaults) result.DefaultSpeed.Add(new StringFloat { Key = (string)entry.Key, Value = Convert.ToSingle(entry.Value) });

                values = SavePublic(vehicles, ListOf(vehicleType));
                for (int i = 0; i < values.Count; i++) result.Vehicles.Add(new LaneValueRule {
                    Lane = StableLane(context, U32(values[i], "laneId")), Value = unchecked((uint)EnumI32(Field(values[i], "vehicleTypes"))) });

                values = SavePublic(parking, ListOf(parkingType));
                for (int i = 0; i < values.Count; i++) result.Parking.Add(new ParkingRule {
                    Segment = StableSegment(context, U16(values[i], "segmentId")), Forward = Bool(values[i], "forwardParkingAllowed"),
                    Backward = Bool(values[i], "backwardParkingAllowed") });

                object[] optionArgs = new object[] { true };
                result.Options = (byte[])NeedMethod(options.GetType(), "SaveData", new Type[] { typeof(bool).MakeByRefType() }).Invoke(options, optionArgs);
                if (!(bool)optionArgs[0] || result.Options == null) throw new InvalidOperationException("TM:PE options capture failed.");
                result.Sort();
                return result;
            }

            internal void Apply(IForgeAdapterContextV1 context, Snapshot value)
            {
                ClearExisting();
                InvokeLoad(priority, ListOf(priorityType), BuildPriority(context, value.Priority));
                InvokeLoad(parking, ListOf(parkingType), BuildParking(context, value.Parking));
                InvokeLoad(vehicles, ListOf(vehicleType), BuildLaneValues(context, value.Vehicles, vehicleType, "vehicleTypes", true));
                InvokeLoad(timed, ListOf(timedType), BuildTimed(context, value.Timed));
                InvokeLoad(arrows, ListOf(arrowsType), BuildLaneValues(context, value.Arrows, arrowsType, "arrows", false));
                InvokeLoad(connections, ListOf(connectionType), BuildConnections(context, value.Connections));
                InvokeLoad(speed, DictionaryOf(typeof(string), typeof(float)), BuildDefaults(value.DefaultSpeed));
                InvokeLoad(speed, ListOf(speedType), BuildLaneValues(context, value.Speed, speedType, "speedLimit", false));
                InvokeLoad(junction, ListOf(junctionType), BuildJunctions(context, value.Junctions));
                NeedMethod(options.GetType(), "OnBeforeLoadData", Type.EmptyTypes).Invoke(options, null);
                object loaded = NeedMethod(options.GetType(), "LoadData", new Type[] { typeof(byte[]) }).Invoke(options, new object[] { value.Options });
                if (!(bool)loaded) throw new InvalidOperationException("TM:PE options projection failed.");
            }

            private void ValidateSurface()
            {
                ValidateFacade(priority.GetType(), "AsCustomPrioritySegmentsDM");
                ValidateFacade(arrows.GetType(), "AsLaneArrowsDM");
                ValidateFacade(speed.GetType(), "AsLaneSpeedLimitsDM");
                ValidateFacade(speed.GetType(), "AsCustomDefaultSpeedLimitsDM");
                NeedMethod(timed.GetType(), "RemoveNodeFromSimulation", new Type[] { typeof(ushort), typeof(bool), typeof(bool) });
                NeedMethod(priority.GetType(), "RemovePrioritySignsFromSegment", new Type[] { typeof(ushort) });
                NeedMethod(junction.GetType(), "ClearSegmentEnd", new Type[] { typeof(ushort), typeof(bool) });
                NeedMethod(arrows.GetType(), "ResetLaneArrows", new Type[] { typeof(ushort), typeof(bool?) });
                NeedMethod(flagsType, "ResetSegmentVehicleRestrictions", new Type[] { typeof(ushort) });
                ValidateLoad(priority, ListOf(priorityType)); ValidateLoad(junction, ListOf(junctionType)); ValidateSave(junction, ListOf(junctionType));
                ValidateLoad(timed, ListOf(timedType)); ValidateSave(timed, ListOf(timedType)); ValidateLoad(arrows, ListOf(arrowsType));
                ValidateLoad(connections, ListOf(connectionType)); ValidateSave(connections, ListOf(connectionType));
                ValidateLoad(speed, ListOf(speedType)); ValidateLoad(speed, DictionaryOf(typeof(string), typeof(float)));
                ValidateLoad(vehicles, ListOf(vehicleType)); ValidateSave(vehicles, ListOf(vehicleType));
                ValidateLoad(parking, ListOf(parkingType)); ValidateSave(parking, ListOf(parkingType));
                NeedMethod(options.GetType(), "SaveData", new Type[] { typeof(bool).MakeByRefType() }); NeedMethod(options.GetType(), "LoadData", new Type[] { typeof(byte[]) });
                string[] required = { "PrioritySegment", "SegmentNodeConf", "TimedTrafficLights", "TimedTrafficLightsStep", "CustomSegmentLights",
                    "CustomSegmentLight", "LaneArrowData", "LaneConnection", "LaneSpeedLimit", "LaneVehicleTypes", "ParkingRestriction" };
                for (int i = 0; i < required.Length; i++) if (configuration.GetNestedType(required[i]) == null) throw new MissingMemberException(required[i]);
                RequireFields(priorityType, "segmentId", "nodeId", "priorityType"); RequireFields(junctionType, "segmentId", "startNodeFlags", "endNodeFlags");
                RequireFields(timedType, "nodeId", "nodeGroup", "started", "currentStep", "timedSteps"); RequireFields(timedStepType, "minTime", "maxTime", "changeMetric", "waitFlowBalance", "segmentLights");
                RequireFields(segmentLightsType, "nodeId", "segmentId", "customLights", "pedestrianLightState", "manualPedestrianMode"); RequireFields(lightType, "nodeId", "segmentId", "currentMode", "leftLight", "mainLight", "rightLight");
                RequireFields(arrowsType, "laneId", "arrows"); RequireFields(connectionType, "sourceLaneId", "targetLaneId", "sourceStartNode", "group");
                RequireFields(speedType, "laneId", "speedLimit"); RequireFields(vehicleType, "laneId", "vehicleTypes"); RequireFields(parkingType, "segmentId", "forwardParkingAllowed", "backwardParkingAllowed");
            }

            private static void ValidateFacade(Type type, string name)
            {
                MethodInfo facade = NeedMethod(type, name, Type.EmptyTypes);
                if (facade.ReturnType.GetMethod("SaveData", new Type[] { typeof(bool).MakeByRefType() }) == null) throw new MissingMethodException(facade.ReturnType.FullName, "SaveData");
            }
            private static void ValidateLoad(object manager, Type parameter) { NeedMethod(manager.GetType(), "LoadData", new Type[] { parameter }); }
            private static void ValidateSave(object manager, Type returnType)
            {
                MethodInfo[] methods = manager.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance); for (int i = 0; i < methods.Length; i++) if (methods[i].Name == "SaveData" && methods[i].ReturnType == returnType) return;
                throw new MissingMethodException(manager.GetType().FullName, "SaveData");
            }
            private static void RequireFields(Type type, params string[] names) { for (int i = 0; i < names.Length; i++) if (type.GetField(names[i], BindingFlags.Public | BindingFlags.Instance) == null) throw new MissingFieldException(type.FullName, names[i]); }

            private void ClearExisting()
            {
                IList oldTimed = SavePublic(timed, ListOf(timedType));
                MethodInfo removeTimed = NeedMethod(timed.GetType(), "RemoveNodeFromSimulation", new Type[] { typeof(ushort), typeof(bool), typeof(bool) });
                for (int i = 0; i < oldTimed.Count; i++) removeTimed.Invoke(timed, new object[] { U16(oldTimed[i], "nodeId"), false, true });
                NeedMethod(speed.GetType(), "OnBeforeLoadData", Type.EmptyTypes).Invoke(speed, null);
                NeedMethod(parking.GetType(), "OnBeforeLoadData", Type.EmptyTypes).Invoke(parking, null);
                NeedMethod(connections.GetType(), "OnBeforeLoadData", Type.EmptyTypes).Invoke(connections, null);
                MethodInfo clearArrows = NeedMethod(arrows.GetType(), "ResetLaneArrows", new Type[] { typeof(ushort), typeof(bool?) });
                MethodInfo clearVehicles = NeedMethod(flagsType, "ResetSegmentVehicleRestrictions", new Type[] { typeof(ushort) });
                MethodInfo clearJunction = NeedMethod(junction.GetType(), "ClearSegmentEnd", new Type[] { typeof(ushort), typeof(bool) });
                MethodInfo clearPriority = NeedMethod(priority.GetType(), "RemovePrioritySignsFromSegment", new Type[] { typeof(ushort) });
                int limit = NetManager.instance.m_segments.m_buffer.Length;
                for (int i = 1; i < limit; i++)
                {
                    ushort segment = (ushort)i;
                    clearArrows.Invoke(arrows, new object[] { segment, null }); clearVehicles.Invoke(null, new object[] { segment });
                    clearJunction.Invoke(junction, new object[] { segment, true }); clearJunction.Invoke(junction, new object[] { segment, false });
                    clearPriority.Invoke(priority, new object[] { segment });
                }
            }

            private TimedRule ReadTimed(IForgeAdapterContextV1 context, object source)
            {
                TimedRule result = new TimedRule { Node = StableNode(context, U16(source, "nodeId")), Started = Bool(source, "started"), CurrentStep = I32(source, "currentStep") };
                IList group = (IList)Field(source, "nodeGroup");
                for (int i = 0; i < group.Count; i++) result.Group.Add(StableNode(context, Convert.ToUInt16(group[i])));
                IList steps = (IList)Field(source, "timedSteps");
                for (int i = 0; i < steps.Count; i++)
                {
                    object step = steps[i]; TimedStep target = new TimedStep { Min = I32(step, "minTime"), Max = I32(step, "maxTime"), Metric = I32(step, "changeMetric"), Balance = Convert.ToSingle(Field(step, "waitFlowBalance")) };
                    IDictionary lights = (IDictionary)Field(step, "segmentLights");
                    foreach (DictionaryEntry item in lights) target.Segments.Add(ReadSegmentLights(context, Convert.ToUInt16(item.Key), item.Value));
                    target.Segments.Sort(CompareSegmentLights); result.Steps.Add(target);
                }
                return result;
            }

            private SegmentLights ReadSegmentLights(IForgeAdapterContextV1 context, ushort segment, object source)
            {
                SegmentLights result = new SegmentLights { Segment = StableSegment(context, segment), ManualPedestrian = Bool(source, "manualPedestrianMode") };
                object pedestrian = Field(source, "pedestrianLightState");
                if (pedestrian != null) { result.HasPedestrian = true; result.Pedestrian = EnumI32(pedestrian); }
                IDictionary lights = (IDictionary)Field(source, "customLights");
                foreach (DictionaryEntry item in lights)
                {
                    object light = item.Value; result.Lights.Add(new VehicleLight { VehicleType = EnumI32(item.Key), Mode = I32(light, "currentMode"),
                        Left = EnumI32(Field(light, "leftLight")), Main = EnumI32(Field(light, "mainLight")), Right = EnumI32(Field(light, "rightLight")) });
                }
                result.Lights.Sort(delegate(VehicleLight a, VehicleLight b) { return a.VehicleType.CompareTo(b.VehicleType); });
                return result;
            }

            private Type NeedType(string name) { Type type = assembly.GetType(name, false); if (type == null) throw new TypeLoadException(name); return type; }
            private Type Nested(string name) { Type type = configuration.GetNestedType(name); if (type == null) throw new TypeLoadException(name); return type; }
            private object Instance(string typeName)
            {
                Type type = NeedType(typeName); FieldInfo field = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                if (field != null) return field.GetValue(null); PropertyInfo property = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (property == null) throw new MissingMemberException(typeName, "Instance"); return property.GetValue(null, null);
            }

            private IList SavePublic(object manager, Type returnType)
            {
                MethodInfo[] methods = manager.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++) if (methods[i].Name == "SaveData" && methods[i].ReturnType == returnType)
                { object[] args = new object[] { true }; IList result = (IList)methods[i].Invoke(manager, args); if (!(bool)args[0] || result == null) throw new InvalidOperationException("TM:PE SaveData failed."); return result; }
                throw new MissingMethodException(manager.GetType().FullName, "SaveData");
            }

            private IList SaveFacade(Type managerType, string name, object manager) { return (IList)SaveFacadeRaw(managerType, name, manager); }
            private object SaveFacadeRaw(Type managerType, string name, object manager)
            {
                MethodInfo facade = NeedMethod(managerType, name, Type.EmptyTypes); object provider = facade.Invoke(null, null);
                MethodInfo save = facade.ReturnType.GetMethod("SaveData", new Type[] { typeof(bool).MakeByRefType() });
                if (save == null) throw new MissingMethodException(facade.ReturnType.FullName, "SaveData");
                object[] args = new object[] { true }; object result = save.Invoke(provider ?? manager, args);
                if (!(bool)args[0] || result == null) throw new InvalidOperationException("TM:PE " + name + " capture failed."); return result;
            }

            private static void InvokeLoad(object manager, Type parameter, object value)
            {
                object result = NeedMethod(manager.GetType(), "LoadData", new Type[] { parameter }).Invoke(manager, new object[] { value });
                if (!(bool)result) throw new InvalidOperationException("TM:PE " + parameter.Name + " projection failed.");
            }

            private static MethodInfo NeedMethod(Type type, string name, Type[] parameters)
            {
                MethodInfo method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, null, parameters, null);
                if (method == null) throw new MissingMethodException(type.FullName, name); return method;
            }
            private static Type ListOf(Type item) { return typeof(List<>).MakeGenericType(item); }
            private static Type DictionaryOf(Type key, Type value) { return typeof(Dictionary<,>).MakeGenericType(key, value); }
            private static object Field(object value, string name) { FieldInfo field = value.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance); if (field == null) throw new MissingFieldException(value.GetType().FullName, name); return field.GetValue(value); }
            private static void SetField(object value, string name, object fieldValue) { FieldInfo field = value.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance); if (field == null) throw new MissingFieldException(value.GetType().FullName, name); field.SetValue(value, fieldValue); }
            private static object New(Type type) { return FormatterServices.GetUninitializedObject(type); }
            private static ushort U16(object value, string name) { return Convert.ToUInt16(Field(value, name)); }
            private static uint U32(object value, string name) { return Convert.ToUInt32(Field(value, name)); }
            private static int I32(object value, string name) { return Convert.ToInt32(Field(value, name)); }
            private static bool Bool(object value, string name) { return Convert.ToBoolean(Field(value, name)); }
            private static int EnumI32(object value) { return Convert.ToInt32(value); }

            private object BuildPriority(IForgeAdapterContextV1 context, List<PriorityRule> rules)
            {
                IList list = (IList)Activator.CreateInstance(ListOf(priorityType));
                for (int i = 0; i < rules.Count; i++)
                {
                    object value = New(priorityType); SetField(value, "segmentId", NativeSegment(context, rules[i].Segment));
                    SetField(value, "nodeId", NativeNode(context, rules[i].Node)); SetField(value, "priorityType", rules[i].Priority); list.Add(value);
                }
                return list;
            }

            private object BuildParking(IForgeAdapterContextV1 context, List<ParkingRule> rules)
            {
                IList list = (IList)Activator.CreateInstance(ListOf(parkingType));
                for (int i = 0; i < rules.Count; i++)
                {
                    object value = New(parkingType); SetField(value, "segmentId", NativeSegment(context, rules[i].Segment));
                    SetField(value, "forwardParkingAllowed", rules[i].Forward); SetField(value, "backwardParkingAllowed", rules[i].Backward); list.Add(value);
                }
                return list;
            }

            private object BuildLaneValues(IForgeAdapterContextV1 context, List<LaneValueRule> rules, Type itemType, string valueField, bool enumValue)
            {
                IList list = (IList)Activator.CreateInstance(ListOf(itemType)); FieldInfo target = itemType.GetField(valueField);
                if (target == null) throw new MissingFieldException(itemType.FullName, valueField);
                for (int i = 0; i < rules.Count; i++)
                {
                    object value = New(itemType); SetField(value, "laneId", NativeLane(context, rules[i].Lane));
                    object scalar = enumValue ? Enum.ToObject(target.FieldType, unchecked((int)rules[i].Value)) : Convert.ChangeType(rules[i].Value, target.FieldType);
                    target.SetValue(value, scalar); list.Add(value);
                }
                return list;
            }

            private object BuildConnections(IForgeAdapterContextV1 context, List<ConnectionRule> rules)
            {
                IList list = (IList)Activator.CreateInstance(ListOf(connectionType)); FieldInfo group = connectionType.GetField("group");
                if (group == null) throw new MissingFieldException(connectionType.FullName, "group");
                for (int i = 0; i < rules.Count; i++)
                {
                    object value = New(connectionType); SetField(value, "sourceLaneId", NativeLane(context, rules[i].Source));
                    SetField(value, "targetLaneId", NativeLane(context, rules[i].Target)); SetField(value, "sourceStartNode", rules[i].SourceStart);
                    group.SetValue(value, Enum.ToObject(group.FieldType, rules[i].Group)); list.Add(value);
                }
                return list;
            }

            private object BuildDefaults(List<StringFloat> values)
            {
                IDictionary result = (IDictionary)Activator.CreateInstance(DictionaryOf(typeof(string), typeof(float)));
                for (int i = 0; i < values.Count; i++) result.Add(values[i].Key, values[i].Value); return result;
            }

            private object BuildJunctions(IForgeAdapterContextV1 context, List<JunctionRule> rules)
            {
                IList list = (IList)Activator.CreateInstance(ListOf(junctionType));
                for (int i = 0; i < rules.Count; i++)
                {
                    object value = New(junctionType); SetField(value, "segmentId", NativeSegment(context, rules[i].Segment));
                    SetField(value, "startNodeFlags", BuildFlags(junctionType.GetField("startNodeFlags").FieldType, rules[i].Start));
                    SetField(value, "endNodeFlags", BuildFlags(junctionType.GetField("endNodeFlags").FieldType, rules[i].End)); list.Add(value);
                }
                return list;
            }

            private object BuildTimed(IForgeAdapterContextV1 context, List<TimedRule> rules)
            {
                IList result = (IList)Activator.CreateInstance(ListOf(timedType));
                for (int i = 0; i < rules.Count; i++)
                {
                    TimedRule rule = rules[i]; ushort node = NativeNode(context, rule.Node); object value = New(timedType);
                    SetField(value, "nodeId", node); SetField(value, "started", rule.Started); SetField(value, "currentStep", rule.CurrentStep);
                    IList group = (IList)Activator.CreateInstance(timedType.GetField("nodeGroup").FieldType);
                    for (int g = 0; g < rule.Group.Count; g++) group.Add(NativeNode(context, rule.Group[g])); SetField(value, "nodeGroup", group);
                    IList steps = (IList)Activator.CreateInstance(timedType.GetField("timedSteps").FieldType);
                    for (int j = 0; j < rule.Steps.Count; j++) steps.Add(BuildTimedStep(context, node, rule.Steps[j]));
                    SetField(value, "timedSteps", steps); result.Add(value);
                }
                return result;
            }

            private object BuildTimedStep(IForgeAdapterContextV1 context, ushort node, TimedStep rule)
            {
                object value = New(timedStepType); SetField(value, "minTime", rule.Min); SetField(value, "maxTime", rule.Max);
                SetField(value, "changeMetric", rule.Metric); SetField(value, "waitFlowBalance", rule.Balance);
                IDictionary segments = (IDictionary)Activator.CreateInstance(timedStepType.GetField("segmentLights").FieldType);
                for (int i = 0; i < rule.Segments.Count; i++)
                {
                    SegmentLights source = rule.Segments[i]; ushort segment = NativeSegment(context, source.Segment); object target = New(segmentLightsType);
                    SetField(target, "nodeId", node); SetField(target, "segmentId", segment); SetField(target, "manualPedestrianMode", source.ManualPedestrian);
                    FieldInfo pedestrian = segmentLightsType.GetField("pedestrianLightState");
                    if (source.HasPedestrian)
                    {
                        Type enumType = Nullable.GetUnderlyingType(pedestrian.FieldType); pedestrian.SetValue(target, Enum.ToObject(enumType, source.Pedestrian));
                    }
                    IDictionary lights = (IDictionary)Activator.CreateInstance(segmentLightsType.GetField("customLights").FieldType);
                    Type vehicleEnum = segmentLightsType.GetField("customLights").FieldType.GetGenericArguments()[0];
                    for (int k = 0; k < source.Lights.Count; k++)
                    {
                        VehicleLight light = source.Lights[k]; object targetLight = New(lightType); SetField(targetLight, "nodeId", node); SetField(targetLight, "segmentId", segment);
                        SetField(targetLight, "currentMode", light.Mode); SetEnumField(targetLight, "leftLight", light.Left); SetEnumField(targetLight, "mainLight", light.Main); SetEnumField(targetLight, "rightLight", light.Right);
                        lights.Add(Enum.ToObject(vehicleEnum, light.VehicleType), targetLight);
                    }
                    SetField(target, "customLights", lights); segments.Add(segment, target);
                }
                SetField(value, "segmentLights", segments); return value;
            }

            private static object BuildFlags(Type type, NullableFlags values)
            {
                object result = New(type); string[] names = FlagNames;
                for (int i = 0; i < names.Length; i++) if (values.Values[i] != -1) SetField(result, names[i], values.Values[i] == 1);
                return result;
            }

            private static void SetEnumField(object value, string name, int scalar)
            {
                FieldInfo field = value.GetType().GetField(name); if (field == null) throw new MissingFieldException(value.GetType().FullName, name);
                field.SetValue(value, Enum.ToObject(field.FieldType, scalar));
            }
        }

        private sealed class Snapshot
        {
            public readonly List<PriorityRule> Priority = new List<PriorityRule>();
            public readonly List<JunctionRule> Junctions = new List<JunctionRule>();
            public readonly List<TimedRule> Timed = new List<TimedRule>();
            public readonly List<LaneValueRule> Arrows = new List<LaneValueRule>();
            public readonly List<ConnectionRule> Connections = new List<ConnectionRule>();
            public readonly List<LaneValueRule> Speed = new List<LaneValueRule>();
            public readonly List<StringFloat> DefaultSpeed = new List<StringFloat>();
            public readonly List<LaneValueRule> Vehicles = new List<LaneValueRule>();
            public readonly List<ParkingRule> Parking = new List<ParkingRule>();
            public byte[] Options;
            public void Sort()
            {
                Priority.Sort(ComparePriority); Junctions.Sort(CompareJunction); Timed.Sort(CompareTimed); Arrows.Sort(CompareLaneValue);
                Connections.Sort(CompareConnection); Speed.Sort(CompareLaneValue); Vehicles.Sort(CompareLaneValue); Parking.Sort(CompareParking);
                DefaultSpeed.Sort(delegate(StringFloat a, StringFloat b) { return StringComparer.Ordinal.Compare(a.Key, b.Key); });
            }
        }

        private sealed class PriorityRule { public EntityIdentityV2 Segment; public EntityIdentityV2 Node; public int Priority; }
        private sealed class JunctionRule { public EntityIdentityV2 Segment; public NullableFlags Start; public NullableFlags End; }
        private sealed class NullableFlags { public readonly sbyte[] Values = new sbyte[6]; }
        private sealed class TimedRule { public EntityIdentityV2 Node; public readonly List<EntityIdentityV2> Group = new List<EntityIdentityV2>(); public bool Started; public int CurrentStep; public readonly List<TimedStep> Steps = new List<TimedStep>(); }
        private sealed class TimedStep { public int Min; public int Max; public int Metric; public float Balance; public readonly List<SegmentLights> Segments = new List<SegmentLights>(); }
        private sealed class SegmentLights { public EntityIdentityV2 Segment; public bool HasPedestrian; public int Pedestrian; public bool ManualPedestrian; public readonly List<VehicleLight> Lights = new List<VehicleLight>(); }
        private sealed class VehicleLight { public int VehicleType; public int Mode; public int Left; public int Main; public int Right; }
        private sealed class LaneRef { public EntityIdentityV2 Segment; public ushort LaneIndex; }
        private sealed class LaneValueRule { public LaneRef Lane; public uint Value; }
        private sealed class ConnectionRule { public LaneRef Source; public LaneRef Target; public bool SourceStart; public int Group; }
        private sealed class StringFloat { public string Key; public float Value; }
        private sealed class ParkingRule { public EntityIdentityV2 Segment; public bool Forward; public bool Backward; }

        private static readonly string[] FlagNames = { "uturnAllowed", "turnOnRedAllowed", "farTurnOnRedAllowed", "straightLaneChangingAllowed", "enterWhenBlockedAllowed", "pedestrianCrossingAllowed" };

        private static NullableFlags ReadFlags(object value)
        {
            NullableFlags result = new NullableFlags();
            for (int i = 0; i < FlagNames.Length; i++)
            {
                FieldInfo field = value.GetType().GetField(FlagNames[i]); if (field == null) throw new MissingFieldException(value.GetType().FullName, FlagNames[i]);
                object scalar = field.GetValue(value); result.Values[i] = scalar == null ? (sbyte)-1 : ((bool)scalar ? (sbyte)1 : (sbyte)0);
            }
            return result;
        }

        private static EntityIdentityV2 StableNode(IForgeAdapterContextV1 context, ushort native)
        {
            EntityIdentityV2 result; bool found = context.IsAuthoritative
                ? RuntimeServices.Multiplayer.TryResolveHostNetNode(native, out result)
                : RuntimeServices.Multiplayer.TryResolveClientNetNode(native, out result);
            if (!found || !result.IsValid) throw new InvalidOperationException("TM:PE node has no Forge Stable Net identity."); return result;
        }

        private static EntityIdentityV2 StableSegment(IForgeAdapterContextV1 context, ushort native)
        {
            EntityIdentityV2 result; bool found = context.IsAuthoritative
                ? RuntimeServices.Multiplayer.TryResolveHostNetSegment(native, out result)
                : RuntimeServices.Multiplayer.TryResolveClientNetSegment(native, out result);
            if (!found || !result.IsValid) throw new InvalidOperationException("TM:PE segment has no Forge Stable Net identity."); return result;
        }

        private static ushort NativeNode(IForgeAdapterContextV1 context, EntityIdentityV2 identity)
        {
            uint native; bool found = context.IsAuthoritative
                ? RuntimeServices.Multiplayer.TryResolveHostNetNodeNative(identity, out native)
                : RuntimeServices.Multiplayer.TryResolveClientNetNodeNative(identity, out native);
            if (!found || native == 0 || native > ushort.MaxValue) throw new InvalidOperationException("TM:PE Stable Node is unavailable locally."); return (ushort)native;
        }

        private static ushort NativeSegment(IForgeAdapterContextV1 context, EntityIdentityV2 identity)
        {
            uint native; bool found = context.IsAuthoritative
                ? RuntimeServices.Multiplayer.TryResolveHostNetSegmentNative(identity, out native)
                : RuntimeServices.Multiplayer.TryResolveClientNetSegmentNative(identity, out native);
            if (!found || native == 0 || native > ushort.MaxValue) throw new InvalidOperationException("TM:PE Stable Segment is unavailable locally."); return (ushort)native;
        }

        private static LaneRef StableLane(IForgeAdapterContextV1 context, uint nativeLane)
        {
            if (nativeLane == 0 || nativeLane >= NetManager.instance.m_lanes.m_buffer.Length) throw new InvalidOperationException("TM:PE lane is invalid.");
            ushort segment = NetManager.instance.m_lanes.m_buffer[nativeLane].m_segment;
            if (segment == 0) throw new InvalidOperationException("TM:PE lane has no segment.");
            uint cursor = NetManager.instance.m_segments.m_buffer[segment].m_lanes; ushort index = 0;
            while (cursor != 0 && cursor != nativeLane)
            {
                if (cursor >= NetManager.instance.m_lanes.m_buffer.Length || index == ushort.MaxValue) throw new InvalidOperationException("TM:PE lane chain is invalid.");
                cursor = NetManager.instance.m_lanes.m_buffer[cursor].m_nextLane; index++;
            }
            if (cursor != nativeLane) throw new InvalidOperationException("TM:PE lane is not in its segment chain.");
            return new LaneRef { Segment = StableSegment(context, segment), LaneIndex = index };
        }

        private static uint NativeLane(IForgeAdapterContextV1 context, LaneRef lane)
        {
            ushort segment = NativeSegment(context, lane.Segment); uint cursor = NetManager.instance.m_segments.m_buffer[segment].m_lanes;
            for (ushort i = 0; i < lane.LaneIndex; i++)
            {
                if (cursor == 0 || cursor >= NetManager.instance.m_lanes.m_buffer.Length) throw new InvalidOperationException("TM:PE Stable Lane index is unavailable locally.");
                cursor = NetManager.instance.m_lanes.m_buffer[cursor].m_nextLane;
            }
            if (cursor == 0 || cursor >= NetManager.instance.m_lanes.m_buffer.Length || NetManager.instance.m_lanes.m_buffer[cursor].m_segment != segment)
                throw new InvalidOperationException("TM:PE Stable Lane does not resolve to the expected segment.");
            return cursor;
        }

        private static byte[] Encode(Snapshot value)
        {
            using (MemoryStream stream = new MemoryStream()) using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic); writer.Write((ushort)1);
                WriteCount(writer, value.Priority.Count); for (int i = 0; i < value.Priority.Count; i++) { WriteIdentity(writer, value.Priority[i].Segment); WriteIdentity(writer, value.Priority[i].Node); writer.Write(value.Priority[i].Priority); }
                WriteCount(writer, value.Junctions.Count); for (int i = 0; i < value.Junctions.Count; i++) { WriteIdentity(writer, value.Junctions[i].Segment); WriteFlags(writer, value.Junctions[i].Start); WriteFlags(writer, value.Junctions[i].End); }
                WriteCount(writer, value.Timed.Count); for (int i = 0; i < value.Timed.Count; i++) WriteTimed(writer, value.Timed[i]);
                WriteLaneValues(writer, value.Arrows);
                WriteCount(writer, value.Connections.Count); for (int i = 0; i < value.Connections.Count; i++) { WriteLane(writer, value.Connections[i].Source); WriteLane(writer, value.Connections[i].Target); writer.Write(value.Connections[i].SourceStart); writer.Write(value.Connections[i].Group); }
                WriteLaneValues(writer, value.Speed);
                WriteCount(writer, value.DefaultSpeed.Count); for (int i = 0; i < value.DefaultSpeed.Count; i++) { WriteString(writer, value.DefaultSpeed[i].Key); writer.Write(value.DefaultSpeed[i].Value); }
                WriteLaneValues(writer, value.Vehicles);
                WriteCount(writer, value.Parking.Count); for (int i = 0; i < value.Parking.Count; i++) { WriteIdentity(writer, value.Parking[i].Segment); writer.Write(value.Parking[i].Forward); writer.Write(value.Parking[i].Backward); }
                if (value.Options == null || value.Options.Length > 65536) throw new InvalidDataException("TM:PE saved-game options are invalid."); writer.Write(value.Options.Length); writer.Write(value.Options);
                writer.Flush(); if (stream.Length > Limits.FramePayloadBytes) throw new InvalidOperationException("TM:PE persistent rules exceed one Forge frame."); return stream.ToArray();
            }
        }

        private static Snapshot Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 16 || bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Invalid TM:PE persistent-rule payload size.");
            using (MemoryStream stream = new MemoryStream(bytes, false)) using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != 1) throw new InvalidDataException("Invalid TM:PE persistent-rule header."); Snapshot value = new Snapshot(); int count;
                count = ReadCount(reader); for (int i = 0; i < count; i++) value.Priority.Add(new PriorityRule { Segment = ReadIdentity(reader), Node = ReadIdentity(reader), Priority = reader.ReadInt32() });
                count = ReadCount(reader); for (int i = 0; i < count; i++) value.Junctions.Add(new JunctionRule { Segment = ReadIdentity(reader), Start = ReadFlags(reader), End = ReadFlags(reader) });
                count = ReadCount(reader); for (int i = 0; i < count; i++) value.Timed.Add(ReadTimed(reader));
                ReadLaneValues(reader, value.Arrows);
                count = ReadCount(reader); for (int i = 0; i < count; i++) value.Connections.Add(new ConnectionRule { Source = ReadLane(reader), Target = ReadLane(reader), SourceStart = reader.ReadBoolean(), Group = reader.ReadInt32() });
                ReadLaneValues(reader, value.Speed);
                count = ReadCount(reader); for (int i = 0; i < count; i++) value.DefaultSpeed.Add(new StringFloat { Key = ReadString(reader), Value = reader.ReadSingle() });
                ReadLaneValues(reader, value.Vehicles);
                count = ReadCount(reader); for (int i = 0; i < count; i++) value.Parking.Add(new ParkingRule { Segment = ReadIdentity(reader), Forward = reader.ReadBoolean(), Backward = reader.ReadBoolean() });
                int optionLength = reader.ReadInt32(); if (optionLength < 0 || optionLength > 65536) throw new InvalidDataException("Invalid TM:PE options length."); value.Options = reader.ReadBytes(optionLength);
                if (value.Options.Length != optionLength || stream.Position != stream.Length) throw new InvalidDataException("Truncated or trailing TM:PE persistent-rule payload."); value.Sort(); return value;
            }
        }

        private static void WriteTimed(BinaryWriter writer, TimedRule value)
        {
            WriteIdentity(writer, value.Node); WriteCount(writer, value.Group.Count); for (int i = 0; i < value.Group.Count; i++) WriteIdentity(writer, value.Group[i]);
            writer.Write(value.Started); writer.Write(value.CurrentStep); WriteCount(writer, value.Steps.Count);
            for (int i = 0; i < value.Steps.Count; i++)
            {
                TimedStep step = value.Steps[i]; writer.Write(step.Min); writer.Write(step.Max); writer.Write(step.Metric); writer.Write(step.Balance); WriteCount(writer, step.Segments.Count);
                for (int j = 0; j < step.Segments.Count; j++)
                {
                    SegmentLights segment = step.Segments[j]; WriteIdentity(writer, segment.Segment); writer.Write(segment.HasPedestrian); if (segment.HasPedestrian) writer.Write(segment.Pedestrian);
                    writer.Write(segment.ManualPedestrian); WriteCount(writer, segment.Lights.Count);
                    for (int k = 0; k < segment.Lights.Count; k++) { VehicleLight light = segment.Lights[k]; writer.Write(light.VehicleType); writer.Write(light.Mode); writer.Write(light.Left); writer.Write(light.Main); writer.Write(light.Right); }
                }
            }
        }

        private static TimedRule ReadTimed(BinaryReader reader)
        {
            TimedRule value = new TimedRule { Node = ReadIdentity(reader) }; int count = ReadCount(reader);
            for (int i = 0; i < count; i++) value.Group.Add(ReadIdentity(reader)); value.Started = reader.ReadBoolean(); value.CurrentStep = reader.ReadInt32(); count = ReadCount(reader);
            for (int i = 0; i < count; i++)
            {
                TimedStep step = new TimedStep { Min = reader.ReadInt32(), Max = reader.ReadInt32(), Metric = reader.ReadInt32(), Balance = reader.ReadSingle() }; int segments = ReadCount(reader);
                for (int j = 0; j < segments; j++)
                {
                    SegmentLights segment = new SegmentLights { Segment = ReadIdentity(reader), HasPedestrian = reader.ReadBoolean() }; if (segment.HasPedestrian) segment.Pedestrian = reader.ReadInt32();
                    segment.ManualPedestrian = reader.ReadBoolean(); int lights = ReadCount(reader);
                    for (int k = 0; k < lights; k++) segment.Lights.Add(new VehicleLight { VehicleType = reader.ReadInt32(), Mode = reader.ReadInt32(), Left = reader.ReadInt32(), Main = reader.ReadInt32(), Right = reader.ReadInt32() });
                    step.Segments.Add(segment);
                }
                value.Steps.Add(step);
            }
            return value;
        }

        private static void WriteLaneValues(BinaryWriter writer, List<LaneValueRule> values) { WriteCount(writer, values.Count); for (int i = 0; i < values.Count; i++) { WriteLane(writer, values[i].Lane); writer.Write(values[i].Value); } }
        private static void ReadLaneValues(BinaryReader reader, List<LaneValueRule> values) { int count = ReadCount(reader); for (int i = 0; i < count; i++) values.Add(new LaneValueRule { Lane = ReadLane(reader), Value = reader.ReadUInt32() }); }
        private static void WriteLane(BinaryWriter writer, LaneRef value) { WriteIdentity(writer, value.Segment); writer.Write(value.LaneIndex); }
        private static LaneRef ReadLane(BinaryReader reader) { return new LaneRef { Segment = ReadIdentity(reader), LaneIndex = reader.ReadUInt16() }; }
        private static void WriteIdentity(BinaryWriter writer, EntityIdentityV2 value) { if (!value.IsValid) throw new InvalidDataException("Invalid TM:PE Stable Net identity."); writer.Write(value.EntityId); writer.Write(value.Generation); }
        private static EntityIdentityV2 ReadIdentity(BinaryReader reader) { EntityIdentityV2 value = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32()); if (!value.IsValid) throw new InvalidDataException("Invalid TM:PE Stable Net identity."); return value; }
        private static void WriteFlags(BinaryWriter writer, NullableFlags value) { for (int i = 0; i < value.Values.Length; i++) { if (value.Values[i] < -1 || value.Values[i] > 1) throw new InvalidDataException("Invalid TM:PE junction flag."); writer.Write(value.Values[i]); } }
        private static NullableFlags ReadFlags(BinaryReader reader) { NullableFlags value = new NullableFlags(); for (int i = 0; i < value.Values.Length; i++) { value.Values[i] = reader.ReadSByte(); if (value.Values[i] < -1 || value.Values[i] > 1) throw new InvalidDataException("Invalid TM:PE junction flag."); } return value; }
        private static void WriteCount(BinaryWriter writer, int count) { if (count < 0 || count > 131072) throw new InvalidDataException("TM:PE collection exceeds its bound."); writer.Write(count); }
        private static int ReadCount(BinaryReader reader) { int count = reader.ReadInt32(); if (count < 0 || count > 131072) throw new InvalidDataException("TM:PE collection exceeds its bound."); return count; }
        private static void WriteString(BinaryWriter writer, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty); if (bytes.Length == 0 || bytes.Length > 1024) throw new InvalidDataException("Invalid TM:PE string."); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
        private static string ReadString(BinaryReader reader) { int count = reader.ReadUInt16(); if (count == 0 || count > 1024) throw new InvalidDataException("Invalid TM:PE string."); byte[] bytes = reader.ReadBytes(count); if (bytes.Length != count) throw new EndOfStreamException(); return Encoding.UTF8.GetString(bytes); }

        private static int CompareIdentity(EntityIdentityV2 a, EntityIdentityV2 b) { int value = a.EntityId.CompareTo(b.EntityId); return value != 0 ? value : a.Generation.CompareTo(b.Generation); }
        private static int CompareLane(LaneRef a, LaneRef b) { int value = CompareIdentity(a.Segment, b.Segment); return value != 0 ? value : a.LaneIndex.CompareTo(b.LaneIndex); }
        private static int ComparePriority(PriorityRule a, PriorityRule b) { int value = CompareIdentity(a.Segment, b.Segment); if (value == 0) value = CompareIdentity(a.Node, b.Node); return value != 0 ? value : a.Priority.CompareTo(b.Priority); }
        private static int CompareJunction(JunctionRule a, JunctionRule b) { return CompareIdentity(a.Segment, b.Segment); }
        private static int CompareTimed(TimedRule a, TimedRule b) { return CompareIdentity(a.Node, b.Node); }
        private static int CompareSegmentLights(SegmentLights a, SegmentLights b) { return CompareIdentity(a.Segment, b.Segment); }
        private static int CompareLaneValue(LaneValueRule a, LaneValueRule b) { int value = CompareLane(a.Lane, b.Lane); return value != 0 ? value : a.Value.CompareTo(b.Value); }
        private static int CompareConnection(ConnectionRule a, ConnectionRule b) { int value = CompareLane(a.Source, b.Source); if (value == 0) value = CompareLane(a.Target, b.Target); if (value == 0) value = a.SourceStart.CompareTo(b.SourceStart); return value != 0 ? value : a.Group.CompareTo(b.Group); }
        private static int CompareParking(ParkingRule a, ParkingRule b) { return CompareIdentity(a.Segment, b.Segment); }
    }
}
