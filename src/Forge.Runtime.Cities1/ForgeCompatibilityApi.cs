using CsmForge.Core;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace CsmForge.Runtime.Cities1
{
    public enum ForgeModCompatibilityKind
    {
        ExactMatch = 0,
        ClientOnly = 1,
        ForgeSynchronized = 2,
        Blocked = 3
    }

    /// <summary>
    /// Third-party compatibility declaration surface. Declarations are local facts collected into
    /// the normal Forge compatibility manifest; they do not bypass Host policy or adapter checks.
    /// </summary>
    public static class ForgeCompatibilityApi
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ForgeModCompatibilityKind> ByAssembly =
            new Dictionary<string, ForgeModCompatibilityKind>(StringComparer.Ordinal);

        public static void Declare(Assembly assembly, ForgeModCompatibilityKind kind)
        {
            Check.NotNull(assembly, "assembly");
            Check.OutOfRange(!Enum.IsDefined(typeof(ForgeModCompatibilityKind), kind), "kind");
            string key = assembly.FullName;
            Check.Condition(string.IsNullOrEmpty(key), "assembly", "Assembly identity is unavailable.");
            lock (Gate)
            {
                if (RuntimeServices.Multiplayer.Status.Mode != MultiplayerSessionMode.Offline)
                    throw new InvalidOperationException("Mod compatibility declarations are frozen for the active multiplayer session.");
                ForgeModCompatibilityKind existing;
                if (ByAssembly.TryGetValue(key, out existing) && existing != kind)
                    throw new InvalidOperationException("Assembly already declared a different Forge compatibility kind.");
                ByAssembly[key] = kind;
            }
        }

        public static bool RemoveDeclaration(Assembly assembly)
        {
            if (assembly == null) return false;
            lock (Gate)
            {
                if (RuntimeServices.Multiplayer.Status.Mode != MultiplayerSessionMode.Offline) return false;
                return ByAssembly.Remove(assembly.FullName);
            }
        }

        internal static bool TryGet(Assembly assembly, out ForgeModCompatibilityKind kind)
        {
            kind = ForgeModCompatibilityKind.ExactMatch;
            if (assembly == null || string.IsNullOrEmpty(assembly.FullName)) return false;
            lock (Gate) return ByAssembly.TryGetValue(assembly.FullName, out kind);
        }
    }
}
