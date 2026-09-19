using System;
using System.Reflection;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// WP-3.2 (D2 dedup): reflection-surface guards shared by the known-mod bridges. The
    /// exception types and arguments match the per-bridge copies they replace, so bridge
    /// availability gating behavior is unchanged.
    /// </summary>
    internal static class BridgeSurfaceValidator
    {
        public static PropertyInfo RequiredProperty(PropertyInfo[] properties, string ownerName, string name)
        {
            for (int i = 0; i < properties.Length; i++)
                if (properties[i].Name == name) return properties[i];
            throw new MissingMemberException(ownerName, name);
        }

        public static long RequiredValue(PropertyInfo[] properties, long[] values, string ownerName, string name)
        {
            for (int i = 0; i < properties.Length; i++)
                if (properties[i].Name == name) return values[i];
            throw new MissingMemberException(ownerName, name);
        }

        public static MethodInfo RequiredMethod(Type type, string name, Type[] parameters, bool requireVoid)
        {
            if (type == null) throw new TypeLoadException(name);
            MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, parameters, null);
            if (method == null || (requireVoid && method.ReturnType != typeof(void)))
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        public static Type RequiredType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null) return type;
            }
            throw new TypeLoadException(fullName);
        }
    }
}
