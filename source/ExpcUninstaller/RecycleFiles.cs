using System.Runtime.InteropServices;
namespace ExpcUninstaller;
internal static class RecycleFiles
{
    [DllImport("Expc.Native.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)] private static extern int RecycleOnly(string path);
    internal static void Move(string path)
    {
        int result = unchecked((int)0x80004005); Exception? error = null;
        var thread = new Thread(() => { try { result = RecycleOnly(path); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null || result != 0) throw new IOException(L.T("RecycleFailed") + " (0x" + result.ToString("X8") + ")", error);
    }
}
