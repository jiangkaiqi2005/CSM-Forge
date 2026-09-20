using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LoadCheck
{
    /// <summary>
    /// Loads the built mod assemblies against the game's own managed assemblies and forces full
    /// resolution of every type, member signature and custom attribute. This catches the class of
    /// failure that only appears when the game loads the mod (TypeLoadException,
    /// MissingMethodException, missing referenced assembly) without launching Unity.
    ///
    ///   loadcheck &lt;modDir&gt; &lt;gameManagedDir&gt;
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length < 2) { Console.Error.WriteLine("usage: loadcheck <modDir> <gameManagedDir> [extraDir;extraDir]"); return 2; }
            string modDir = args[0], managedDir = args[1];
            if (!Directory.Exists(modDir) || !Directory.Exists(managedDir))
            { Console.Error.WriteLine("mod dir or managed dir missing"); return 2; }

            // Extra runtime-provided dependencies (Harmony is shipped by the CitiesHarmony mod).
            string[] extraDirs = args.Length > 2 ? args[2].Split(';') : new string[0];

            string[] modAssemblies =
            {
                "CSM.Forge.Core.dll", "CSM.Forge.Protocol.dll", "CSM.Forge.Checkpoints.dll",
                "CSM.Forge.Transport.LiteNet.dll", "CSM.Forge.Runtime.Cities1.dll"
            };

            // Resolution universe: the game's managed assemblies plus the mod's own.
            var universe = new List<string>();
            universe.AddRange(Directory.GetFiles(managedDir, "*.dll"));
            universe.AddRange(Directory.GetFiles(modDir, "*.dll"));
            // Fall back to the desktop runtime only for assemblies the game does not ship.
            string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (runtimeDir != null) universe.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));
            foreach (string extra in extraDirs)
                if (Directory.Exists(extra)) universe.AddRange(Directory.GetFiles(extra, "*.dll"));

            int failures = 0, totalTypes = 0, totalAttributes = 0;
            foreach (string name in modAssemblies)
            {
                string path = Path.Combine(modDir, name);
                if (!File.Exists(path)) { Console.WriteLine("MISSING  " + name); failures++; continue; }
                Console.WriteLine("=== " + name);

                using (var context = new MetadataLoadContext(new PathAssemblyResolver(universe.Distinct()), "mscorlib"))
                {
                    Assembly assembly;
                    try { assembly = context.LoadFromAssemblyPath(path); }
                    catch (Exception error) { Console.WriteLine("  LOAD FAILED: " + error.GetType().Name + ": " + error.Message); failures++; continue; }

                    // referenced assemblies must all resolve
                    foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                    {
                        try
                        {
                            Assembly resolved = context.LoadFromAssemblyName(reference);
                            if (resolved == null) { Console.WriteLine("  UNRESOLVED REFERENCE: " + reference.Name); failures++; }
                        }
                        catch (Exception error)
                        {
                            Console.WriteLine("  UNRESOLVED REFERENCE: " + reference.Name + " (" + error.GetType().Name + ")");
                            failures++;
                        }
                    }

                    Type[] types;
                    try { types = assembly.GetTypes(); }
                    catch (ReflectionTypeLoadException error)
                    {
                        types = error.Types.Where(t => t != null).ToArray();
                        foreach (Exception loader in error.LoaderExceptions.Where(e => e != null).Take(10))
                            Console.WriteLine("  TYPE LOAD ERROR: " + loader.Message);
                        failures++;
                    }
                    totalTypes += types.Length;

                    foreach (Type type in types)
                    {
                        try
                        {
                            ForceResolve(type, ref totalAttributes);
                        }
                        catch (Exception error)
                        {
                            Console.WriteLine("  RESOLUTION FAILED " + type.FullName + ": " + error.GetType().Name + ": " + error.Message);
                            failures++;
                        }
                    }
                    Console.WriteLine("  types=" + types.Length + " references=" + assembly.GetReferencedAssemblies().Length + " OK");
                }
            }

            Console.WriteLine();
            Console.WriteLine("RESULT assemblies=" + modAssemblies.Length + " types=" + totalTypes +
                " attributes=" + totalAttributes + " failures=" + failures);
            Console.WriteLine();
            int executeFailures = Execute.Run(modDir);
            return (failures == 0 && executeFailures == 0) ? 0 : 1;
        }

        /// <summary>Touch every signature and attribute so the runtime must resolve it.</summary>
        private static void ForceResolve(Type type, ref int attributeCount)
        {
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly;

            attributeCount += CountAttributes(type.CustomAttributes);
            Type baseType = type.BaseType;
            foreach (Type iface in type.GetInterfaces()) { _ = iface.FullName; }

            foreach (FieldInfo field in type.GetFields(All))
            {
                _ = field.FieldType.FullName;
                attributeCount += CountAttributes(field.CustomAttributes);
            }
            foreach (PropertyInfo property in type.GetProperties(All))
            {
                _ = property.PropertyType.FullName;
                if (property.CanRead && property.GetGetMethod(true) != null) _ = property.GetGetMethod(true).ReturnType.FullName;
                attributeCount += CountAttributes(property.CustomAttributes);
            }
            foreach (MethodInfo method in type.GetMethods(All))
            {
                _ = method.ReturnType.FullName;
                foreach (ParameterInfo parameter in method.GetParameters()) _ = parameter.ParameterType.FullName;
                attributeCount += CountAttributes(method.CustomAttributes);
            }
            foreach (ConstructorInfo ctor in type.GetConstructors(All))
            {
                foreach (ParameterInfo parameter in ctor.GetParameters()) _ = parameter.ParameterType.FullName;
                attributeCount += CountAttributes(ctor.CustomAttributes);
            }
        }

        private static int CountAttributes(IEnumerable<CustomAttributeData> attributes)
        {
            int count = 0;
            foreach (CustomAttributeData attribute in attributes)
            {
                _ = attribute.AttributeType.FullName;
                _ = attribute.Constructor;
                foreach (CustomAttributeTypedArgument argument in attribute.ConstructorArguments)
                {
                    _ = argument.ArgumentType.FullName;
                    // typeof(...) arguments reference game types - this is what Harmony patches do
                    var referenced = argument.Value as Type;
                    if (referenced != null) _ = referenced.FullName;
                    var collection = argument.Value as IReadOnlyCollection<CustomAttributeTypedArgument>;
                    if (collection != null)
                        foreach (CustomAttributeTypedArgument item in collection)
                        {
                            _ = item.ArgumentType.FullName;
                            var element = item.Value as Type;
                            if (element != null) _ = element.FullName;
                        }
                }
                foreach (CustomAttributeNamedArgument named in attribute.NamedArguments)
                {
                    _ = named.TypedValue.ArgumentType.FullName;
                    var namedType = named.TypedValue.Value as Type;
                    if (namedType != null) _ = namedType.FullName;
                }
                count++;
            }
            return count;
        }
    }
}
