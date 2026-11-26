using System.Runtime.InteropServices;

// Native DLLs are only ever loaded from System32, never from the program's own folder (blocks DLL planting).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
