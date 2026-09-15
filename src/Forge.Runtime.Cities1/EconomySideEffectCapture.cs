using System;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class EconomySideEffectScope : IDisposable
    {
        private bool disposed;
        internal int ConstructionFetched;
        internal int RefundAdded;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            EconomySideEffectCapture.End(this);
        }
    }

    internal static class EconomySideEffectCapture
    {
        [ThreadStatic] private static EconomySideEffectScope current;

        public static EconomySideEffectScope Begin()
        {
            if (current != null) throw new InvalidOperationException("Nested economy side-effect capture is not permitted.");
            current = new EconomySideEffectScope();
            return current;
        }

        internal static void End(EconomySideEffectScope scope)
        {
            if (!object.ReferenceEquals(current, scope)) throw new InvalidOperationException("Economy side-effect capture ownership mismatch.");
            current = null;
        }

        internal static void RecordFetch(EconomyManager.Resource resource, int fetched)
        {
            EconomySideEffectScope scope = current;
            if (scope == null || fetched <= 0) return;
            if (resource == EconomyManager.Resource.Construction)
                scope.ConstructionFetched = checked(scope.ConstructionFetched + fetched);
        }

        internal static void RecordAdd(EconomyManager.Resource resource, int amount)
        {
            EconomySideEffectScope scope = current;
            if (scope == null || amount <= 0) return;
            if (resource == EconomyManager.Resource.RefundAmount)
                scope.RefundAdded = checked(scope.RefundAdded + amount);
        }
    }

    [HarmonyPatch(typeof(EconomyManager), "FetchResource", new Type[] { typeof(EconomyManager.Resource), typeof(int), typeof(ItemClass) })]
    internal static class ForgeEconomyFetchCapturePatch
    {
        public static void Postfix(EconomyManager.Resource __0, int __result)
        {
            EconomySideEffectCapture.RecordFetch(__0, __result);
        }
    }

    [HarmonyPatch(typeof(EconomyManager), "AddResource", new Type[] { typeof(EconomyManager.Resource), typeof(int), typeof(ItemClass) })]
    internal static class ForgeEconomyAddCapturePatch
    {
        public static void Postfix(EconomyManager.Resource __0, int __1)
        {
            EconomySideEffectCapture.RecordAdd(__0, __1);
        }
    }
}
