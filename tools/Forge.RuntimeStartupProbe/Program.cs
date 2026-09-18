using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class Program
{
    private const string AdapterTypeName = "CsmForge.Runtime.Cities1.DistrictParkDeepScalarAdapter";

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: Forge.RuntimeStartupProbe <runtime-dll> <cities-managed-directory>");
            return 2;
        }

        string runtimePath = Path.GetFullPath(args[0]);
        string managedPath = Path.GetFullPath(args[1]);
        string runtimeDirectory = Path.GetDirectoryName(runtimePath);

        AssemblyLoadContext.Default.Resolving += delegate(AssemblyLoadContext context, AssemblyName name)
        {
            string runtimeDependency = Path.Combine(runtimeDirectory, name.Name + ".dll");
            if (File.Exists(runtimeDependency))
                return context.LoadFromAssemblyPath(runtimeDependency);

            string gameDependency = Path.Combine(managedPath, name.Name + ".dll");
            return File.Exists(gameDependency) ? context.LoadFromAssemblyPath(gameDependency) : null;
        };

        try
        {
            Assembly runtime = AssemblyLoadContext.Default.LoadFromAssemblyPath(runtimePath);
            Type adapterType = runtime.GetType(AdapterTypeName, true);
            RuntimeHelpers.RunClassConstructor(adapterType.TypeHandle);
            Console.WriteLine("PASS: " + AdapterTypeName + " initialized against " + managedPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + AdapterTypeName + " initialization failed");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
