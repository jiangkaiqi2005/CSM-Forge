using System;
using System.IO;
using System.Reflection;

namespace LoadCheck
{
    /// <summary>
    /// Second stage: actually execute IL from the built CSM.Forge.Core.dll (not just resolve
    /// metadata). Loads the installed assembly, constructs real objects, runs the sharded
    /// district index and the compatibility catalog path, and reports observable results.
    /// Uses the installed artifact, so it validates what the game will load.
    /// </summary>
    public static class Execute
    {
        public static int Run(string modDir)
        {
            string corePath = Path.Combine(modDir, "CSM.Forge.Core.dll");
            Console.WriteLine("=== 执行 IL: " + Path.GetFileName(corePath));
            Assembly core;
            try { core = Assembly.LoadFrom(corePath); }
            catch (Exception error)
            {
                Console.WriteLine("  程序集加载失败: " + error.GetType().Name + ": " + error.Message);
                return 1;
            }
            Console.WriteLine("  已加载: " + core.FullName);
            Console.WriteLine("  位置: " + SafeLocation(core));

            int failures = 0;
            failures += Invoke(core, "CsmForge.Core.Hash256", "Compute",
                new[] { typeof(byte[]) }, new object[] { new byte[] { 1, 2, 3 } }, "Hash256.Compute");
            failures += Construct(core, "CsmForge.Core.ModCompatibilityCatalog", "Default",
                "ModCompatibilityCatalog.Default");
            failures += ConstructParameterless(core, "CsmForge.Core.DistrictShardedCellIndex",
                "DistrictShardedCellIndex()");
            failures += Construct(core, "CsmForge.Core.VerificationCadence", null,
                "VerificationCadence(0, 5000)");
            failures += ExerciseCatalog(core);
            failures += ExerciseShardedIndex(core);

            Console.WriteLine("  执行结果: failures=" + failures);
            return failures == 0 ? 0 : 1;
        }

        private static int ExerciseCatalog(Assembly core)
        {
            try
            {
                Type documentType = core.GetType("CsmForge.Core.ModCompatibilityDocument", true);
                object builtIn = documentType.GetMethod("BuiltIn", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, null);
                object mods = documentType.GetProperty("Mods").GetValue(builtIn, null);
                int count = (int)mods.GetType().GetProperty("Count").GetValue(mods, null);
                object clientOnly = documentType.GetProperty("ClientOnlyModTypes").GetValue(builtIn, null);
                object blocklist = documentType.GetProperty("GameAnarchy").GetValue(builtIn, null);
                object unsupported = blocklist.GetType().GetProperty("UnsupportedBooleanSettings").GetValue(blocklist, null);
                int blocked = (int)unsupported.GetType().GetProperty("Length").GetValue(unsupported, null);
                Console.WriteLine("  ModCompatibilityDocument.BuiltIn: mods=" + count +
                    " clientOnly=" + ((Array)clientOnly).Length + " GA-blocked=" + blocked);
                return (count > 0 && blocked == 29) ? 0 : 1;
            }
            catch (Exception error)
            {
                Console.WriteLine("  BuiltIn 执行失败: " + Unwrap(error));
                return 1;
            }
        }

        private static int ExerciseShardedIndex(Assembly core)
        {
            try
            {
                Type indexType = core.GetType("CsmForge.Core.DistrictShardedCellIndex", true);
                object index = Activator.CreateInstance(indexType);
                MethodInfo apply = indexType.GetMethod("ApplyCell", BindingFlags.Public | BindingFlags.Instance);
                Type cellType = core.GetType("CsmForge.Core.DistrictCellStateV2", true);
                ConstructorInfo cellCtor = cellType.GetConstructor(new[]
                {
                    typeof(uint), core.GetType("CsmForge.Core.EntityIdentityV2"), typeof(byte),
                    core.GetType("CsmForge.Core.EntityIdentityV2"), typeof(byte),
                    core.GetType("CsmForge.Core.EntityIdentityV2"), typeof(byte),
                    core.GetType("CsmForge.Core.EntityIdentityV2"), typeof(byte)
                });
                Type identityType = core.GetType("CsmForge.Core.EntityIdentityV2", true);
                ConstructorInfo identityCtor = identityType.GetConstructor(new[] { typeof(ulong), typeof(uint) });
                object identity = identityCtor.Invoke(new object[] { 7UL, 1u });
                object empty = Activator.CreateInstance(identityType); // default identity
                object cell = cellCtor.Invoke(new object[] { 100u, identity, (byte)255, empty, (byte)0, empty, (byte)0, empty, (byte)0 });
                apply.Invoke(index, new[] { (object)100u, cell });
                object root = indexType.GetProperty("AggregateRoot").GetValue(index, null);
                object recomputed = indexType.GetMethod("RecomputeFullAggregateRoot", BindingFlags.Public | BindingFlags.Instance)
                    .Invoke(index, null);
                int count = (int)indexType.GetProperty("CellCount").GetValue(index, null);
                bool coherent = root.ToString() == recomputed.ToString();
                Console.WriteLine("  分片索引: cellCount=" + count + " 根一致性=" + coherent +
                    " root=" + root.ToString().Substring(0, 16) + "…");
                return (count == 1 && coherent) ? 0 : 1;
            }
            catch (Exception error)
            {
                Exception inner = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                Console.WriteLine("  分片索引执行失败: " + inner.GetType().Name + ": " + inner.Message);
                Console.WriteLine("  堆栈: " + inner.StackTrace);
                return 1;
            }
        }

        private static int Construct(Assembly assembly, string typeName, string propertyName, string label)
        {
            try
            {
                Type type = assembly.GetType(typeName, true);
                object value = propertyName == null
                    ? Activator.CreateInstance(type, new object[] { 0L, 5000L })
                    : type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
                Console.WriteLine("  " + label + " -> " + (value == null ? "(null)" : value.GetType().Name));
                return 0;
            }
            catch (Exception error)
            {
                Console.WriteLine("  " + label + " 失败: " + Unwrap(error));
                return 1;
            }
        }

        private static int ConstructParameterless(Assembly assembly, string typeName, string label)
        {
            try
            {
                Type type = assembly.GetType(typeName, true);
                object value = Activator.CreateInstance(type);
                Console.WriteLine("  " + label + " -> " + (value == null ? "(null)" : value.GetType().Name));
                return 0;
            }
            catch (Exception error)
            {
                Console.WriteLine("  " + label + " 失败: " + Unwrap(error));
                return 1;
            }
        }

        private static int Invoke(Assembly assembly, string typeName, string methodName,
            Type[] parameterTypes, object[] arguments, string label)
        {
            try
            {
                Type type = assembly.GetType(typeName, true);
                object result = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null)
                    .Invoke(null, arguments);
                Console.WriteLine("  " + label + " -> " + result);
                return 0;
            }
            catch (Exception error)
            {
                Console.WriteLine("  " + label + " 失败: " + Unwrap(error));
                return 1;
            }
        }

        private static string Unwrap(Exception error)
        {
            Exception inner = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
            return inner.GetType().Name + ": " + inner.Message;
        }

        private static string SafeLocation(Assembly assembly)
        {
            try { return assembly.Location; } catch { return "(unavailable)"; }
        }
    }
}
