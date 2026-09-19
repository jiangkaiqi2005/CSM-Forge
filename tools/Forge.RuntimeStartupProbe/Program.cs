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
            ProbeDistrictBrushHarmonyBinding(runtime, managedPath);
            ProbeBulldozeGameSurface(managedPath);
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

    private static void ProbeDistrictBrushHarmonyBinding(Assembly runtime, string managedPath)
    {
        Assembly game = null;
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            if (loaded.GetName().Name == "Assembly-CSharp") { game = loaded; break; }
        if (game == null)
            game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managedPath, "Assembly-CSharp.dll"));

        Type districtTool = game.GetType("DistrictTool", true);
        MethodInfo original = null;
        foreach (MethodInfo candidate in districtTool.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            if (candidate.Name == "ApplyBrush" && candidate.GetParameters().Length == 6) { original = candidate; break; }
        if (original == null) throw new MissingMethodException("DistrictTool", "ApplyBrush");

        Type patch = runtime.GetType("CsmForge.Runtime.Cities1.DistrictToolApplyBrushAuthorityPatch", true);
        MethodInfo prefix = patch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (prefix == null) throw new MissingMethodException(patch.FullName, "Prefix");
        ParameterInfo[] gameParameters = original.GetParameters();
        ParameterInfo[] prefixParameters = prefix.GetParameters();
        if (prefixParameters.Length < gameParameters.Length)
            throw new InvalidOperationException("District brush Prefix omits game parameters.");
        for (int i = 0; i < gameParameters.Length; i++)
            if (gameParameters[i].Name != prefixParameters[i].Name || gameParameters[i].ParameterType != prefixParameters[i].ParameterType)
                throw new InvalidOperationException("District brush Harmony parameter mismatch at index " + i +
                    ": game=" + gameParameters[i].Name + ", prefix=" + prefixParameters[i].Name + ".");
    }

    private static void ProbeBulldozeGameSurface(string managedPath)
    {
        Assembly game = null;
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            if (loaded.GetName().Name == "Assembly-CSharp") { game = loaded; break; }
        if (game == null)
            game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managedPath, "Assembly-CSharp.dll"));

        Type bulldozeTool = game.GetType("BulldozeTool", true);
        MethodInfo[] methods = bulldozeTool.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        string[] required = { "DeleteSegment", "DeleteNode" };
        int[] parameterCounts = { 2, 1 };
        for (int nameIndex = 0; nameIndex < required.Length; nameIndex++)
        {
            bool found = false;
            for (int i = 0; i < methods.Length; i++)
            {
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (methods[i].Name != required[nameIndex] || parameters.Length != parameterCounts[nameIndex] ||
                    !typeof(System.Collections.IEnumerator).IsAssignableFrom(methods[i].ReturnType)) continue;
                found = true;
                for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                    if (parameters[parameterIndex].ParameterType != typeof(ushort)) found = false;
            }
            if (!found)
            {
                string candidates = "";
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].Name.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (candidates.Length != 0) candidates += "; ";
                    candidates += FormatMethod(methods[i]);
                }
                throw new MissingMethodException("BulldozeTool." + required[nameIndex] +
                    " coroutine has an unexpected signature. Real delete methods: " + candidates);
            }
        }
    }

    private static string FormatMethod(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        string result = method.ReturnType.FullName + " " + method.Name + "(";
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i != 0) result += ", ";
            result += parameters[i].ParameterType.FullName + " " + parameters[i].Name;
        }
        return result + ")";
    }

}
