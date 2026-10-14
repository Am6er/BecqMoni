using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BecquerelMonitor
{
    public enum AmplitudaUsbReaderState
    {
        Stopped,
        Running,
        Reconnecting,
        Failed
    }

    public sealed class AmplitudaUsbDeviceInfo
    {
        public string Path;
        public string SerialNumber;
        public string Manufacturer;
        public string Product;

        public override string ToString()
        {
            return Product + ", s/n " + SerialNumber;
        }
    }

    // Which physical units this application is measuring (P257). HID hands every open handle its own
    // copy of the frames, so Windows never refuses a second reader of the same unit: without this
    // registry two device configurations with "(first found)" both took unit No. 1, two documents
    // counted the same events and unit No. 2 stood idle. The registry is the application's own
    // lock, as AmplitudaSerialDeviceController.activeBlocks is for the serial blocks.
    // Key: AmplitudaUsbReader.BlockKey. Owner: the reader. One owner holds at most one key.
    public sealed class AmplitudaUsbBlockLeases
    {
        // The registry of the measurements (AmplitudaUsbDeviceController). The Test button of the
        // device form stays out of it: a three-second look at the stream writes no document.
        public static readonly AmplitudaUsbBlockLeases Measurements = new AmplitudaUsbBlockLeases();

        readonly object gate = new object();
        readonly Dictionary<object, string> keyByOwner = new Dictionary<object, string>();

        // Keys held by any owner other than this one (a snapshot).
        public HashSet<string> HeldByOthers(object owner)
        {
            HashSet<string> held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (gate)
            {
                foreach (KeyValuePair<object, string> lease in keyByOwner)
                {
                    if (!ReferenceEquals(lease.Key, owner))
                    {
                        held.Add(lease.Value);
                    }
                }
            }
            return held;
        }

        // Moves the owner's lease to this key. exclusive: refused while another owner holds the key
        // ("first found"); otherwise taken regardless (an explicit serial number, see SelectCandidates).
        public bool TryTake(string key, object owner, bool exclusive)
        {
            lock (gate)
            {
                if (exclusive)
                {
                    foreach (KeyValuePair<object, string> lease in keyByOwner)
                    {
                        if (!ReferenceEquals(lease.Key, owner) && string.Equals(lease.Value, key, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                }
                keyByOwner[owner] = key;
                return true;
            }
        }

        // Idempotent.
        public void Release(object owner)
        {
            lock (gate)
            {
                keyByOwner.Remove(owner);
            }
        }

        // The key the owner holds, null when none.
        public string KeyOf(object owner)
        {
            lock (gate)
            {
                string key;
                return keyByOwner.TryGetValue(owner, out key) ? key : null;
            }
        }

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return keyByOwner.Count;
                }
            }
        }
    }

    // Reads list-mode input reports from one Amplituda USB HID device on a background thread.
    // Owns no UI: state and data are polled by the owner (TakeBatch, State, LastError).
    public sealed class AmplitudaUsbReader : IDisposable
    {
        public const int ErrorDeviceNotFound = -1;
        public const int ErrorUnexpected = -2;
        public const int ErrorSilence = -3;
        const int ReadStopped = -4;
        // "(first found)": units were found, but every one is already measured by another document (P257).
        public const int ErrorAllInUse = -5;

        // Seams for the tests (tools/AmplitudaUsbTests): the device list and the opening of a path.
        // null in the application - FindDevices and CreateFile on the HID path.
        internal delegate List<AmplitudaUsbDeviceInfo> FindHook();
        internal delegate SafeFileHandle OpenHook(string path, out int error);

        const int SilenceTimeoutMs = 3000;      // the device sends ~91 frames per second, always
        const int ReconnectWindowMs = 10000;
        const int ReconnectPollMs = 500;

        // How long a silent device goes without frames before the reader gives up: the read waits
        // SilenceTimeoutMs before the stream counts as lost, and only then does the reconnect window
        // start. ERRAmplitudaUsbSilent quotes this number, so the message cannot drift from the code (A316).
        public const int SilenceGiveUpSeconds = (SilenceTimeoutMs + ReconnectWindowMs) / 1000;
        const int InputBufferCount = 512;       // default HID ring buffer is 32 reports = 0.35 s
        const int JoinTimeoutMs = 2000;

        readonly int vendorId;
        readonly int productId;
        readonly double deadMicrosPerEvent;
        readonly AmplitudaUsbAccumulator accumulator = new AmplitudaUsbAccumulator();
        readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        readonly ManualResetEvent ioEvent = new ManualResetEvent(false);
        readonly AmplitudaUsbBlockLeases leases;    // null: neither takes nor respects leases (the form's Test)
        readonly FindHook findHook;
        readonly OpenHook openHook;
        readonly object leaseGate = new object();

        string serialNumber;
        SafeFileHandle handle;                  // touched by the reader thread only, once it runs
        Thread thread;
        IntPtr bufferPtr = IntPtr.Zero;         // unmanaged: must not move during overlapped I/O
        IntPtr overlappedPtr = IntPtr.Zero;
        volatile int state = (int)AmplitudaUsbReaderState.Stopped;
        volatile int lastError;
        bool leaseClosed;                       // under leaseGate: Stop/Dispose came, no lease may be taken
        bool disposed;

        // A reader outside any lease registry: the Test button of the device form, the live smoke test.
        public AmplitudaUsbReader(int vendorId, int productId, string serialNumber, double deadMicrosPerEvent)
            : this(vendorId, productId, serialNumber, deadMicrosPerEvent, null, null, null)
        {
        }

        // A measurement: with an empty serial number the reader skips units other readers of the same
        // registry hold, and holds the unit it opened until Stop / Dispose / the end of its thread (P257).
        public AmplitudaUsbReader(int vendorId, int productId, string serialNumber, double deadMicrosPerEvent,
            AmplitudaUsbBlockLeases leases)
            : this(vendorId, productId, serialNumber, deadMicrosPerEvent, leases, null, null)
        {
        }

        internal AmplitudaUsbReader(int vendorId, int productId, string serialNumber, double deadMicrosPerEvent,
            AmplitudaUsbBlockLeases leases, FindHook findHook, OpenHook openHook)
        {
            this.vendorId = vendorId;
            this.productId = productId;
            this.serialNumber = serialNumber ?? "";
            this.deadMicrosPerEvent = deadMicrosPerEvent;
            this.leases = leases;
            this.findHook = findHook;
            this.openHook = openHook;
        }

        public AmplitudaUsbReaderState State
        {
            get { return (AmplitudaUsbReaderState)state; }
        }

        public int LastError
        {
            get { return lastError; }
        }

        // Serial number of the unit actually opened (the configured one may be empty = "first found").
        public string SerialNumber
        {
            get { return serialNumber; }
        }

        public static bool IsBusyError(int error)
        {
            return error == HidNative.ERROR_ACCESS_DENIED || error == HidNative.ERROR_SHARING_VIOLATION;
        }

        // The key of a physical unit in the lease registry (P257): its USB serial number; a unit
        // without one - its HID device path.
        public static string BlockKey(AmplitudaUsbDeviceInfo device)
        {
            return string.IsNullOrEmpty(device.SerialNumber) ? "path:" + (device.Path ?? "") : "sn:" + device.SerialNumber;
        }

        // The units Open tries, in this order (P257).
        // An explicit serial number: every unit with that number, held or not. Whether two documents may
        // measure one unit named explicitly is Amber's decision, still open: the behaviour stays as it was.
        // Empty ("first found"): every unit except those held elsewhere - by other readers of the registry;
        // the reader's own unit is not in heldElsewhere, so its reconnect is never refused by its own lease.
        // allHeld: units were found, and every one of them is held elsewhere.
        public static List<AmplitudaUsbDeviceInfo> SelectCandidates(IList<AmplitudaUsbDeviceInfo> found,
            string serialNumber, IEnumerable<string> heldElsewhere, out bool allHeld)
        {
            List<AmplitudaUsbDeviceInfo> result = new List<AmplitudaUsbDeviceInfo>();
            allHeld = false;
            if (found == null)
            {
                return result;
            }
            string wanted = serialNumber ?? "";
            HashSet<string> held = heldElsewhere == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(heldElsewhere, StringComparer.OrdinalIgnoreCase);
            int skipped = 0;
            foreach (AmplitudaUsbDeviceInfo device in found)
            {
                if (device == null)
                {
                    continue;
                }
                if (wanted.Length != 0)
                {
                    if (string.Equals(device.SerialNumber, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(device);
                    }
                    continue;
                }
                if (held.Contains(BlockKey(device)))
                {
                    skipped++;
                    continue;
                }
                result.Add(device);
            }
            allHeld = skipped > 0 && result.Count == 0;
            return result;
        }

        // The unit this reader holds in its registry (BlockKey), null when none or outside a registry.
        public string LeaseKey
        {
            get { return leases == null ? null : leases.KeyOf(this); }
        }

        public AmplitudaUsbBatch TakeBatch()
        {
            return accumulator.TakeBatch();
        }

        public static List<AmplitudaUsbDeviceInfo> FindDevices(int vendorId, int productId)
        {
            List<AmplitudaUsbDeviceInfo> result = new List<AmplitudaUsbDeviceInfo>();
            Guid hidGuid;
            HidNative.HidD_GetHidGuid(out hidGuid);
            IntPtr deviceInfoSet = HidNative.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);
            if (deviceInfoSet == HidNative.INVALID_HANDLE_VALUE)
            {
                return result;
            }
            try
            {
                for (int index = 0; ; index++)
                {
                    HidNative.SP_DEVICE_INTERFACE_DATA data = new HidNative.SP_DEVICE_INTERFACE_DATA();
                    data.cbSize = Marshal.SizeOf(typeof(HidNative.SP_DEVICE_INTERFACE_DATA));
                    if (!HidNative.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref data))
                    {
                        break;
                    }
                    int devInst;
                    string path = GetInterfacePath(deviceInfoSet, ref data, out devInst);
                    if (path == null)
                    {
                        continue;
                    }
                    AmplitudaUsbDeviceInfo info = Probe(path, devInst, vendorId, productId);
                    if (info != null)
                    {
                        result.Add(info);
                    }
                }
            }
            finally
            {
                HidNative.SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
            return result;
        }

        static string GetInterfacePath(IntPtr deviceInfoSet, ref HidNative.SP_DEVICE_INTERFACE_DATA data, out int devInst)
        {
            devInst = 0;
            int required;
            HidNative.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
            if (required <= 0)
            {
                return null;
            }
            IntPtr detail = Marshal.AllocHGlobal(required);
            try
            {
                // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 in a 64-bit process, 6 in a 32-bit one.
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                HidNative.SP_DEVINFO_DATA node = new HidNative.SP_DEVINFO_DATA();
                node.cbSize = Marshal.SizeOf(typeof(HidNative.SP_DEVINFO_DATA));
                if (!HidNative.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref data, detail, required, out required, ref node))
                {
                    return null;
                }
                devInst = node.DevInst;
                return Marshal.PtrToStringUni(IntPtr.Add(detail, 4));   // DevicePath follows cbSize
            }
            finally
            {
                Marshal.FreeHGlobal(detail);
            }
        }

        // Extracts the USB serial number from a device instance id such as
        // USB\VID_534B&PID_0874\02164. Returns the serial; "" for a USB device that has none
        // (Windows then generates an id containing '&'); null when the id is not the USB device
        // node itself (a HID node, an MI_xx interface node of a composite device) - keep walking up.
        public static string ParseSerialFromInstanceId(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                return null;
            }
            string[] parts = instanceId.Split('\\');
            if (parts.Length != 3 || !string.Equals(parts[0], "USB", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (parts[1].IndexOf("&MI_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return null;
            }
            return parts[2].IndexOf('&') >= 0 ? "" : parts[2];
        }

        // Finds the USB device node above a HID interface and the serial number Windows recorded for
        // it at plug-in time. Nothing here talks to the device: on real hardware its string
        // descriptors were seen to fail with ERROR_GEN_FAILURE until the next re-plug (after a
        // string request had been issued while it streamed at its top frame rate), so identity
        // must never depend on asking the device.
        static bool FindUsbNode(int devInst, out int usbNode, out string serial)
        {
            usbNode = 0;
            serial = null;
            int node = devInst;
            for (int level = 0; level < 3; level++)
            {
                int parent;
                if (HidNative.CM_Get_Parent(out parent, node, 0) != HidNative.CR_SUCCESS)
                {
                    break;
                }
                StringBuilder id = new StringBuilder(HidNative.MAX_DEVICE_ID_LEN + 1);
                if (HidNative.CM_Get_Device_ID(parent, id, id.Capacity, 0) != HidNative.CR_SUCCESS)
                {
                    break;
                }
                serial = ParseSerialFromInstanceId(id.ToString());
                if (serial != null)
                {
                    usbNode = parent;
                    return true;
                }
                node = parent;
            }
            return false;
        }

        // The product name as the bus driver read it when the device was plugged in ("" if unknown).
        static string BusReportedName(int usbNode)
        {
            HidNative.DEVPROPKEY key = HidNative.DEVPKEY_Device_BusReportedDeviceDesc();
            byte[] buffer = new byte[512];
            int size = buffer.Length;
            int type;
            if (HidNative.CM_Get_DevNode_Property(usbNode, ref key, out type, buffer, ref size, 0) != HidNative.CR_SUCCESS ||
                type != HidNative.DEVPROP_TYPE_STRING)
            {
                return "";
            }
            return DecodeString(buffer);
        }

        // Opens the interface with no access rights: enough for attributes and strings,
        // and it never conflicts with another program that holds the device.
        static AmplitudaUsbDeviceInfo Probe(string path, int devInst, int vendorId, int productId)
        {
            using (SafeFileHandle probe = HidNative.CreateFile(path, 0,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (probe.IsInvalid)
                {
                    return null;
                }
                HidNative.HIDD_ATTRIBUTES attributes = new HidNative.HIDD_ATTRIBUTES();
                attributes.Size = Marshal.SizeOf(typeof(HidNative.HIDD_ATTRIBUTES));
                if (!HidNative.HidD_GetAttributes(probe, ref attributes) ||
                    attributes.VendorID != vendorId || attributes.ProductID != productId)
                {
                    return null;
                }
                AmplitudaUsbDeviceInfo info = new AmplitudaUsbDeviceInfo();
                info.Path = path;
                // Identity comes from what Windows recorded at plug-in time. The device itself is asked
                // only as a fallback, when Windows knows nothing - normal operation sends it no request.
                info.Manufacturer = "";
                int usbNode;
                string serial;
                bool known = FindUsbNode(devInst, out usbNode, out serial);
                info.Product = known ? BusReportedName(usbNode) : "";
                info.SerialNumber = serial;

                byte[] buffer = new byte[512];
                if (info.Product.Length == 0 && HidNative.HidD_GetProductString(probe, buffer, buffer.Length))
                {
                    info.Product = DecodeString(buffer);
                }
                if (info.Product.Length == 0)
                {
                    info.Product = string.Format(CultureInfo.InvariantCulture, "USB {0:X4}:{1:X4}", vendorId, productId);
                }
                if (info.SerialNumber == null)
                {
                    info.SerialNumber = HidNative.HidD_GetSerialNumberString(probe, buffer, buffer.Length) ? DecodeString(buffer) : "";
                }
                return info;
            }
        }

        static string DecodeString(byte[] buffer)
        {
            string text = Encoding.Unicode.GetString(buffer);
            int end = text.IndexOf('\0');
            return (end >= 0 ? text.Substring(0, end) : text).Trim();
        }

        // Opens the device on the calling thread, so that "not found" and "busy" can be reported
        // at once, then starts the reader thread. error: 0, an Error* constant or a Win32 code.
        public bool Start(out int error)
        {
            error = 0;
            if (disposed || thread != null)
            {
                error = ErrorUnexpected;
                return false;
            }
            lock (leaseGate)
            {
                leaseClosed = false;
            }
            try
            {
                handle = Open(out error);
                if (handle == null)
                {
                    lastError = error;
                    ReleaseLease();
                    return false;
                }
                bufferPtr = Marshal.AllocHGlobal(AmplitudaUsbProtocol.ReportLength);
                overlappedPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
                stopEvent.Reset();
                state = (int)AmplitudaUsbReaderState.Running;
                thread = new Thread(Run);
                thread.Name = "AmplitudaUsbReader";
                thread.IsBackground = true;
                thread.Start();
                return true;
            }
            catch (Exception)
            {
                error = ErrorUnexpected;
                lastError = error;
                CloseHandle();
                FreeNative();
                ReleaseLease();
                state = (int)AmplitudaUsbReaderState.Stopped;
                return false;
            }
        }

        // With an empty serial number ("first found") a unit that refuses to open - held by another
        // program - does not end the search: the next connected unit is tried (A316). The error of the
        // last refusal is returned only when no unit opened at all.
        // Within a lease registry "first found" also skips the units other documents of this application
        // measure, and takes the lease of the unit it opened (P257); ErrorAllInUse when every unit found
        // is held. An explicit serial number opens its unit held or not, as before, and takes the lease
        // too - so that a "first found" reader skips that unit. A reconnect keeps the reader's own lease.
        SafeFileHandle Open(out int error)
        {
            error = ErrorDeviceNotFound;
            List<AmplitudaUsbDeviceInfo> found = findHook != null ? findHook() : FindDevices(vendorId, productId);
            bool firstFound = serialNumber.Length == 0;
            bool allHeld;
            List<AmplitudaUsbDeviceInfo> candidates = SelectCandidates(found, serialNumber,
                leases == null ? null : leases.HeldByOthers(this), out allHeld);
            if (allHeld)
            {
                error = ErrorAllInUse;
                return null;
            }
            foreach (AmplitudaUsbDeviceInfo device in candidates)
            {
                int openError;
                SafeFileHandle opened = openHook != null ? openHook(device.Path, out openError) : OpenPath(device.Path, out openError);
                if (opened == null || opened.IsInvalid)
                {
                    error = openError;
                    if (opened != null)
                    {
                        opened.Dispose();
                    }
                    continue;
                }
                // Taken only with the handle in hand, and checked again: another document may have taken
                // the unit since the list was made. A lost race closes the handle before any read.
                if (!TakeLease(BlockKey(device), firstFound))
                {
                    opened.Dispose();
                    error = ErrorAllInUse;
                    continue;
                }
                // Best effort: a deeper queue survives a stalled PC, a flush drops frames from before Start.
                HidNative.HidD_SetNumInputBuffers(opened, InputBufferCount);
                HidNative.HidD_FlushQueue(opened);
                serialNumber = device.SerialNumber ?? "";   // reconnect to the very same unit
                error = 0;
                return opened;
            }
            return null;
        }

        static SafeFileHandle OpenPath(string path, out int error)
        {
            SafeFileHandle opened = HidNative.CreateFile(path, HidNative.GENERIC_READ,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero, HidNative.OPEN_EXISTING,
                HidNative.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            error = opened.IsInvalid ? Marshal.GetLastWin32Error() : 0;
            return opened;
        }

        // Moves this reader's lease to the unit (see AmplitudaUsbBlockLeases.TryTake). Refused once
        // Stop / Dispose came: a thread still inside Open must not take the unit back.
        bool TakeLease(string key, bool exclusive)
        {
            if (leases == null)
            {
                return true;
            }
            lock (leaseGate)
            {
                return !leaseClosed && leases.TryTake(key, this, exclusive);
            }
        }

        // Idempotent: the end of the reader thread, a refused Start.
        void ReleaseLease()
        {
            if (leases == null)
            {
                return;
            }
            lock (leaseGate)
            {
                leases.Release(this);
            }
        }

        // Idempotent: Stop and Dispose. Also after a stuck thread - leak the buffers, never the unit.
        void CloseLease()
        {
            lock (leaseGate)
            {
                leaseClosed = true;
                if (leases != null)
                {
                    leases.Release(this);
                }
            }
        }

        void Run()
        {
            byte[] report = new byte[AmplitudaUsbProtocol.ReportLength];
            ushort[] codes = new ushort[AmplitudaUsbProtocol.MaxEvents];
            Stopwatch lost = new Stopwatch();
            try
            {
                while (!stopEvent.WaitOne(0))
                {
                    // The stream has been gone for too long: give up. The clock starts once, when the
                    // stream is lost, and is cleared only by a frame that was actually read - a device
                    // that re-opens fine but stays silent must not be able to postpone this for ever.
                    if (lost.IsRunning && lost.ElapsedMilliseconds > ReconnectWindowMs)
                    {
                        state = (int)AmplitudaUsbReaderState.Failed;
                        return;
                    }

                    if (handle == null)
                    {
                        if (stopEvent.WaitOne(ReconnectPollMs))
                        {
                            break;
                        }
                        int openError;
                        SafeFileHandle reopened = Open(out openError);
                        if (reopened != null)
                        {
                            handle = reopened;
                            state = (int)AmplitudaUsbReaderState.Running;
                        }
                        continue;
                    }

                    int transferred;
                    int error;
                    if (ReadOne(out transferred, out error))
                    {
                        if (lost.IsRunning)
                        {
                            lost.Reset();
                        }
                        Marshal.Copy(bufferPtr, report, 0, AmplitudaUsbProtocol.ReportLength);
                        int count;
                        int liveMicros;
                        if (AmplitudaUsbProtocol.TryDecode(report, transferred, codes, out count, out liveMicros))
                        {
                            accumulator.AddFrame(codes, count, liveMicros, deadMicrosPerEvent);
                        }
                        else
                        {
                            accumulator.AddMalformedFrame();
                        }
                    }
                    else if (error == ReadStopped)
                    {
                        break;
                    }
                    else
                    {
                        CloseHandle();
                        if (!lost.IsRunning)
                        {
                            lastError = error;
                            lost.Start();
                        }
                        state = (int)AmplitudaUsbReaderState.Reconnecting;
                    }
                }
                state = (int)AmplitudaUsbReaderState.Stopped;
            }
            catch (Exception)
            {
                // An exception escaping a thread would terminate the whole application.
                lastError = ErrorUnexpected;
                state = (int)AmplitudaUsbReaderState.Failed;
            }
            finally
            {
                CloseHandle();
                ReleaseLease();     // a failed reader reads nothing more: the unit is free for another document
            }
        }

        // One overlapped read. Returns false with error = ReadStopped, ErrorSilence or a Win32 code.
        bool ReadOne(out int transferred, out int error)
        {
            transferred = 0;
            NativeOverlapped overlapped = new NativeOverlapped();
            overlapped.EventHandle = ioEvent.SafeWaitHandle.DangerousGetHandle();
            Marshal.StructureToPtr(overlapped, overlappedPtr, false);

            if (!HidNative.ReadFile(handle, bufferPtr, AmplitudaUsbProtocol.ReportLength, IntPtr.Zero, overlappedPtr))
            {
                error = Marshal.GetLastWin32Error();
                if (error != HidNative.ERROR_IO_PENDING)
                {
                    return false;
                }
                int signaled = WaitHandle.WaitAny(new WaitHandle[] { ioEvent, stopEvent }, SilenceTimeoutMs);
                if (signaled != 0)
                {
                    // Stop requested or the device went silent: cancel and wait until the
                    // kernel is done with our buffer before anyone may free it.
                    HidNative.CancelIoEx(handle, overlappedPtr);
                    HidNative.GetOverlappedResult(handle, overlappedPtr, out transferred, true);
                    error = signaled == 1 ? ReadStopped : ErrorSilence;
                    return false;
                }
            }
            if (!HidNative.GetOverlappedResult(handle, overlappedPtr, out transferred, false))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            error = 0;
            return true;
        }

        void CloseHandle()
        {
            if (handle != null)
            {
                handle.Dispose();
                handle = null;
            }
        }

        void FreeNative()
        {
            if (bufferPtr != IntPtr.Zero) { Marshal.FreeHGlobal(bufferPtr); bufferPtr = IntPtr.Zero; }
            if (overlappedPtr != IntPtr.Zero) { Marshal.FreeHGlobal(overlappedPtr); overlappedPtr = IntPtr.Zero; }
        }

        // Idempotent. The batch collected so far stays available through TakeBatch.
        public void Stop()
        {
            Thread running = thread;
            if (running == null)
            {
                CloseLease();
                return;
            }
            stopEvent.Set();
            bool joined = running.Join(JoinTimeoutMs);
            CloseLease();
            if (!joined)
            {
                // Stuck thread: keep the reference (so Start refuses to run a second thread on the same
                // buffers) and keep the native buffers - leak rather than corrupt.
                return;
            }
            thread = null;
            FreeNative();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            Stop();
            disposed = true;
            if (thread == null)
            {
                stopEvent.Dispose();
                ioEvent.Dispose();
            }
        }
    }
}
