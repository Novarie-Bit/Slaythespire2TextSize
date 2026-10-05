// Rewrites the mod DLL's references to the game's assemblies (sts2, GodotSharp, 0Harmony) to
// version 0.0.0.0. .NET accepts any loaded version for a 0.0.0.0 reference, so the prebuilt
// DLL loads no matter which exact versions the installed game ships.
//
//   dotnet run --project ci/RetargetReferences -- <path to TextSizeSetting.dll>
using Mono.Cecil;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: RetargetReferences <assembly.dll>");
    return 1;
}

string[] gameAssemblies = ["sts2", "GodotSharp", "0Harmony"];
var path = args[0];
var bytes = File.ReadAllBytes(path);

using (var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes)))
{
    foreach (var reference in assembly.MainModule.AssemblyReferences)
    {
        if (!gameAssemblies.Contains(reference.Name))
            continue;

        Console.WriteLine($"{reference.Name}: {reference.Version} -> 0.0.0.0");
        reference.Version = new Version(0, 0, 0, 0);
    }

    assembly.Write(path);
}

return 0;
