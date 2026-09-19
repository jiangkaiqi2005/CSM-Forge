using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CsmForge.Core;

namespace ModScan
{
    /// <summary>
    /// WP-3.1 evidence tool: runs the client-only heuristic over a local workshop mod set and
    /// diffs it against the hardcoded ClientOnlyModTypes allowlist. Usage:
    ///   modscan &lt;workshopRoot&gt; &lt;gameManagedDir&gt;
    /// Exit output is the diff report; redirect to a file to archive as evidence.
    /// </summary>
    public static class Program
    {
        private static readonly string[] HardcodedClientOnlyModTypes = ModCompatibilityCatalog.ClientOnlyModTypes;

        public static int Main(string[] args)
        {
            if (args.Length != 2) { Console.Error.WriteLine("usage: modscan <workshopRoot> <gameManagedDir>"); return 2; }
            string workshopRoot = args[0];
            string managedDir = args[1];
            if (!Directory.Exists(workshopRoot) || !Directory.Exists(managedDir))
            { Console.Error.WriteLine("workshop root or managed dir missing"); return 2; }

            int scanned = 0, unscannable = 0, clientByHeuristic = 0, diffAgainstHardcoded = 0;
            var lines = new List<string>();
            lines.Add("# WP-3.1 client-only heuristic diff report");
            lines.Add("# format: workshopId | dll | hardcoded-client? | heuristic | simulation-surface sample");
            foreach (string modDir in Directory.EnumerateDirectories(workshopRoot).OrderBy(p => p, StringComparer.Ordinal))
            {
                foreach (string dll in Directory.EnumerateFiles(modDir, "*.dll").OrderBy(p => p, StringComparer.Ordinal))
                {
                    scanned++;
                    string verdict;
                    List<string> simSample;
                    string userModType;
                    try
                    {
                        using (var context = CreateContext(dll, managedDir, modDir))
                        {
                            Assembly assembly = context.LoadFromAssemblyPath(dll);
                            List<string> names = CollectTypeNames(assembly, out userModType);
                            bool touches = SimulationSurfaceMatcher.TouchesSimulationSurface(names);
                            verdict = touches ? "sim" : "client";
                            simSample = names.Where(IsSimulationName).Distinct().OrderBy(n => n, StringComparer.Ordinal).Take(3).ToList();
                        }
                    }
                    catch (Exception error)
                    {
                        unscannable++;
                        lines.Add(Path.GetFileName(Path.GetDirectoryName(dll)) + " | " + Path.GetFileName(dll) +
                            " | ? | unscannable | " + error.GetType().Name);
                        continue;
                    }
                    bool hardcoded = userModType != null && HardcodedClientOnlyModTypes.Contains(userModType);
                    if (verdict == "client") clientByHeuristic++;
                    if (hardcoded != (verdict == "client")) diffAgainstHardcoded++;
                    lines.Add(Path.GetFileName(Path.GetDirectoryName(dll)) + " | " + Path.GetFileName(dll) +
                        " | " + (userModType == null ? "?" : (hardcoded ? "client" : "exact")) +
                        " | " + verdict + " | " + string.Join(",", simSample));
                }
            }
            lines.Add($"# scanned={scanned} unscannable={unscannable} clientByHeuristic={clientByHeuristic} diffAgainstHardcoded={diffAgainstHardcoded}");
            foreach (string line in lines) Console.WriteLine(line);
            return 0;
        }

        private static bool IsSimulationName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (string suffix in new[] { "Manager", "Tool", "Simulation", "AI" })
                if (name.EndsWith(suffix, StringComparison.Ordinal)) return true;
            return false;
        }

        private static MetadataLoadContext CreateContext(string dll, string managedDir, string modDir)
        {
            var paths = new List<string>();
            foreach (string file in Directory.EnumerateFiles(managedDir, "*.dll")) paths.Add(file);
            foreach (string file in Directory.EnumerateFiles(modDir, "*.dll")) if (file != dll) paths.Add(file);
            paths.Add(dll);
            var resolver = new PathAssemblyResolver(paths);
            return new MetadataLoadContext(resolver, coreAssemblyName: "mscorlib");
        }

        private static List<string> CollectTypeNames(Assembly assembly, out string userModType)
        {
            userModType = null;
            var names = new List<string>();
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException error) { types = error.Types; }
            if (types == null) return names;
            foreach (Type type in types)
            {
                if (type == null) continue;
                if (type.BaseType != null) names.Add(type.BaseType.Name);
                foreach (Type iface in type.GetInterfaces())
                {
                    names.Add(iface.Name);
                    if (iface.FullName == "ICities.IUserMod" && userModType == null) userModType = type.FullName;
                }
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (FieldInfo field in type.GetFields(all | BindingFlags.DeclaredOnly)) names.Add(field.FieldType.Name);
                foreach (PropertyInfo property in type.GetProperties(all | BindingFlags.DeclaredOnly)) names.Add(property.PropertyType.Name);
                foreach (MethodInfo method in type.GetMethods(all | BindingFlags.DeclaredOnly))
                {
                    names.Add(method.ReturnType.Name);
                    foreach (ParameterInfo parameter in method.GetParameters()) names.Add(parameter.ParameterType.Name);
                }
            }
            return names;
        }
    }
}
