# CS1 runtime probe

This optional project is the first ICities entry point, **not the multiplayer game adapter**. It reports the loading callback, managed/Unity versions and the presence of an engine method. It does not patch simulation, host a room, connect, modify a city or write a save. A discovered method does not prove a safe patch boundary.

Build with your legally installed game references:

```powershell
dotnet build src/Forge.Runtime.Cities1/Forge.Runtime.Cities1.csproj -c Release -p:CitiesManagedPath="C:/Program Files (x86)/Steam/steamapps/common/Cities_Skylines/Cities_Data/Managed"
```

The build never installs a mod. This project is excluded from public kernel CI because game assemblies are not redistributed. It has not been compiled against game DLLs or tested inside CS1 in this implementation session. Do not describe it as a tested or playable build.

A future opt-in smoke test may copy only the produced Forge runtime/core DLLs into a separate local development mod folder, using a disposable city. Never overwrite game DLLs or combine this with the old CSM assembly. Record actual game build, DLC, loaded mods, probe output and loading/unloading behavior before adding Harmony patches.
