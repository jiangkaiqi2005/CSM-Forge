using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class Program
{
    private const string AdapterTypeName = "CsmForge.Runtime.Cities1.DistrictParkDeepScalarAdapter";
    private const string MultiplayerUiTypeName = "CsmForge.Runtime.Cities1.ForgeMultiplayerUi";

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
            ProbeInvitationCodec(runtime);
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

    private static void ProbeInvitationCodec(Assembly runtime)
    {
        Type ui = runtime.GetType(MultiplayerUiTypeName, true);
        BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        MethodInfo build = ui.GetMethod("BuildInviteCode", flags);
        MethodInfo parse = ui.GetMethod("TryParseInviteCode", flags);
        if (build == null || parse == null) throw new MissingMethodException(MultiplayerUiTypeName, "invitation codec");
        string encoded = (string)build.Invoke(null, new object[] { "192.0.2.10", 4230, "probe-key" });
        object[] args = new object[] { encoded, null, null };
        if (!(bool)parse.Invoke(null, args)) throw new InvalidOperationException("Forge LAN invitation did not round-trip.");
        IPEndPoint endpoint = args[1] as IPEndPoint;
        if (endpoint == null || endpoint.Address.ToString() != "192.0.2.10" || endpoint.Port != 4230 ||
            (string)args[2] != "probe-key") throw new InvalidOperationException("Forge LAN invitation changed values.");
    }
}
