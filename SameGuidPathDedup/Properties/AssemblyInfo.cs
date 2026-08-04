// Custom assembly metadata. The .csproj sets GenerateAssemblyInfo=false so the
// SDK does not generate a default AssemblyInfo.cs; this file is the only source
// of the Emby-required plugin GUID.
using System.Reflection;

[assembly: Guid("58a3ade8-ca3f-4b2b-b036-a0ccb3d3f809")]

// Plugin description surfaced in the Emby Dashboard. Other fields (title, version)
// come from the .csproj <AssemblyVersion> / <Version> properties.