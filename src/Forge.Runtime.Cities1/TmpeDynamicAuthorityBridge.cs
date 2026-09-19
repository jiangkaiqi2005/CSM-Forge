using System;
using System.Collections.Generic;
using System.Reflection;
using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>Exact TM:PE 11.9.4.1 dynamic simulation barrier; Host results flow through P7 adapters.</summary>
    internal static class TmpeDynamicAuthorityBridge
    {
        private static readonly Version ExactVersion = new Version(11, 9, 4, 25100);
        private static bool installed;

        internal static void InstallOptionalPatches(Harmony harmony)
        {
            if (installed || harmony == null) return; Assembly assembly = FindAssembly(); if (assembly == null) return;
            Type path = NeedType(assembly, "TrafficManager.Custom.PathFinding.CustomPathManager");
            Type threading = NeedType(assembly, "TrafficManager.Lifecycle.ThreadingExtension");
            MethodInfo boolBarrier = NeedLocal("ClientBarrier"); MethodInfo createBarrier = NeedLocal("ClientCreateBarrier");
            MethodInfo createObserved = NeedLocal("HostCreatePostfix"); MethodInfo releasePrefix = NeedLocal("HostReleasePrefix"); MethodInfo releasePostfix = NeedLocal("HostReleasePostfix");
            Patch(harmony, NeedMethod(path, "SimulationStepImpl", new[] { typeof(int) }), boolBarrier, null);
            Patch(harmony, NeedMethod(path, "CustomCreatePath", null), createBarrier, createObserved);
            Patch(harmony, NeedMethod(path, "CreateTransportLinePath", null), createBarrier, createObserved);
            MethodInfo customRelease = NeedMethod(path, "CustomReleasePath", new[] { typeof(uint) });
            harmony.Patch(customRelease, new HarmonyMethod(releasePrefix), new HarmonyMethod(releasePostfix));
            Patch(harmony, NeedMethod(path, "ReleasePath", new[] { typeof(uint) }), boolBarrier, null);
            Patch(harmony, NeedMethod(threading, "OnBeforeSimulationTick", Type.EmptyTypes), boolBarrier, null);
            Patch(harmony, NeedMethod(threading, "OnBeforeSimulationFrame", Type.EmptyTypes), boolBarrier, null);
            Patch(harmony, NeedMethod(threading, "OnAfterSimulationTick", Type.EmptyTypes), boolBarrier, null);
            installed = true;
        }

        internal static void ResetPatchState() { installed = false; }

        private static bool ClientBarrier() { return DynamicSimulationPatchPolicy.AllowClientManagerMutation(); }
        private static bool ClientCreateBarrier(ref uint __0, ref bool __result)
        { if (DynamicSimulationPatchPolicy.AllowClientManagerMutation()) return true; __0 = 0; __result = false; return false; }
        private static void HostCreatePostfix(uint __0, bool __result)
        { if (__result && !RuntimeScopeGuard.IsApplying && RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.HostLive) PathUnitStateAdapter.ObserveHostCreated(__0); }
        private static bool HostReleasePrefix(uint __0, out EntityIdentityV2[] __state)
        {
            __state = null; if (!DynamicSimulationPatchPolicy.AllowClientManagerMutation()) return false;
            if (!RuntimeScopeGuard.IsApplying && RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.HostLive) __state = PathUnitStateAdapter.PrepareHostRelease(__0, true); return true;
        }
        private static void HostReleasePostfix(EntityIdentityV2[] __state) { PathUnitStateAdapter.CompleteHostRelease(__state); }

        private static Assembly FindAssembly()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                AssemblyName name = assemblies[i].GetName();
                if (StringComparer.Ordinal.Equals(name.Name, "TrafficManager") && ExactVersion.Equals(name.Version)) return assemblies[i];
            }
            return null;
        }
        private static Type NeedType(Assembly assembly, string name) { Type value = assembly.GetType(name, false); if (value == null) throw new TypeLoadException(name); return value; }
        private static MethodInfo NeedLocal(string name) { MethodInfo value = typeof(TmpeDynamicAuthorityBridge).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic); if (value == null) throw new MissingMethodException(typeof(TmpeDynamicAuthorityBridge).FullName, name); return value; }
        private static MethodInfo NeedMethod(Type type, string name, Type[] parameters)
        {
            if (parameters != null)
            {
                MethodInfo exact = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null);
                if (exact == null) throw new MissingMethodException(type.FullName, name); return exact;
            }
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); MethodInfo found = null;
            for (int i = 0; i < methods.Length; i++) if (methods[i].Name == name) { if (found != null) throw new AmbiguousMatchException(type.FullName + "." + name); found = methods[i]; }
            if (found == null) throw new MissingMethodException(type.FullName, name); return found;
        }
        private static void Patch(Harmony harmony, MethodInfo original, MethodInfo prefix, MethodInfo postfix)
        { harmony.Patch(original, prefix == null ? null : new HarmonyMethod(prefix), postfix == null ? null : new HarmonyMethod(postfix)); }
    }
}
