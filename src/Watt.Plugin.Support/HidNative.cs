using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Watt.Plugins.Support
{
    /// <summary>One HID top-level collection exposed by SetupAPI.</summary>
    public sealed class HidInterfaceInfo
    {
        public string DevicePath;
        public string ProductName;
        public ushort VendorId;
        public ushort ProductId;
        public ushort UsagePage;
        public ushort Usage;
        public int InputReportByteLength;
        public int OutputReportByteLength;
        public int FeatureReportByteLength;
    }

    /// <summary>
    /// Minimal SetupAPI + HIDClass interop used to find and open HID report collections
    /// (vendor pages, diagnostics, or third-party plugins). Win32 HID is chosen over the WinRT
    /// HID API because it lets us control share mode, report lengths and read timeouts.
    /// </summary>
    public static class HidNative
    {
        private const int MaxInterfaces = 8;
        public const uint GenericRead = 0x80000000;
        public const uint GenericWrite = 0x40000000;
        public const uint FileShareRead = 0x00000001;
        public const uint FileShareWrite = 0x00000002;
        public const uint OpenExisting = 3;

        private static readonly Guid HidInterfaceClassGuid =
            new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;
        private const int HidpStatusSuccess = 0x00110000;
        private const int ErrorIoPending = 997;

        /// <summary>
        /// Diagnostic helper: every present HID top-level collection (keyboards, mice, receivers,
        /// vendor pages). Used by --diagnose to explain which devices the app can see.
        /// </summary>
        public static List<HidInterfaceInfo> EnumerateAllPresentCollections()
        {
            return EnumerateCollections(null, 128);
        }

        private static List<HidInterfaceInfo> EnumerateCollections(Func<HidInterfaceInfo, bool> accept,
            int maxCollections)
        {
            var result = new List<HidInterfaceInfo>(4);
            var deviceInfoSet = IntPtr.Zero;

            try
            {
                var interfaceClassGuid = HidInterfaceClassGuid;
                deviceInfoSet = SetupDiGetClassDevs(ref interfaceClassGuid, IntPtr.Zero, IntPtr.Zero,
                    DigcfPresent | DigcfDeviceInterface);
                if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1)) return result;

                var interfaceData = new SpDeviceInterfaceData();
                interfaceData.Size = (uint)Marshal.SizeOf(typeof(SpDeviceInterfaceData));

                for (uint index = 0; index < 256 && result.Count < maxCollections; index++)
                {
                    if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref interfaceClassGuid,
                            index, ref interfaceData))
                        break;

                    string path;
                    try
                    {
                        path = GetDeviceInterfacePath(deviceInfoSet, ref interfaceData);
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(path)) continue;

                    HidInterfaceInfo info = null;
                    try
                    {
                        info = InspectDevice(path);
                    }
                    catch
                    {
                        // keep enumerating other collections
                    }

                    if (info == null) continue;
                    if (accept != null && !accept(info)) continue;

                    result.Add(info);
                }
            }
            catch
            {
                // ignore; caller treats the empty list as "no HID devices"
            }
            finally
            {
                if (deviceInfoSet != IntPtr.Zero && deviceInfoSet != new IntPtr(-1))
                {
                    try
                    {
                        SetupDiDestroyDeviceInfoList(deviceInfoSet);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            return result;
        }

        public static IntPtr OpenShared(string devicePath)
        {
            return Open(devicePath, 0);
        }

        /// <summary>Opens a HID collection with FILE_FLAG_OVERLAPPED for timed input reads (diagnostics).</summary>
        public static IntPtr OpenSharedOverlapped(string devicePath)
        {
            return Open(devicePath, FileFlagOverlapped);
        }

        /// <summary>Read-only overlapped open, used by passive listeners that must not write.</summary>
        public static IntPtr OpenReadOverlapped(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return IntPtr.Zero;
            try
            {
                var handle = CreateFile(devicePath, GenericRead, FileShareRead | FileShareWrite,
                    IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
                return handle == new IntPtr(-1) ? IntPtr.Zero : handle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private const uint FileFlagOverlapped = 0x40000000;

        private static IntPtr Open(string devicePath, uint extraFlags)
        {
            if (string.IsNullOrEmpty(devicePath)) return IntPtr.Zero;
            try
            {
                var handle = CreateFile(devicePath, GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                    IntPtr.Zero, OpenExisting, extraFlags, IntPtr.Zero);
                return handle == new IntPtr(-1) ? IntPtr.Zero : handle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>Reads one input report on an overlapped handle, waiting at most timeoutMs.</summary>
        public static bool TryReadInputReport(IntPtr handle, byte[] buffer, int timeoutMs, out int bytesRead)
        {
            bytesRead = 0;
            if (handle == IntPtr.Zero || buffer == null || buffer.Length == 0) return false;

            var readEvent = CreateEvent(IntPtr.Zero, true, false, null);
            if (readEvent == IntPtr.Zero) return false;

            try
            {
                var overlapped = new NativeOverlapped { EventHandle = readEvent };
                var started = ReadFile(handle, buffer, (uint)buffer.Length, IntPtr.Zero, ref overlapped);
                if (!started && Marshal.GetLastWin32Error() != ErrorIoPending)
                    return false;

                // An overlapped read normally returns false + ERROR_IO_PENDING, but a fast HID
                // device can complete it synchronously. In that case the event is never
                // signalled, so only wait when the operation is actually pending.
                if (!started)
                {
                    var wait = WaitForSingleObject(readEvent, (uint)Math.Max(0, timeoutMs));
                    if (wait != 0)
                    {
                        try { CancelIoEx(handle, IntPtr.Zero); } catch { }
                        return false;
                    }
                }

                uint transferred;
                if (!GetOverlappedResult(handle, ref overlapped, out transferred, false)) return false;
                if (transferred == 0 || transferred > buffer.Length) return false;
                bytesRead = (int)transferred;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                CloseHandle(readEvent);
            }
        }

        /// <summary>Writes one output report on an overlapped handle, waiting at most timeoutMs.</summary>
        public static bool TryWriteReport(IntPtr handle, byte[] report, int timeoutMs)
        {
            if (handle == IntPtr.Zero || report == null || report.Length == 0) return false;

            var writeEvent = CreateEvent(IntPtr.Zero, true, false, null);
            if (writeEvent == IntPtr.Zero) return false;

            try
            {
                var overlapped = new NativeOverlapped { EventHandle = writeEvent };
                var started = WriteFile(handle, report, (uint)report.Length, IntPtr.Zero, ref overlapped);
                if (!started && Marshal.GetLastWin32Error() != ErrorIoPending)
                    return false;

                if (!started)
                {
                    var wait = WaitForSingleObject(writeEvent, (uint)Math.Max(0, timeoutMs));
                    if (wait != 0)
                    {
                        try { CancelIoEx(handle, IntPtr.Zero); } catch { }
                        return false;
                    }
                }

                uint transferred;
                return GetOverlappedResult(handle, ref overlapped, out transferred, false);
            }
            catch
            {
                return false;
            }
            finally
            {
                CloseHandle(writeEvent);
            }
        }

        public static void CloseHandlePublic(IntPtr handle)
        {
            CloseHandleSafe(handle);
        }

        public static void CloseHandleSafe(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try
            {
                CloseHandle(handle);
            }
            catch
            {
                // ignore
            }
        }

        private static string GetDeviceInterfacePath(IntPtr deviceInfoSet, ref SpDeviceInterfaceData interfaceData)
        {
            uint requiredSize;
            SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out requiredSize,
                IntPtr.Zero);
            if (requiredSize == 0 || requiredSize > 64 * 1024) return null;

            var buffer = Marshal.AllocHGlobal((int)requiredSize);
            try
            {
                // SP_DEVICE_INTERFACE_DETAIL_DATA_W: DWORD cbSize followed by WCHAR DevicePath[].
                // The documented "sizeof" is 8 on x64 and 6 on x86.
                Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                uint ignored;
                if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, buffer, requiredSize,
                        out ignored, IntPtr.Zero))
                    return null;

                return Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static HidInterfaceInfo InspectDevice(string path)
        {
            var handle = OpenShared(path);
            if (handle == IntPtr.Zero) return null;

            try
            {
                var attributes = new HiddAttributes();
                attributes.Size = (uint)Marshal.SizeOf(typeof(HiddAttributes));
                if (!HidD_GetAttributes(handle, ref attributes)) return null;

                var info = new HidInterfaceInfo
                {
                    DevicePath = path,
                    VendorId = attributes.VendorId,
                    ProductId = attributes.ProductId
                };

                IntPtr preparsedData;
                if (!HidD_GetPreparsedData(handle, out preparsedData) || preparsedData == IntPtr.Zero)
                    return info;

                try
                {
                    var capsBuffer = Marshal.AllocHGlobal(512);
                    try
                    {
                        if (HidP_GetCaps(preparsedData, capsBuffer) == HidpStatusSuccess)
                        {
                            info.Usage = (ushort)Marshal.ReadInt16(capsBuffer, 0);
                            info.UsagePage = (ushort)Marshal.ReadInt16(capsBuffer, 2);
                            info.InputReportByteLength = (ushort)Marshal.ReadInt16(capsBuffer, 4);
                            info.OutputReportByteLength = (ushort)Marshal.ReadInt16(capsBuffer, 6);
                            info.FeatureReportByteLength = (ushort)Marshal.ReadInt16(capsBuffer, 8);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(capsBuffer);
                    }
                }
                finally
                {
                    HidD_FreePreparsedData(preparsedData);
                }

                try
                {
                    var product = new StringBuilder(128);
                    if (HidD_GetProductString(handle, product, (uint)(product.Capacity * 2)))
                        info.ProductName = product.ToString().Trim();
                }
                catch
                {
                    // product string is optional
                }

                return info;
            }
            finally
            {
                CloseHandleSafe(handle);
            }
        }

        // ---- HID read/write (synchronous handles; one reader thread per connection) ----

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(IntPtr file, byte[] buffer, uint bytesToRead, out uint bytesRead,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr file, byte[] buffer, uint bytesToWrite, out uint bytesWritten,
            IntPtr overlapped);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess,
            out IntPtr target, uint desiredAccess, bool inheritHandle, uint options);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelSynchronousIo(IntPtr threadHandle);

        public const uint DuplicateSameAccess = 0x00000002;

        // ---- SetupAPI / HID ----

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData
        {
            public uint Size;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HiddAttributes
        {
            public uint Size;
            public ushort VendorId;
            public ushort ProductId;
            public ushort VersionNumber;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent,
            uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
            ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
            ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HiddAttributes attributes);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr preparsedData, IntPtr capabilityBuffer);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool HidD_GetProductString(IntPtr hidDeviceObject, StringBuilder buffer,
            uint bufferLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr file, byte[] buffer, uint bytesToRead, IntPtr bytesRead,
            ref NativeOverlapped overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr file, byte[] buffer, uint bytesToWrite, IntPtr bytesWritten,
            ref NativeOverlapped overlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr eventAttributes, bool manualReset, bool initialState,
            string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(IntPtr file, ref NativeOverlapped overlapped,
            out uint bytesTransferred, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(IntPtr file, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

