using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace ExpcUninstaller;

// Local namespace isolates Windows sessions; SID isolates users. No path/version in identity.
internal sealed class InstanceSession : IDisposable
{
    internal static string Prefix => @"Local\EXPCdelete-" + WindowsIdentity.GetCurrent().User!.Value;
    private IntPtr mutex, activation;
    internal bool Primary { get; }
    internal InstanceSession(string? testIdentity = null)
    {
        var name = testIdentity ?? Prefix;
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor("D:P(A;;GA;;;" + sid + ")S:(ML;;NW;;;LW)", 1, out var descriptor, out _))
            throw new Win32Exception();
        try
        {
            var sa = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            activation = CreateEvent(ref sa, false, false, name + "-activate");
            mutex = CreateMutex(ref sa, false, name + "-app");
            if (mutex == IntPtr.Zero || activation == IntPtr.Zero) throw new Win32Exception();
            var result = WaitForSingleObject(mutex, 0);
            Primary = result is 0 or 0x80;
            if (!Primary)
            {
                if (result != 0x102) throw new Win32Exception();
                AllowSetForegroundWindow(uint.MaxValue);
                SetEvent(activation);
            }
        }
        finally { LocalFree(descriptor); }
    }
    internal bool PollActivation() => WaitForSingleObject(activation, 0) == 0;
    public void Dispose()
    {
        if (mutex != IntPtr.Zero) { if (Primary) ReleaseMutex(mutex); CloseHandle(mutex); mutex = IntPtr.Zero; }
        if (activation != IntPtr.Zero) { CloseHandle(activation); activation = IntPtr.Zero; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateMutex(ref SecurityAttributes attributes, bool initialOwner, string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateEvent(ref SecurityAttributes attributes, bool manualReset, bool initialState, string name);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool ReleaseMutex(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool SetEvent(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
}
