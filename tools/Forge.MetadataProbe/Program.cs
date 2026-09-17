using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: Forge.MetadataProbe <assembly> <type> [type...]");
            return 2;
        }
        string assemblyPath = Path.GetFullPath(args[0]);
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine("assembly-not-found: " + assemblyPath);
            return 3;
        }
        DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath));
        ReaderParameters parameters = new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false };
        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath, parameters))
        {
            for (int i = 1; i < args.Length; i++)
            {
                TypeDefinition type = FindType(assembly.MainModule, args[i]);
                if (type == null)
                {
                    Console.WriteLine("TYPE-MISSING " + args[i]);
                    continue;
                }
                Console.WriteLine("TYPE " + type.FullName);
                List<FieldDefinition> fields = new List<FieldDefinition>(type.Fields);
                fields.Sort(delegate(FieldDefinition a, FieldDefinition b) { return StringComparer.Ordinal.Compare(a.Name, b.Name); });
                foreach (FieldDefinition field in fields)
                    Console.WriteLine("FIELD " + field.Attributes + " " + field.FieldType.FullName + " " + field.Name);
                List<MethodDefinition> methods = new List<MethodDefinition>(type.Methods);
                methods.Sort(delegate(MethodDefinition a, MethodDefinition b)
                {
                    int byName = StringComparer.Ordinal.Compare(a.Name, b.Name);
                    return byName != 0 ? byName : a.Parameters.Count.CompareTo(b.Parameters.Count);
                });
                foreach (MethodDefinition method in methods)
                {
                    Console.Write("METHOD " + method.Attributes + " " + method.ReturnType.FullName + " " + method.Name + "(");
                    for (int p = 0; p < method.Parameters.Count; p++)
                    {
                        if (p != 0) Console.Write(", ");
                        ParameterDefinition parameter = method.Parameters[p];
                        Console.Write(parameter.ParameterType.FullName + " " + parameter.Name);
                    }
                    Console.WriteLine(")");
                }
            }
        }
        return 0;
    }

    private static TypeDefinition FindType(ModuleDefinition module, string name)
    {
        foreach (TypeDefinition type in module.Types)
        {
            TypeDefinition found = FindTypeRecursive(type, name);
            if (found != null) return found;
        }
        return null;
    }

    private static TypeDefinition FindTypeRecursive(TypeDefinition type, string name)
    {
        if (type.FullName == name || type.Name == name) return type;
        foreach (TypeDefinition nested in type.NestedTypes)
        {
            TypeDefinition found = FindTypeRecursive(nested, name);
            if (found != null) return found;
        }
        return null;
    }
}
