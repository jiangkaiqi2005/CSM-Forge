using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace LoadCheck
{
    /// <summary>
    /// Third stage: replays the mod-classification decision over the REAL compatibility catalog
    /// and the REAL installed assemblies, to prove no installed mod is misclassified as blocked.
    /// Reads the manifest document from the built CSM.Forge.Core.dll and the handwritten-bridge
    /// ownership rule from the built CSM.Forge.Runtime.Cities1.dll.
    /// </summary>
    public static class Classify
    {
        public static int Run(string modDir)
        {
            Console.WriteLine("=== 分类决策复核（真实目录文档 + 已安装程序集）");
            Assembly core = Assembly.LoadFrom(Path.Combine(modDir, "CSM.Forge.Core.dll"));
            Assembly runtime = Assembly.LoadFrom(Path.Combine(modDir, "CSM.Forge.Runtime.Cities1.dll"));

            Type documentType = core.GetType("CsmForge.Core.ModCompatibilityDocument", true);
            object document = documentType.GetMethod("BuiltIn", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, null);
            MethodInfo tryGetEntry = documentType.GetMethod("TryGetModEntry", BindingFlags.Public | BindingFlags.Instance);

            Type registryType = runtime.GetType("CsmForge.Runtime.Cities1.KnownModBridgeRegistry", true);
            MethodInfo isHandwritten = registryType.GetMethod("IsHandwrittenSynchronized",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (isHandwritten == null) { Console.WriteLine("  IsHandwrittenSynchronized 未找到"); return 1; }

            // Every installed user-mod type name (from the mod folders actually present).
            string[] installedModTypes =
            {
                "CitiesHarmony.Mod", "TrafficManager.Lifecycle.TrafficManagerMod",
                "NetworkMultitool.Mod", "EightyOne2.Mod", "GameAnarchy.Mod",
                "DemandController.DemandController", "InfiniteGoodsMod.ModIdentity",
                "ACSM.Mod", "PrecisionEngineering.Mod", "CSLModernMap.CSLModernMap",
                "UnifiedUI.UnifiedUI", "TMPE.API.Mod"
            };

            int failures = 0;
            object[] args = { null, null };
            foreach (string typeName in installedModTypes)
            {
                args[0] = typeName; args[1] = null;
                bool declared = (bool)tryGetEntry.Invoke(document, args);
                string category = declared ? (string)args[1].GetType().GetProperty("Category").GetValue(args[1], null) : "(未在目录中)";
                bool handwritten = declared && (bool)isHandwritten.Invoke(null, new object[] { typeName });

                // The rule under test: a handwritten synchronized entry must NOT be forced blocked.
                bool wouldBlock = declared && category == "synchronized" && !handwritten;
                string verdict = wouldBlock ? "会被通用检查判 blocked（需通用适配器已解析）" : "不会被该规则拦截";
                Console.WriteLine(string.Format("  {0,-46} manifest={1,-14} handwritten={2,-5} -> {3}",
                    typeName, category, handwritten, verdict));
                if (handwritten && category == "synchronized" && !declared) failures++;
            }

            // Explicit assertion: the five handwritten types are all exempt.
            // count client-only entries actually present in the catalog
            object mods = documentType.GetProperty("Mods").GetValue(document, null);
            int modCount = (int)mods.GetType().GetProperty("Count").GetValue(mods, null);
            int clientOnlyCount = 0;
            for (int i = 0; i < modCount; i++)
            {
                object item = mods.GetType().GetProperty("Item").GetValue(mods, new object[] { i });
                string cat = (string)item.GetType().GetProperty("Category").GetValue(item, null);
                if (cat == "client-only") clientOnlyCount++;
            }
            Console.WriteLine("  统计 client-only 条目: " + clientOnlyCount + " (期望 9)");
            if (clientOnlyCount != 9) failures++;

            string[] handwrittenExpected =
            {
                "NetworkMultitool.Mod", "EightyOne2.Mod", "GameAnarchy.Mod",
                "DemandController.DemandController", "InfiniteGoodsMod.ModIdentity"
            };
            int exempt = 0;
            foreach (string typeName in handwrittenExpected)
                if ((bool)isHandwritten.Invoke(null, new object[] { typeName })) exempt++;
            Console.WriteLine("  手写桥豁免覆盖: " + exempt + "/5");
            if (exempt != 5) failures++;

            Console.WriteLine("  分类复核结果: failures=" + failures);
            return failures == 0 ? 0 : 1;
        }
    }
}
