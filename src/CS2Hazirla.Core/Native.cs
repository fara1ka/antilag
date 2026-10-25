using System.Runtime.InteropServices;
using System.Text;

namespace CS2Hazirla.Core;

internal static class Native
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint action, uint param, int[] vparam, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint action, uint param, ref StickyKeys v, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint action, uint param, ref ToggleKeys v, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint action, uint param, ref FilterKeys v, uint winIni);

    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder exeName, ref int size);
    [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buf);

    // read-only: the size of the standby list, shown in the before/after measurement
    [DllImport("ntdll.dll")] public static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int ret);

    public const uint GW_OWNER = 4;
    public const uint WM_CLOSE = 0x0010;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_APPWINDOW = 0x40000;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint SPI_GETMOUSE = 3, SPI_SETMOUSE = 4;
    public const uint SPI_GETFILTERKEYS = 0x32, SPI_SETFILTERKEYS = 0x33, SPI_GETTOGGLEKEYS = 0x34, SPI_SETTOGGLEKEYS = 0x35,
                      SPI_GETSTICKYKEYS = 0x3A, SPI_SETSTICKYKEYS = 0x3B;

    [StructLayout(LayoutKind.Sequential)] public struct StickyKeys { public uint cbSize, dwFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct ToggleKeys { public uint cbSize, dwFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct FilterKeys { public uint cbSize, dwFlags, iWaitMSec, iDelayMSec, iRepeatMSec, iBounceMSec; }
    public const int SystemMemoryListInformation = 80;

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
}
