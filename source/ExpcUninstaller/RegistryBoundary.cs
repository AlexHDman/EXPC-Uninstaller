using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
namespace ExpcUninstaller;
internal static class RegistryBoundary
{
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegOpenKeyEx(SafeRegistryHandle key, string subkey, int options, int access, out SafeRegistryHandle result);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegQueryValueEx(SafeRegistryHandle key, string value, IntPtr reserved, out int type, IntPtr data, ref int size);
    internal static void NoLinks(RegistryHive hive, RegistryView view, string path)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view); var current = "";
        foreach (var part in path.Split('\\'))
        {
            current = current.Length == 0 ? part : current + "\\" + part;
            var error = RegOpenKeyEx(root.Handle, current, 8, 1 | (int)view, out var handle); // REG_OPTION_OPEN_LINK
            using (handle)
            {
                if (error == 2) return;
                if (error != 0) throw new System.ComponentModel.Win32Exception(error);
                int size = 0; var query = RegQueryValueEx(handle, "SymbolicLinkValue", IntPtr.Zero, out var type, IntPtr.Zero, ref size);
                if (type == 6 || query is not (0 or 2 or 234)) throw new IOException(L.T("RegistryLinkBlocked"));
            }
        }
    }
}
