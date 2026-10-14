using BecquerelMonitor;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AmplitudaUsbTests
{
    // P257: "(first found)" must skip the units other documents of this application measure.
    // P285 (Amber 14.10.2026, "Отказывать второму"): a second document naming a measured unit by its
    // serial number is refused (ErrorBlockBusy) until that measurement ends.
    // No hardware: a fake unit is an empty file opened for overlapped reading - the reader's first read
    // ends with EOF, so it drops the handle and reconnects every 500 ms, which is exactly the path that
    // must not be refused by the reader's own lease. A frame never arrives, so a fake reader gives up
    // on its own after the reconnect window (~10 s) - the "link lost" path.
    public static class LeaseTests
    {
        const int Vid = 0x534B;
        const int Pid = 0x0874;

        sealed class FakeBench : IDisposable
        {
            public readonly List<AmplitudaUsbDeviceInfo> Units = new List<AmplitudaUsbDeviceInfo>();
            public readonly List<SafeFileHandle> Handles = new List<SafeFileHandle>();
            public Action<string> BeforeOpen;
            readonly HashSet<int> unplugged = new HashSet<int>();
            readonly Dictionary<string, int> opens = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            readonly List<AmplitudaUsbReader> readers = new List<AmplitudaUsbReader>();
            readonly Dictionary<AmplitudaUsbReader, Switch> switches = new Dictionary<AmplitudaUsbReader, Switch>();
            readonly string dir;

            public FakeBench(params string[] serials)
            {
                dir = Path.Combine(Path.GetTempPath(), "AmplitudaUsbTests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                for (int i = 0; i < serials.Length; i++)
                {
                    string path = Path.Combine(dir, "unit" + i.ToString(CultureInfo.InvariantCulture) + ".hid");
                    File.WriteAllBytes(path, new byte[0]);
                    AmplitudaUsbDeviceInfo info = new AmplitudaUsbDeviceInfo();
                    info.Path = path;
                    info.SerialNumber = serials[i];
                    info.Manufacturer = "";
                    info.Product = "fake";
                    Units.Add(info);
                }
            }

            // Per reader: a failing device list must fail only the reader under test.
            sealed class Switch
            {
                public volatile bool FailFind;
            }

            List<AmplitudaUsbDeviceInfo> Find(Switch reader)
            {
                if (reader.FailFind)
                {
                    throw new InvalidOperationException("fake: the device list cannot be read");
                }
                List<AmplitudaUsbDeviceInfo> present = new List<AmplitudaUsbDeviceInfo>();
                lock (unplugged)
                {
                    for (int i = 0; i < Units.Count; i++)
                    {
                        if (!unplugged.Contains(i))
                        {
                            present.Add(Units[i]);
                        }
                    }
                }
                return present;
            }

            // The unit leaves (or comes back to) the device list, as a pulled cable does.
            public void Unplug(int unit, bool gone)
            {
                lock (unplugged)
                {
                    if (gone)
                    {
                        unplugged.Add(unit);
                    }
                    else
                    {
                        unplugged.Remove(unit);
                    }
                }
            }

            SafeFileHandle Open(string path, out int error)
            {
                lock (opens)
                {
                    int count;
                    opens.TryGetValue(path, out count);
                    opens[path] = count + 1;
                }
                Action<string> before = BeforeOpen;
                if (before != null)
                {
                    before(path);
                }
                SafeFileHandle opened = HidNative.CreateFile(path, HidNative.GENERIC_READ, 7u /* read | write | delete */,
                    IntPtr.Zero, HidNative.OPEN_EXISTING, HidNative.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                error = opened.IsInvalid ? Marshal.GetLastWin32Error() : 0;
                lock (Handles)
                {
                    Handles.Add(opened);
                }
                return opened;
            }

            public int OpenCount(int unit)
            {
                lock (opens)
                {
                    int count;
                    return opens.TryGetValue(Units[unit].Path, out count) ? count : 0;
                }
            }

            public AmplitudaUsbReader Reader(string serial, AmplitudaUsbBlockLeases leases)
            {
                Switch control = new Switch();
                AmplitudaUsbReader reader = new AmplitudaUsbReader(Vid, Pid, serial, 14.0, leases, () => Find(control), Open);
                readers.Add(reader);
                switches[reader] = control;
                return reader;
            }

            public void FailFind(AmplitudaUsbReader reader, bool fail)
            {
                switches[reader].FailFind = fail;
            }

            public void Dispose()
            {
                foreach (AmplitudaUsbReader reader in readers)
                {
                    reader.Dispose();
                }
                lock (Handles)
                {
                    foreach (SafeFileHandle handle in Handles)
                    {
                        handle.Dispose();
                    }
                }
                try { Directory.Delete(dir, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        static bool WaitFor(Func<bool> condition, int timeoutMs)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition())
            {
                if (DateTime.UtcNow > end)
                {
                    return false;
                }
                Thread.Sleep(20);
            }
            return true;
        }

        static string Code(int error)
        {
            return error.ToString(CultureInfo.InvariantCulture);
        }

        static AmplitudaUsbDeviceInfo Unit(string serial, string path)
        {
            AmplitudaUsbDeviceInfo info = new AmplitudaUsbDeviceInfo();
            info.SerialNumber = serial;
            info.Path = path;
            info.Manufacturer = "";
            info.Product = "fake";
            return info;
        }

        // ---- the selection itself, no reader --------------------------------------------------

        public static void TestSelectFirstFoundSkipsHeldUnit()
        {
            AmplitudaUsbDeviceInfo u1 = Unit("S1", @"\\?\hid#1");
            AmplitudaUsbDeviceInfo u2 = Unit("S2", @"\\?\hid#2");
            List<AmplitudaUsbDeviceInfo> found = new List<AmplitudaUsbDeviceInfo> { u1, u2 };
            bool allHeld;
            List<AmplitudaUsbDeviceInfo> picked = AmplitudaUsbReader.SelectCandidates(found, "",
                new[] { AmplitudaUsbReader.BlockKey(u1) }, out allHeld);
            T.True(picked.Count == 1 && picked[0] == u2, "first found, unit 1 held elsewhere: only unit 2 is tried");
            T.True(!allHeld, "one unit is still free: not a refusal");

            picked = AmplitudaUsbReader.SelectCandidates(found, "", new string[0], out allHeld);
            T.True(picked.Count == 2 && picked[0] == u1 && picked[1] == u2, "nothing held: every unit, in the order found (A316: the next is tried on a refusal)");
        }

        // P285: an explicit serial number of a unit held elsewhere is a refusal, found or not.
        public static void TestSelectExplicitSerialOfHeldUnitIsARefusal()
        {
            AmplitudaUsbDeviceInfo u1 = Unit("S1", @"\\?\hid#1");
            AmplitudaUsbDeviceInfo u2 = Unit("S2", @"\\?\hid#2");
            List<AmplitudaUsbDeviceInfo> found = new List<AmplitudaUsbDeviceInfo> { u1, u2 };
            bool allHeld;
            List<AmplitudaUsbDeviceInfo> picked = AmplitudaUsbReader.SelectCandidates(found, "s1",
                new[] { AmplitudaUsbReader.BlockKey(u1) }, out allHeld);
            T.True(picked.Count == 0 && allHeld, "explicit serial of a unit held elsewhere (serial compared ignoring case): nothing is tried, refusal (got "
                + picked.Count.ToString(CultureInfo.InvariantCulture) + " candidates, allHeld " + allHeld + ")");

            picked = AmplitudaUsbReader.SelectCandidates(found, "S1", new[] { AmplitudaUsbReader.BlockKey(u2) }, out allHeld);
            T.True(picked.Count == 1 && picked[0] == u1 && !allHeld, "explicit serial, ANOTHER unit held elsewhere: its own unit is tried");

            picked = AmplitudaUsbReader.SelectCandidates(new List<AmplitudaUsbDeviceInfo> { u2 }, "S1", new[] { "sn:S1" }, out allHeld);
            T.True(picked.Count == 0 && allHeld, "explicit serial held elsewhere while unplugged (its reader reconnects): a refusal, not 'not found'");

            picked = AmplitudaUsbReader.SelectCandidates(found, "S3", new string[0], out allHeld);
            T.True(picked.Count == 0 && !allHeld, "explicit serial of a unit nobody holds and nobody found: 'not found', not a refusal");

            picked = AmplitudaUsbReader.SelectCandidates(found, "S1", null, out allHeld);
            T.True(picked.Count == 1 && picked[0] == u1 && !allHeld, "outside a registry (the form's Test): the unit is tried, as before");
        }

        public static void TestSelectAllHeldIsARefusal()
        {
            AmplitudaUsbDeviceInfo u1 = Unit("S1", @"\\?\hid#1");
            AmplitudaUsbDeviceInfo u2 = Unit("S2", @"\\?\hid#2");
            List<AmplitudaUsbDeviceInfo> found = new List<AmplitudaUsbDeviceInfo> { u1, u2 };
            bool allHeld;
            List<AmplitudaUsbDeviceInfo> picked = AmplitudaUsbReader.SelectCandidates(found, "",
                new[] { "SN:s1", AmplitudaUsbReader.BlockKey(u2) }, out allHeld);
            T.True(picked.Count == 0 && allHeld, "first found, both units held elsewhere (keys compared ignoring case): refusal");

            picked = AmplitudaUsbReader.SelectCandidates(new List<AmplitudaUsbDeviceInfo>(), "", new[] { "sn:S1" }, out allHeld);
            T.True(picked.Count == 0 && !allHeld, "no unit found at all is 'not found', not 'all in use'");
        }

        public static void TestBlockKeyFallsBackToPath()
        {
            AmplitudaUsbDeviceInfo bare1 = Unit("", @"\\?\hid#vid_534b&pid_0874#1");
            AmplitudaUsbDeviceInfo bare2 = Unit(null, @"\\?\hid#vid_534b&pid_0874#2");
            T.True(AmplitudaUsbReader.BlockKey(Unit("02164", "x")) == "sn:02164", "a unit with a serial number is keyed by it");
            T.True(AmplitudaUsbReader.BlockKey(bare1) != AmplitudaUsbReader.BlockKey(bare2), "units without a serial number are told apart by their HID path");
            bool allHeld;
            List<AmplitudaUsbDeviceInfo> picked = AmplitudaUsbReader.SelectCandidates(
                new List<AmplitudaUsbDeviceInfo> { bare1, bare2 }, "", new[] { AmplitudaUsbReader.BlockKey(bare1) }, out allHeld);
            T.True(picked.Count == 1 && picked[0] == bare2 && !allHeld, "a held unit without a serial number is skipped by its path");
        }

        // ---- the reader -------------------------------------------------------------------------

        public static void TestFirstFoundTakesNextFreeUnit()
        {
            using (FakeBench bench = new FakeBench("S1", "S2"))
            {
                AmplitudaUsbBlockLeases leases = new AmplitudaUsbBlockLeases();
                int error;
                AmplitudaUsbReader a = bench.Reader("", leases);
                T.True(a.Start(out error) && a.SerialNumber == "S1", "first document, first found: unit 1 (error " + Code(error) + ")");
                AmplitudaUsbReader b = bench.Reader("", leases);
                T.True(b.Start(out error) && b.SerialNumber == "S2", "second document, first found: unit 1 is measured, unit 2 is taken (got s/n " + b.SerialNumber + ")");
                AmplitudaUsbReader c = bench.Reader("", leases);
                bool started = c.Start(out error);
                T.True(!started && error == AmplitudaUsbReader.ErrorAllInUse, "third document, first found, both units measured: refused with ErrorAllInUse (got " + started + ", error " + Code(error) + ")");
                T.True(leases.Count == 2 && c.LeaseKey == null, "the refused reader holds no lease");

                // Guard of the unchanged behaviour: the device form's Test reader stays outside the registry.
                AmplitudaUsbReader test = new AmplitudaUsbReader(Vid, Pid, "", 14.0, null,
                    () => new List<AmplitudaUsbDeviceInfo>(bench.Units),
                    (string path, out int e) => { e = 0; return HidNative.CreateFile(path, HidNative.GENERIC_READ, 7u, IntPtr.Zero, HidNative.OPEN_EXISTING, HidNative.FILE_FLAG_OVERLAPPED, IntPtr.Zero); });
                try
                {
                    T.True(test.Start(out error) && test.SerialNumber == "S1", "a reader outside the registry (the form's Test) opens unit 1, as before");
                }
                finally
                {
                    test.Dispose();
                }
            }
        }

        // P285: two device configurations with one explicit serial number - the second is refused.
        public static void TestExplicitSerialOfMeasuredUnitIsRefused()
        {
            using (FakeBench bench = new FakeBench("S1", "S2"))
            {
                AmplitudaUsbBlockLeases leases = new AmplitudaUsbBlockLeases();
                int error;
                AmplitudaUsbReader a = bench.Reader("S1", leases);
                T.True(a.Start(out error) && a.SerialNumber == "S1", "first document, explicit S1: unit S1 opens (error " + Code(error) + ")");
                AmplitudaUsbReader b = bench.Reader("S1", leases);
                bool started = b.Start(out error);
                T.True(!started && error == AmplitudaUsbReader.ErrorBlockBusy, "second document, explicit S1 while S1 is measured: refused with ErrorBlockBusy (got "
                    + started + ", error " + Code(error) + ")");
                T.True(b.LeaseKey == null && b.State == AmplitudaUsbReaderState.Stopped && leases.Count == 1, "the refused reader holds no lease and runs no thread ("
                    + leases.Count.ToString(CultureInfo.InvariantCulture) + " leases)");
                T.True(a.LeaseKey == AmplitudaUsbReader.BlockKey(bench.Units[0]), "the first document keeps unit S1");
                AmplitudaUsbReader lower = bench.Reader("s1", leases);
                started = lower.Start(out error);
                T.True(!started && error == AmplitudaUsbReader.ErrorBlockBusy, "the serial number compared ignoring case: 's1' is refused too (got "
                    + started + ", error " + Code(error) + ")");
                AmplitudaUsbReader other = bench.Reader("S2", leases);
                T.True(other.Start(out error) && other.SerialNumber == "S2", "explicit S2 while S1 is measured: unit S2 opens (error " + Code(error) + ")");

                // Guard of the unchanged behaviour: the device form's Test reader stays outside the registry.
                AmplitudaUsbReader test = new AmplitudaUsbReader(Vid, Pid, "S1", 14.0, null,
                    () => new List<AmplitudaUsbDeviceInfo>(bench.Units),
                    (string path, out int e) => { e = 0; return HidNative.CreateFile(path, HidNative.GENERIC_READ, 7u, IntPtr.Zero, HidNative.OPEN_EXISTING, HidNative.FILE_FLAG_OVERLAPPED, IntPtr.Zero); });
                try
                {
                    T.True(test.Start(out error) && test.SerialNumber == "S1", "a reader outside the registry (the form's Test) opens the measured S1, as before");
                }
                finally
                {
                    test.Dispose();
                }
            }
        }

        // P285: the unit "(first found)" took is refused to an explicit serial number, and back: first found
        // skips a unit measured under its explicit serial number (P257).
        public static void TestExplicitSerialAndFirstFoundShareOneRegistry()
        {
            using (FakeBench bench = new FakeBench("S1", "S2"))
            {
                AmplitudaUsbBlockLeases leases = new AmplitudaUsbBlockLeases();
                int error;
                AmplitudaUsbReader f = bench.Reader("", leases);
                T.True(f.Start(out error) && f.SerialNumber == "S1", "first found takes unit 1");
                AmplitudaUsbReader d = bench.Reader("S1", leases);
                bool started = d.Start(out error);
                T.True(!started && error == AmplitudaUsbReader.ErrorBlockBusy, "explicit S1 of the unit measured by first found: refused with ErrorBlockBusy (got "
                    + started + ", error " + Code(error) + ")");
                AmplitudaUsbReader e = bench.Reader("S2", leases);
                T.True(e.Start(out error) && e.SerialNumber == "S2", "explicit S2: opens (error " + Code(error) + ")");
                AmplitudaUsbReader g = bench.Reader("", leases);
                started = g.Start(out error);
                T.True(!started && error == AmplitudaUsbReader.ErrorAllInUse, "first found, both units measured: still ErrorAllInUse (got "
                    + started + ", error " + Code(error) + ")");
            }
            using (FakeBench bench = new FakeBench("S1", "S2"))
            {
                AmplitudaUsbBlockLeases leases = new AmplitudaUsbBlockLeases();
                int error;
                AmplitudaUsbReader a = bench.Reader("S1", leases);
                T.True(a.Start(out error) && a.SerialNumber == "S1", "explicit S1 opens");
                AmplitudaUsbReader e = bench.Reader("", leases);
                T.True(e.Start(out error) && e.SerialNumber == "S2", "first found skips a unit measured under its explicit serial number (got s/n " + e.SerialNumber + ")");
            }
        }

        // P285: the release paths of an explicit serial number. Each first shows the second document
        // refused, then frees the unit and starts the second document on it.
        static void ExplicitReleasePath(string what, Action<AmplitudaUsbReader, FakeBench> release)
        {
            using (FakeBench bench = new FakeBench("S1", "S2"))
            {
                AmplitudaUsbBlockLeases leases = new AmplitudaUsbBlockLeases();
                int error;
                AmplitudaUsbReader a = bench.Reader("S1", leases);
                T.True(a.Start(out error) && a.SerialNumber == "S1", what + ": the first document opens S1 (error " + Code(error) + ")");
                AmplitudaUsbReader b = bench.Reader("S1", leases);
                bool started = b.Start(out error);
                T.True(!started && error == AmplitudaUsbReader.ErrorBlockBusy, what + ": while S1 is measured, the second document is refused (got "
                    + started + ", error " + Code(error) + ")");
                release(a, bench);
                T.True(a.LeaseKey == null && leases.Count == 0, what + ": the lease of S1 is gone ("
                    + leases.Count.ToString(CultureInfo.InvariantCulture) + " left)");
                AmplitudaUsbReader c = bench.Reader("S1", leases);
                T.True(c.Start(out error) && c.SerialNumber == "S1", what + ": the second document now starts on S1 (error " + Code(error) + ")");
            }
        }

        public static void TestExplicitLeaseReleasedOnStop()
        {
            ExplicitReleasePath("explicit, Stop", (a, bench) => a.Stop());
        }

        public static void TestExplicitLeaseReleasedOnDispose()
        {
            ExplicitReleasePath("explicit, Dispose", (a, bench) => a.Dispose());
        }

        public static void TestExplicitLeaseReleasedWhenReaderThreadFails()
        {
            ExplicitReleasePath("explicit, thread failed", (a, bench) =>
            {
                bench.FailFind(a, true);
                T.True(WaitFor(() => a.State == AmplitudaUsbReaderState.Failed, 3000), "explicit, thread failed: the reader reached Failed");
            });
        }

        // P285: the link of the first document is lost - the unit is pulled, the reconnect window runs out
        // (~10 s), the reader gives up on its own. While it still reconnects the unit stays taken; after
        // the give-up it is free. No Stop, no Dispose: the controller learns of it on its next timer tick.
        public static void TestExplicitLeaseReleasedWhenLinkLost()
        {
            using (FakeBench bench = new FakeBench("S1", "S2"))
            {
                AmplitudaUsbBlockLeases leases = new AmplitudaUsbBlockLeases();
                int error;
                AmplitudaUsbReader a = bench.Reader("S1", leases);
                T.True(a.Start(out error) && a.SerialNumber == "S1", "link lost: the first document opens S1 (error " + Code(error) + ")");
                bench.Unplug(0, true);
                AmplitudaUsbReader b = bench.Reader("S1", leases);
                bool started = b.Start(out error);
                T.True(!started && error == AmplitudaUsbReader.ErrorBlockBusy, "link lost: S1 pulled, its reader still reconnects - the second document is refused as busy, not 'not found' (got "
                    + started + ", error " + Code(error) + ")");
                T.True(WaitFor(() => a.State == AmplitudaUsbReaderState.Failed, 15000), "link lost: the first reader gave up after the reconnect window (state " + a.State + ")");
                T.True(WaitFor(() => a.LeaseKey == null && leases.Count == 0, 3000), "link lost: the lease of S1 is gone ("
                    + leases.Count.ToString(CultureInfo.InvariantCulture) + " left)");
                AmplitudaUsbReader c = bench.Reader("S1", leases);
                started = c.Start(out error);
                T.True(!started && error == AmplitudaUsbReader.ErrorDeviceNotFound, "link lost: S1 still pulled and nobody holds it - 'not found' (got "
                    + started + ", error " + Code(error) + ")");
                bench.Unplug(0, false);
                AmplitudaUsbReader d = bench.Reader("S1", leases);
                T.True(d.Start(out error) && d.SerialNumber == "S1", "link lost: S1 back - the second document starts on it (error " + Code(error) + ")");
            }
        }

        // P285: another document takes the unit between the list and the lease: the handle is closed,
        // the explicit serial number is refused as busy, no lease is left behind.
        public static void TestExplicitLostRaceClosesHandleAndRefuses()
        {
            using (FakeBench bench = new FakeBench("S1", "S2"))
            {
                AmplitudaUsbBlockLeases leases = new AmplitudaUsbBlockLeases();
                object other = new object();
                string key1 = AmplitudaUsbReader.BlockKey(bench.Units[0]);
                string path1 = bench.Units[0].Path;
                bool raced = false;
                bench.BeforeOpen = path =>
                {
                    if (!raced && path == path1)
                    {
                        raced = true;
                        leases.TryTake(key1, other, true);
                    }
                };
                int error;
                AmplitudaUsbReader a = bench.Reader("S1", leases);
                bool started = a.Start(out error);
                T.True(!started && error == AmplitudaUsbReader.ErrorBlockBusy, "explicit S1 taken by another document mid-open: refused with ErrorBlockBusy (got "
                    + started + ", error " + Code(error) + ")");
                T.True(bench.Handles.Count > 0 && bench.Handles[0].IsClosed, "the handle opened on S1 was closed before any read");
                T.True(leases.KeyOf(other) == key1 && a.LeaseKey == null && leases.Count == 1, "the other document keeps S1, the refused reader holds nothing");
            }
        }

        public static void TestReconnectKeepsOwnUnit()
        {
            // A unit without a serial number: the reconnect is "first found" again, keyed by the path.
            using (FakeBench bench = new FakeBench(""))
            {
                AmplitudaUsbBlockLeases leases = new AmplitudaUsbBlockLeases();
                int error;
                AmplitudaUsbReader a = bench.Reader("", leases);
                T.True(a.Start(out error), "the unit without a serial number opens (error " + Code(error) + ")");
                T.True(WaitFor(() => bench.OpenCount(0) >= 2, 3000), "after the lost read the reader re-opened its own unit: its own lease did not refuse it");
                T.True(a.State != AmplitudaUsbReaderState.Failed && a.LeaseKey == AmplitudaUsbReader.BlockKey(bench.Units[0]), "the reconnecting reader keeps the lease of its unit (" + (a.LeaseKey ?? "<none>") + ")");
                AmplitudaUsbReader b = bench.Reader("", leases);
                bool started = b.Start(out error);
                T.True(!started && error == AmplitudaUsbReader.ErrorAllInUse, "while its reader reconnects the unit stays taken: another first found is refused (got " + started + ", error " + Code(error) + ")");
            }
            // A unit with a serial number: the reconnect goes by the serial number.
            using (FakeBench bench = new FakeBench("S1", "S2"))
            {
                AmplitudaUsbBlockLeases leases = new AmplitudaUsbBlockLeases();
                int error;
                AmplitudaUsbReader a = bench.Reader("", leases);
                T.True(a.Start(out error) && a.SerialNumber == "S1", "first found takes unit 1");
                T.True(WaitFor(() => bench.OpenCount(0) >= 2, 3000), "the reader re-opened unit 1 after the lost read");
                AmplitudaUsbReader b = bench.Reader("", leases);
                T.True(b.Start(out error) && b.SerialNumber == "S2", "while unit 1 reconnects another first found takes unit 2 (got s/n " + b.SerialNumber + ")");
            }
        }

        // The release paths. Each first shows the unit held (a third first found is refused), then frees it.
        static void ReleasePath(string what, Action<AmplitudaUsbReader, FakeBench> release)
        {
            using (FakeBench bench = new FakeBench("S1", "S2"))
            {
                AmplitudaUsbBlockLeases leases = new AmplitudaUsbBlockLeases();
                int error;
                AmplitudaUsbReader a = bench.Reader("", leases);
                AmplitudaUsbReader b = bench.Reader("", leases);
                T.True(a.Start(out error) && b.Start(out error) && a.SerialNumber == "S1" && b.SerialNumber == "S2", what + ": two first found take the two units");
                AmplitudaUsbReader c = bench.Reader("", leases);
                T.True(!c.Start(out error) && error == AmplitudaUsbReader.ErrorAllInUse, what + ": while held, a third first found is refused (error " + Code(error) + ")");
                release(a, bench);
                T.True(a.LeaseKey == null && leases.Count == 1, what + ": the lease of unit 1 is gone (" + leases.Count.ToString(CultureInfo.InvariantCulture) + " left)");
                AmplitudaUsbReader d = bench.Reader("", leases);
                T.True(d.Start(out error) && d.SerialNumber == "S1", what + ": the next first found takes the freed unit 1 (error " + Code(error) + ")");
            }
        }

        public static void TestLeaseReleasedOnStop()
        {
            ReleasePath("Stop", (a, bench) => a.Stop());
        }

        public static void TestLeaseReleasedOnDispose()
        {
            ReleasePath("Dispose", (a, bench) => a.Dispose());
        }

        // The reader thread dies on its own (here: the device list throws during the reconnect).
        // No Stop, no Dispose: the controller learns of it only on its next timer tick.
        public static void TestLeaseReleasedWhenReaderThreadFails()
        {
            ReleasePath("thread failed", (a, bench) =>
            {
                bench.FailFind(a, true);
                T.True(WaitFor(() => a.State == AmplitudaUsbReaderState.Failed, 3000), "thread failed: the reader reached Failed");
            });
        }

        // Another document takes the unit between the list and the lease: the handle is closed, the next unit is tried.
        public static void TestLostRaceClosesHandleAndTakesNextUnit()
        {
            using (FakeBench bench = new FakeBench("S1", "S2"))
            {
                AmplitudaUsbBlockLeases leases = new AmplitudaUsbBlockLeases();
                object other = new object();
                string key1 = AmplitudaUsbReader.BlockKey(bench.Units[0]);
                string path1 = bench.Units[0].Path;
                bool raced = false;
                bench.BeforeOpen = path =>
                {
                    if (!raced && path == path1)
                    {
                        raced = true;
                        leases.TryTake(key1, other, true);
                    }
                };
                int error;
                AmplitudaUsbReader a = bench.Reader("", leases);
                T.True(a.Start(out error) && a.SerialNumber == "S2", "unit 1 taken by another document mid-open: unit 2 is taken (got s/n " + a.SerialNumber + ")");
                T.True(bench.Handles.Count > 0 && bench.Handles[0].IsClosed, "the handle opened on unit 1 was closed before any read");
                T.True(leases.KeyOf(other) == key1, "the other document keeps unit 1");
            }
        }
    }
}
