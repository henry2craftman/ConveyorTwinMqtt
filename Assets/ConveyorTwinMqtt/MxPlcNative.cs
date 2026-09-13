using System.Runtime.InteropServices;

/// <summary>
/// MxComponentNativeDll P/Invoke
/// Y 읽기: PLC 출력 -> Unity | X 쓰기: Unity -> PLC 입력
/// </summary>
public static class MxPlcNative
{
    const string DLL = "MxComponentNativeDll";

    [DllImport(DLL)] public static extern int MxInit(
        int station,
        [MarshalAs(UnmanagedType.LPWStr)] string yDevice, int yCount,
        [MarshalAs(UnmanagedType.LPWStr)] string xDevice, int xCount,
        int intervalUs);
    [DllImport(DLL)] public static extern int  MxStart(int cpuCore);
    [DllImport(DLL)] public static extern void MxStop();
    [DllImport(DLL)] public static extern void MxDispose();

    // Y 읽기 (PLC 출력 -> Unity)
    [DllImport(DLL)] public static extern int   MxReadY([Out] short[] buf, int count, out long tsUs);
    [DllImport(DLL)] public static extern short MxReadYOne(int index);

    // X 쓰기 (Unity -> PLC 입력)
    [DllImport(DLL)] public static extern int MxWriteX([In] short[] data, int count);

    // Ring Buffer
    [DllImport(DLL)] public static extern int MxPopSnapshot([Out] short[] buf, int count, out long tsUs);
    [DllImport(DLL)] public static extern int MxGetSnapshotCount();

    // Diagnostics
    [DllImport(DLL)] public static extern int  MxGetStatus();
    [DllImport(DLL)] public static extern int  MxGetLastPlcError();
    [DllImport(DLL)] public static extern int  MxGetYCount();
    [DllImport(DLL)] public static extern int  MxGetXCount();
    [DllImport(DLL)] public static extern long MxGetPollCount();
    [DllImport(DLL)] public static extern void MxGetTimingStats(out long avg, out long min, out long max);
}
