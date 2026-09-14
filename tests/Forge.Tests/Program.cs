using System;
using System.Collections.Generic;
using System.Reflection;

namespace CsmForge.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CaseAttribute : Attribute { }

    public static class Assert
    {
        public static void True(bool condition)
        {
            if (!condition) throw new Exception("Assertion failed.");
        }
        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception("Expected " + expected + ", actual " + actual + ".");
        }
        public static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected exception " + typeof(T).Name + ".");
        }
    }

    public static class Program
    {
        public static int Main()
        {
            List<MethodInfo> tests = new List<MethodInfo>();
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    if (method.GetCustomAttributes(typeof(CaseAttribute), false).Length != 0)
                        tests.Add(method);
            tests.Sort(delegate(MethodInfo a, MethodInfo b)
            {
                return StringComparer.Ordinal.Compare(a.DeclaringType.FullName + "." + a.Name,
                    b.DeclaringType.FullName + "." + b.Name);
            });
            int failed = 0;
            foreach (MethodInfo method in tests)
            {
                string name = method.DeclaringType.Name + "." + method.Name;
                try
                {
                    method.Invoke(null, null);
                    Console.WriteLine("PASS " + name);
                }
                catch (Exception error)
                {
                    failed++;
                    Exception cause = error is TargetInvocationException ? error.InnerException : error;
                    Console.WriteLine("FAIL " + name + "\n" + cause);
                }
            }
            Console.WriteLine("RESULT tests=" + tests.Count + " passed=" + (tests.Count - failed) + " failed=" + failed);
            return failed == 0 && tests.Count != 0 ? 0 : 1;
        }
    }
}
