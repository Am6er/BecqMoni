using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using System.Threading.Tasks;
using Windows.Devices.Radios;
using Windows.Devices.Bluetooth.Advertisement;

namespace BecquerelMonitor
{
    public class RadiaCodeIn : IDisposable
    {
        private Thread readerThread, discoveryThread;
        private volatile bool thread_alive = true;
        private readonly int[] hystogram_buffered = new int[1024];
        private readonly object connectionLock = new object();
        private readonly object packetLock = new object();
        private readonly object stateLock = new object();
        private enum State { Starting, Connecting, Connected, Recording, Reconnecting, Disconnected, Resetting, Calibration, CalibrationDone, CalibrationFail, Faulted, Stopped };
        private State state = State.Disconnected;
        private double cps;
        private String deviceserial, addressble;
        private string guid;
        private volatile bool device_serial_changed;
        //protocol
        private const string RC_BLE_Service = "e63215e5-7003-49d8-96b0-b024798fb901";
        private const string RC_BLE_Characteristic = "e63215e6-7003-49d8-96b0-b024798fb901";
        private const string RC_BLE_Notify = "e63215e7-7003-49d8-96b0-b024798fb901";
        private const string RC_SET_EXCHANGE = "\x08\x00\x00\x00\x07\x00\x00\x80\x01\xff\x12\xff";
        private const string RC_GET_SPECTRUM = "\x08\x00\x00\x00&\x08\x00\x80\x00\x02\x00\x00";
        private const string RC_RESET_SPECTRUM = "\x0c\x00\x00\x00'\x08\x00\x82\x00\x02\x00\x00\x00\x00\x00\x00";
        private const string RC_VS_ENERGY_CALIB = "\x18\x00\x00\x00'\x08\x00\x83\x02\x02\x00\x00\x0c\x00\x00\x00";
        BluetoothLEDevice dev = null;
        GattDeviceService service = null;
        GattCharacteristic characteristic, characteristicNotify = null;
        RCSpectrum packet = new RCSpectrum();
        private BluetoothLEAdvertisementWatcher watcher;
        private readonly object watcherLock = new object();

        public event EventHandler<RadiaCodeInDataReadyArgs> DataReady;
        public event EventHandler<EventArgs> PortFailure;
        public event EventHandler<RadiaCodeStatusArgs> Status;
        public event EventHandler<RadiaCodeTroubleShootArgs> TroubleShoot;
        private static readonly object instancesLock = new object();
        private static List<RadiaCodeIn> instances = new List<RadiaCodeIn>();
        private bool trshoot = false;
        // Troubleshoot-only: track whether the target device is actually advertising during the
        // discovery window (helps tell "device out of range / silent" apart from "GATT failed").
        private volatile int discoveryAdvCount = 0;
        private volatile short discoveryLastRssi = 0;
        // One-shot guard: the reader thread waits for the initial discovery scan to finish before
        // its very first connect attempt (wake-before-connect), so attempt #1 no longer races.
        private volatile bool initialDiscoveryJoined = false;
        private bool calibration = false;
        private bool calibration_sent = false;
        private PolynomialEnergyCalibration polynomialEnergyCalibration;

        float A0, A1, A2;

        public void setCalibration(PolynomialEnergyCalibration cal)
        {
            this.polynomialEnergyCalibration = cal;
        }

        public static void cleanUp(string guid)
        {
            RadiaCodeIn instanceToDispose = null;
            lock (instancesLock)
            {
                foreach (RadiaCodeIn s in instances.ToArray())
                {
                    if (s != null && s.GUID.Equals(guid))
                    {
                        instances.Remove(s);
                        instanceToDispose = s;
                        break;
                    }
                }
            }
            if (instanceToDispose != null)
            {
                instanceToDispose.Dispose();
                Trace.WriteLine("Instance " + guid + " removed!");
            }
        }

        private void sendTroubleShoot(string text)
        {
            if (TroubleShoot != null && this.trshoot)
            {

                TroubleShoot(this, new RadiaCodeTroubleShootArgs(text));
            }
        }

        private void setStatus(State state)
        {
            string nextStatus;
            State prevState;
            lock (stateLock)
            {
                prevState = this.state;
                this.state = state;
                nextStatus = GetStateString(state);
            }
            // Diagnostics only (troubleshoot form): make the state machine's path visible so the
            // disconnect -> connect -> reconnect cycle behind a failed first attempt is readable.
            if (prevState != state)
            {
                sendTroubleShoot($"State: {GetStateString(prevState)} -> {nextStatus}");
            }
            if (Status != null) Status(this, new RadiaCodeStatusArgs(nextStatus));
        }

        // --- Troubleshoot diagnostic helpers (used only on the troubleshoot path) ---
        private static long ElapsedMs(Stopwatch sw)
        {
            return sw != null ? sw.ElapsedMilliseconds : -1;
        }

        private static string FormatProtocolError(byte? protocolError)
        {
            return protocolError.HasValue ? $"0x{protocolError.Value:X2}" : "none";
        }

        private static string SafeConnStatus(BluetoothLEDevice device)
        {
            if (device == null)
            {
                return "n/a";
            }
            try
            {
                return device.ConnectionStatus.ToString();
            }
            catch (Exception)
            {
                return "unavailable";
            }
        }

        public string getStateString()
        {
            lock (stateLock)
            {
                return GetStateString(state);
            }
        }

        private static string GetStateString(State state)
        {
            switch (state)
            {
                case State.Starting: return "Starting";
                case State.Connecting: return "Connecting";
                case State.Connected: return "Connected";
                case State.Recording: return "Recording";
                case State.Reconnecting: return "Reconnecting";
                case State.Disconnected: return "Disconnected";
                case State.Resetting: return "Resetting";
                case State.Calibration: return "Calibration";
                case State.CalibrationDone: return "Calibration done";
                case State.CalibrationFail: return "Calibration fail";
                case State.Faulted: return "Faulted";
                case State.Stopped: return "Stopped";
                default: return "Unknown";
            }
        }

        public static List<RadiaCodeIn> getAllInstances()
        {
            lock (instancesLock)
            {
                return new List<RadiaCodeIn>(instances);
            }
        }

        public static RadiaCodeIn getInstance(string guid, bool troubleshoot = false)
        {
            lock (instancesLock)
            {
                foreach (RadiaCodeIn s in instances)
                {
                    if (s == null) continue;
                    if (guid.Equals(s.GUID))
                    {
                        return s;
                    }
                }
                RadiaCodeIn instance = new RadiaCodeIn(guid, troubleshoot);
                instances.Add(instance);
                return instance;
            }
        }

        public static void finishAll()
        {
            List<RadiaCodeIn> instancesToDispose;
            lock (instancesLock)
            {
                instancesToDispose = new List<RadiaCodeIn>(instances);
                instances.Clear();
            }
            foreach (RadiaCodeIn s in instancesToDispose)
            {
                if (s != null) s.Dispose();
            }
        }

        private void TestBT()
        {
            try
            {
                Trace.WriteLine("Check BT status");
                sendTroubleShoot("Check BT status...");
                RadioAccessStatus access = Radio.RequestAccessAsync().AsTask().GetAwaiter().GetResult();
                if (access != RadioAccessStatus.Allowed)
                {
                    sendTroubleShoot("Error! current user isn't allowed to use radio module.");
                    return;
                }
                BluetoothAdapter adapter = BluetoothAdapter.GetDefaultAsync().AsTask().GetAwaiter().GetResult();
                if (null != adapter)
                {
                    sendTroubleShoot($"BT adapter: address={adapter.BluetoothAddress:X12}, lowEnergySupported={adapter.IsLowEnergySupported}, centralRoleSupported={adapter.IsCentralRoleSupported}");
                    Radio btRadio = adapter.GetRadioAsync().AsTask().GetAwaiter().GetResult();
                    if (btRadio.State != RadioState.On)
                    {
                        Trace.WriteLine("BT was disabled, enabling it");
                        sendTroubleShoot($"BT radio '{btRadio.Name}' was {btRadio.State}, enabling it.");
                        RadioAccessStatus setResult = btRadio.SetStateAsync(RadioState.On).AsTask().GetAwaiter().GetResult();
                        sendTroubleShoot($"BT radio enable result: {setResult}");
                    }
                    else
                    {
                        Trace.WriteLine($"BT status: {btRadio.State}");
                        sendTroubleShoot($"BT radio '{btRadio.Name}' status: {btRadio.State}");
                    }

                }
                else
                {
                    sendTroubleShoot("BT adapter not found (GetDefaultAsync returned null).");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Exception while enabling BT: {ex.Message} {ex.StackTrace}");
                sendTroubleShoot($"Exception while enabling BT: {ex.Message} {ex.StackTrace}");
            }
        }

        private void doDiscovery()
        {
            TestBT();
            Trace.WriteLine("Run discovery, to awaiken device");
            // Serialize the whole discovery session under watcherLock: doDiscovery runs from
            // both the discovery thread and the reader thread (reconnect). Locking only the
            // watcher creation left Start/Stop/subscribe/unsubscribe racing on a shared
            // watcher, so two sessions could Start/Stop the same instance concurrently.
            lock (watcherLock)
            {
                if (watcher == null) watcher = new BluetoothLEAdvertisementWatcher();
                watcher.ScanningMode = BluetoothLEScanningMode.Active;
                watcher.Received -= Watcher_Recived;
                watcher.Received += Watcher_Recived;
                discoveryAdvCount = 0;
                discoveryLastRssi = 0;
                sendTroubleShoot($"Discovery: 5s active scan to wake device (target addr={addressble})...");
                try
                {
                    watcher.Start();
                    Thread.Sleep(5000);
                }
                catch (Exception ex) {
                    // No MessageBox from the reader thread: it blocked BLE reception until
                    // the user clicked it away.
                    Trace.WriteLine($"Error doing discovery: {ex.Message}: {ex.StackTrace}");
                    sendTroubleShoot($"Error doing discovery: {ex.Message}");
                    RaisePortFailure();
                } finally
                {
                    watcher.Stop();
                    watcher.Received -= Watcher_Recived;
                    sendTroubleShoot($"Discovery finished (watcher status={watcher.Status}): {discoveryAdvCount} advertisement(s) from target seen" +
                        (discoveryAdvCount > 0 ? $", last RSSI={discoveryLastRssi} dBm." : " — device did not advertise in this window."));
                }
            }
        }

        private void Watcher_Recived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            // Troubleshoot-only bookkeeping: count how many advertisements the target device
            // emits during the discovery window. No effect on the normal (non-troubleshoot) path.
            if (!trshoot || args == null)
            {
                return;
            }
            try
            {
                ulong target;
                if (addressble != null && ulong.TryParse(addressble, out target) && args.BluetoothAddress == target)
                {
                    discoveryAdvCount++;
                    discoveryLastRssi = args.RawSignalStrengthInDBm;
                }
            }
            catch (Exception)
            {
            }
        }

        private bool ConnectBLE(string addrBLE)
        {
            BluetoothLEDevice localDevice = null;
            GattDeviceService localService = null;
            GattCharacteristic localWriteCharacteristic = null;
            GattCharacteristic localNotifyCharacteristic = null;
            try
            {
                lock (connectionLock)
                {
                    Stopwatch sw = trshoot ? Stopwatch.StartNew() : null;
                    Trace.WriteLine($"Try to connect BLE at addr: {addrBLE}");
                    sendTroubleShoot($"Try to connect BLE at addr: {addrBLE}");
                    localDevice = BluetoothLEDevice.FromBluetoothAddressAsync(Convert.ToUInt64(addrBLE)).AsTask().GetAwaiter().GetResult();
                    if (localDevice == null)
                    {
                        sendTroubleShoot($"Failed to create BLE device from address (FromBluetoothAddressAsync returned null after {ElapsedMs(sw)} ms).");
                        return false;
                    }
                    // Windows caches a BluetoothLEDevice even when it is not yet connected; the very
                    // first GATT query is what actually forces the connection, and it commonly fails
                    // with Unreachable if the device just started advertising. Report the pre-query
                    // connection status so a "not yet connected" first attempt is obvious.
                    if (trshoot)
                    {
                        string preConnStatus;
                        try { preConnStatus = localDevice.ConnectionStatus.ToString(); } catch (Exception) { preConnStatus = "unavailable"; }
                        sendTroubleShoot($"BLE device resolved in {ElapsedMs(sw)} ms: name='{localDevice.Name}', connStatus={preConnStatus}");
                    }

                    GattDeviceServicesResult servicesResult = localDevice.GetGattServicesForUuidAsync(Guid.Parse(RC_BLE_Service)).AsTask().GetAwaiter().GetResult();
                    if (servicesResult == null || servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
                    {
                        sendTroubleShoot($"Failed to get GATT service after {ElapsedMs(sw)} ms. Status={servicesResult?.Status}, protocolError={FormatProtocolError(servicesResult?.ProtocolError)}, serviceCount={(servicesResult != null ? servicesResult.Services.Count : 0)}, connStatus={SafeConnStatus(localDevice)}");
                        return false;
                    }
                    sendTroubleShoot($"GATT service acquired after {ElapsedMs(sw)} ms.");

                    localService = servicesResult.Services[0];
                    GattCharacteristicsResult writeResult = localService.GetCharacteristicsForUuidAsync(Guid.Parse(RC_BLE_Characteristic)).AsTask().GetAwaiter().GetResult();
                    if (writeResult == null || writeResult.Status != GattCommunicationStatus.Success || writeResult.Characteristics.Count == 0)
                    {
                        sendTroubleShoot($"Failed to get write characteristic after {ElapsedMs(sw)} ms. Status={writeResult?.Status}, protocolError={FormatProtocolError(writeResult?.ProtocolError)}");
                        return false;
                    }

                    localWriteCharacteristic = writeResult.Characteristics[0];
                    if (!CanWrite(localWriteCharacteristic))
                    {
                        sendTroubleShoot($"Write characteristic does not support write operations (properties={localWriteCharacteristic.CharacteristicProperties}).");
                        return false;
                    }

                    GattCharacteristicsResult notifyResult = localService.GetCharacteristicsForUuidAsync(Guid.Parse(RC_BLE_Notify)).AsTask().GetAwaiter().GetResult();
                    if (notifyResult == null || notifyResult.Status != GattCommunicationStatus.Success || notifyResult.Characteristics.Count == 0)
                    {
                        sendTroubleShoot($"Failed to get notify characteristic after {ElapsedMs(sw)} ms. Status={notifyResult?.Status}, protocolError={FormatProtocolError(notifyResult?.ProtocolError)}");
                        return false;
                    }

                    localNotifyCharacteristic = notifyResult.Characteristics[0];
                    if (!localNotifyCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                    {
                        sendTroubleShoot($"Notify characteristic does not support notifications (properties={localNotifyCharacteristic.CharacteristicProperties}).");
                        return false;
                    }

                    localNotifyCharacteristic.ValueChanged += Characteristic_ValueChanged;
                    GattCommunicationStatus cccdStatus = localNotifyCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask().GetAwaiter().GetResult();
                    if (cccdStatus != GattCommunicationStatus.Success)
                    {
                        localNotifyCharacteristic.ValueChanged -= Characteristic_ValueChanged;
                        sendTroubleShoot($"Failed to enable notifications after {ElapsedMs(sw)} ms. CCCD status={cccdStatus}, connStatus={SafeConnStatus(localDevice)}");
                        return false;
                    }
                    sendTroubleShoot($"BLE connected: notifications enabled after {ElapsedMs(sw)} ms total.");

                    localDevice.ConnectionStatusChanged += Dev_ConnectionStatusChanged;
                    dev = localDevice;
                    service = localService;
                    characteristic = localWriteCharacteristic;
                    characteristicNotify = localNotifyCharacteristic;

                    ResetPacket();
                    sendTroubleShoot("Send RC_SET_EXCHANGE handshake");
                    WritePacket(RC_SET_EXCHANGE);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
                sendTroubleShoot($"Exception while connecting BLE: {ex.Message}");
                return false;
            }
            finally
            {
                bool success;
                lock (connectionLock)
                {
                    success = ReferenceEquals(dev, localDevice) &&
                        ReferenceEquals(service, localService) &&
                        ReferenceEquals(characteristic, localWriteCharacteristic) &&
                        ReferenceEquals(characteristicNotify, localNotifyCharacteristic);
                }
                if (!success)
                {
                    DisposeConnectionResources(localDevice, localService, localNotifyCharacteristic, detachDeviceEvent: true);
                }
            }
        }

        private void Dev_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            BluetoothConnectionStatus? connectionStatus = null;
            State currentState;
            lock (connectionLock)
            {
                if (dev != null)
                {
                    connectionStatus = dev.ConnectionStatus;
                }
            }
            if (connectionStatus.HasValue)
            {
                sendTroubleShoot($"Device connection status changed: {connectionStatus.Value}");
            }
            lock (stateLock)
            {
                currentState = state;
            }
            if (!connectionStatus.HasValue && (currentState == State.Connected || currentState == State.Recording || currentState == State.Resetting))
            {
                Trace.WriteLine("Disconnect device event");
                sendTroubleShoot($"Device connection status changed: dev disconnected");
                setStatus(State.Reconnecting);
            }
            if (connectionStatus == BluetoothConnectionStatus.Disconnected &&
                currentState != State.Disconnected &&
                currentState != State.Stopped &&
                currentState != State.Faulted)
            {
                Trace.WriteLine("Disconnect device event");
                setStatus(State.Reconnecting);
            }
        }

        private void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                DataReader reader = DataReader.FromBuffer(args.CharacteristicValue);
                byte[] buffer = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(buffer);
                string bufferSignature = string.Join(",", buffer);
                if (bufferSignature.StartsWith("16,0,0,0,7,0,0,128"))
                {
                    if (buffer.Length > 17 && buffer[16] == 1)
                    {
                        sendTroubleShoot("Exchange response: BLE_IF ready");
                        Trace.WriteLine("Exchange response: BLE_IF ready");
                    } else
                    {
                        sendTroubleShoot("Error! Exchange response: BLE_IF busy");
                        Trace.WriteLine("Exchange response: BLE_IF busy");
                    }
                    return;
                }
                if (calibration && calibration_sent)
                {
                    if (bufferSignature.StartsWith("8,0,0,0,39,8,0,131"))
                    {
                        if (buffer.Length > 9 && buffer[8] == 1)
                        {
                            Trace.WriteLine("Calibration response - calibration done.");
                            setStatus(State.CalibrationDone);
                            calibration = false;
                            calibration_sent = false;
                            return;
                        } else
                        {
                            Trace.WriteLine("Calibration response - calibration fail.");
                            setStatus(State.CalibrationFail);
                            calibration = false;
                            calibration_sent = false;
                            return;
                        }
                    }
                    return;
                }

                lock (packetLock)
                {
                    // Resync: 0x80000A26 at offset 4 marks the start of a new spectrum response.
                    // (Was "x == 2147485734 && x == 1" - the same expression compared to two
                    // constants, i.e. always false, so resync never fired.)
                    if (buffer.Length > 14 && BitConverter.ToUInt32(buffer, 4) == 2147485734)
                    {
                        packet = new RCSpectrum();
                    }
                    if (packet.NEWPACKET)
                    {
                        packet.SIZE = BitConverter.ToInt32(buffer, 0) + 4;
                        if (packet.SIZE < 20)
                        {
                            packet.BROKEN = true;
                            Trace.WriteLine("Drop packet because it is not spectrum packet");
                            return;
                        }
                        packet.buffer = new byte[packet.SIZE];
                        packet.NEWPACKET = false;
                    }
                    if (buffer.Length > packet.buffer.Length - packet.counter)
                    {
                        packet.BROKEN = true;
                        Trace.WriteLine("Drop packet because size > expected size.");
                        return;
                    }
                    Array.Copy(buffer, 0, packet.buffer, packet.counter, buffer.Length);
                    packet.counter += buffer.Length;
                    if (packet.counter == packet.SIZE)
                    {
                        if (packet.SIZE == 12)
                        {
                            packet.BROKEN = true;
                            Trace.WriteLine("Drop packet because it is not spectrum packet");
                            return;
                        }
                        try
                        {
                            packet.DecodePacket();
                            List<float> calibrationValues = packet.GetCalibration();
                            this.A0 = calibrationValues[0];
                            this.A1 = calibrationValues[1];
                            this.A2 = calibrationValues[2];
                        } catch (Exception ex)
                        {
                            Trace.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
                            packet.BROKEN = true;
                            Trace.WriteLine("Drop packet because it is not spectrum packet");
                            return;
                        }

                        if (packet.SPECTRUM.Length == 1024)
                        {
                            packet.COMPLETE = true;
                        }
                        else
                        {
                            packet.BROKEN = true;
                            sendTroubleShoot($"Drop packet because spectrum channels: {packet.SPECTRUM.Length}. Expected: 1024 channels.");
                            Trace.WriteLine($"Drop packet because spectrum channels: {packet.SPECTRUM.Length}. Expected: 1024 channels.");
                            return;
                        }
                    }
                    else if (packet.counter > packet.SIZE)
                    {
                        packet.BROKEN = true;
                        Trace.WriteLine($"Drop packet because size: {packet.counter} > expected size: {packet.SIZE}");
                        return;
                    }
                }
            } catch (Exception ex)
            {
                lock (packetLock)
                {
                    packet.BROKEN = true;
                }
                sendTroubleShoot($"Drop packet because EXCEPTION: {ex.Message} at {ex.StackTrace}");
                Trace.WriteLine($"Drop packet because EXCEPTION: {ex.Message} at {ex.StackTrace}");
                return;
            }
        }

        private void ResetPacket()
        {
            lock (packetLock)
            {
                packet = new RCSpectrum();
            }
        }

        public RadiaCodeIn(string guid, bool troubleshoot = false)
        {
            this.guid = guid;
            this.trshoot = troubleshoot;
            Trace.WriteLine("RadiaCodeIn instance created " + guid);

            readerThread = new Thread(this.run);
            readerThread.Name = "RadiaCodeIn";
            readerThread.IsBackground = true;
            readerThread.Start();

            discoveryThread = new Thread(doDiscovery);
            discoveryThread.Name = "Discovery";
            discoveryThread.IsBackground = true;
            discoveryThread.Start();
        }

        public string GUID
        {
            get { return this.guid; }
        }

        public void setDeviceSerial(string devSerial, string addrBle)
        {
            if (addrBle != null)
            {
                this.deviceserial = devSerial;
                this.addressble = addrBle;
                this.device_serial_changed = true;
            } else
            {
                sendTroubleShoot("Address BLE is empty, nothing to test");
            }
        }

        public void sendCommand(string command)
        {
            Trace.WriteLine("Command sent: " + command);
            switch (command)
            {
                case "Start": setStatus(State.Starting); break;
                case "Stop": setStatus(State.Stopped); DisconnectBLE(); break;
                case "Reset": setStatus(State.Resetting); break;
                case "Calibration":
                    {
                        calibration = true;
                        lock (stateLock)
                        {
                            if (this.state == State.Disconnected || this.state == State.Stopped)
                            {
                                setStatus(State.Starting);
                            }
                        }
                        break;
                    }
                case "Continue": setStatus(State.Connected); break;
                default: setStatus(State.Faulted); break;
            }
        }

        public PolynomialEnergyCalibration GetCalibration()
        {
            if (this.A0 != 0 && this.A1 != 0 && this.A2 != 0)
            {
                PolynomialEnergyCalibration calibration = new PolynomialEnergyCalibration();
                calibration.PolynomialOrder = 2;
                calibration.Coefficients = new double[3];
                calibration.Coefficients[0] = this.A0;
                calibration.Coefficients[1] = this.A1;
                calibration.Coefficients[2] = this.A2;
                return calibration;
            }
            return null;
        }

        private void WritePacket(string packet)
        {
            byte[] input = packet.ToCharArray().Select(b => (byte)b).ToArray<byte>();
            lock (connectionLock)
            {
                if (characteristic == null)
                {
                    throw new InvalidOperationException("Write characteristic is not available.");
                }

                DataWriter writer = new DataWriter();
                writer.WriteBytes(input);
                GattCommunicationStatus result = characteristic.WriteValueAsync(writer.DetachBuffer()).AsTask().GetAwaiter().GetResult();
                if (result != GattCommunicationStatus.Success)
                {
                    throw new InvalidOperationException($"BLE write failed with status {result}.");
                }
            }
        }

        private void WriteCalibration(PolynomialEnergyCalibration calibration)
        {
            byte[] a0 = BitConverter.GetBytes(Convert.ToSingle(calibration.Coefficients[0]));
            byte[] a1 = BitConverter.GetBytes(Convert.ToSingle(calibration.Coefficients[1]));
            byte[] a2 = BitConverter.GetBytes(Convert.ToSingle(calibration.Coefficients[2]));
            byte[] rc_vs_array = RC_VS_ENERGY_CALIB.ToCharArray().Select(b => (byte)b).ToArray<byte>();

            byte[] payload = rc_vs_array.Concat(a0).Concat(a1).Concat(a2).ToArray();
            byte[] payload1 = payload.Take(18).ToArray();
            byte[] payload2 = payload.Skip(18).ToArray();

            lock (connectionLock)
            {
                if (characteristic == null)
                {
                    throw new InvalidOperationException("Write characteristic is not available.");
                }

                DataWriter writer1 = new DataWriter();
                writer1.WriteBytes(payload1);
                GattCommunicationStatus result = characteristic.WriteValueAsync(writer1.DetachBuffer()).AsTask().GetAwaiter().GetResult();
                if (result != GattCommunicationStatus.Success)
                {
                    throw new InvalidOperationException($"Calibration write step 1 failed with status {result}.");
                }

                DataWriter writer2 = new DataWriter();
                writer2.WriteBytes(payload2);
                result = characteristic.WriteValueAsync(writer2.DetachBuffer()).AsTask().GetAwaiter().GetResult();
                if (result != GattCommunicationStatus.Success)
                {
                    throw new InvalidOperationException($"Calibration write step 2 failed with status {result}.");
                }
            }
        }

        private static bool CanWrite(GattCharacteristic gattCharacteristic)
        {
            return gattCharacteristic != null &&
                (gattCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ||
                 gattCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse));
        }

        private void RaisePortFailure()
        {
            if (PortFailure != null) PortFailure(this, null);
        }

        private void RaiseDataReady(RadiaCodeInDataReadyArgs args)
        {
            if (DataReady == null)
            {
                return;
            }

            foreach (EventHandler<RadiaCodeInDataReadyArgs> handler in DataReady.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"RadiaCode DataReady handler exception: {ex.Message} {ex.StackTrace}");
                }
            }
        }

        private bool HasActiveConnection()
        {
            lock (connectionLock)
            {
                return dev != null && service != null && characteristic != null && characteristicNotify != null;
            }
        }

        private void DisposeConnectionResources(BluetoothLEDevice bluetoothLeDevice, GattDeviceService gattService, GattCharacteristic notifyCharacteristic, bool detachDeviceEvent)
        {
            if (notifyCharacteristic != null)
            {
                try
                {
                    notifyCharacteristic.ValueChanged -= Characteristic_ValueChanged;
                }
                catch (Exception)
                {
                }
            }
            if (gattService != null)
            {
                try
                {
                    gattService.Dispose();
                }
                catch (Exception)
                {
                }
            }
            if (bluetoothLeDevice != null)
            {
                try
                {
                    if (detachDeviceEvent)
                    {
                        bluetoothLeDevice.ConnectionStatusChanged -= Dev_ConnectionStatusChanged;
                    }
                    bluetoothLeDevice.Dispose();
                }
                catch (Exception)
                {
                }
            }
        }

        int trshootCount = 0;

        public void run()
        {
            while (thread_alive)
            {
                if (device_serial_changed)
                {
                    device_serial_changed = false;
                    setStatus(State.Starting);
                }

                State currentState;
                lock (stateLock)
                {
                    currentState = state;
                }

                switch (currentState)
                {
                    case State.Stopped:
                    case State.Disconnected:
                        Thread.Sleep(500);
                        break;

                    case State.Starting:
                    case State.Connecting:
                    case State.Reconnecting:
                        try
                        {
                            if (addressble != null)
                            {
                                // Wake-before-connect: the discoveryThread runs a one-shot active
                                // scan that makes the peripheral known to the Windows BT stack.
                                // Without waiting for it, the first ConnectBLE races ahead and
                                // FromBluetoothAddressAsync returns null (device not yet seen
                                // advertising), burning attempt #1. Join that initial scan once
                                // before the first attempt so attempt #1 lands on an awake device.
                                if (!initialDiscoveryJoined)
                                {
                                    initialDiscoveryJoined = true;
                                    Thread discovery = discoveryThread;
                                    if (discovery != null && discovery.IsAlive && !ReferenceEquals(discovery, Thread.CurrentThread))
                                    {
                                        sendTroubleShoot("Waiting for initial discovery scan to finish before first connect...");
                                        discovery.Join(7000);
                                        sendTroubleShoot("Initial discovery scan done; proceeding to first connect.");
                                    }
                                }
                                sendTroubleShoot($"Connect cycle: entryState={GetStateString(currentState)}, attempt={(trshoot ? trshootCount + 1 : 0)}");
                                if (currentState != State.Reconnecting)
                                {
                                    setStatus(State.Connecting);
                                }
                                DisconnectBLE();
                                bool connected = ConnectBLE(addressble);
                                if (connected)
                                {
                                    if (trshoot)
                                    {
                                        trshootCount = 0;
                                    }
                                    setStatus(State.Connected);
                                    ResetPacket();
                                    break;
                                }
                                if (trshoot)
                                {
                                    trshootCount++;
                                    if (trshootCount == 3)
                                    {
                                        sendTroubleShoot("Error! 3 attempts was made to connect device, no success connection.");
                                        sendTroubleShoot("QUIT");
                                        setStatus(State.Faulted);
                                        Thread.Sleep(500);
                                        thread_alive = false;
                                        break;
                                    }
                                }
                                setStatus(State.Reconnecting);
                                doDiscovery();
                            }
                            else
                            {
                                if (this.trshoot)
                                {
                                    sendTroubleShoot("QUIT");
                                }
                                setStatus(State.Faulted);
                                Thread.Sleep(500);
                                thread_alive = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
                            DisconnectBLE();
                            Thread.Sleep(500);
                        }
                        break;

                    case State.Resetting:
                        try
                        {
                            if (!thread_alive)
                            {
                                break;
                            }
                            if (!HasActiveConnection())
                            {
                                setStatus(State.Reconnecting);
                                break;
                            }
                            ResetPacket();
                            sendTroubleShoot("Send reset spectrum command");
                            WritePacket(RC_RESET_SPECTRUM);
                            Thread.Sleep(1000);
                            setStatus(State.Connected);
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
                            sendTroubleShoot($"Reset command failed: {ex.Message}");
                            setStatus(State.Reconnecting);
                        }
                        break;

                    case State.Calibration:
                        try
                        {
                            if (!thread_alive)
                            {
                                break;
                            }
                            if (!HasActiveConnection())
                            {
                                setStatus(State.Reconnecting);
                                break;
                            }
                            if (polynomialEnergyCalibration != null && !calibration_sent)
                            {
                                ResetPacket();
                                WriteCalibration(this.polynomialEnergyCalibration);
                                calibration_sent = true;
                                Trace.WriteLine("Calibration sent");
                            }
                            if (calibration_sent)
                            {
                                Thread.Sleep(200);
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Calibration write exception: {ex.Message} {ex.StackTrace}");
                            sendTroubleShoot($"Calibration write failed: {ex.Message}");
                            calibration_sent = false;
                            setStatus(State.Reconnecting);
                        }
                        break;

                    case State.CalibrationDone:
                        Thread.Sleep(200);
                        Trace.WriteLine($"State: CalibrationDone");
                        break;

                    case State.CalibrationFail:
                        Thread.Sleep(200);
                        Trace.WriteLine($"State: CalibrationFail");
                        break;

                    case State.Connected:
                    case State.Recording:
                        try
                        {
                            if (!thread_alive)
                            {
                                break;
                            }
                            if (!HasActiveConnection())
                            {
                                setStatus(State.Reconnecting);
                                break;
                            }
                            if (calibration)
                            {
                                setStatus(State.Calibration);
                                break;
                            }
                            ResetPacket();
                            sendTroubleShoot("Send get Spectrum command");
                            WritePacket(RC_GET_SPECTRUM);
                            int counter = 0;
                            while (true)
                            {
                                bool complete;
                                bool broken;
                                State pollingState;
                                lock (packetLock)
                                {
                                    complete = packet.COMPLETE;
                                    broken = packet.BROKEN;
                                }
                                lock (stateLock)
                                {
                                    pollingState = state;
                                }
                                if (complete || broken || !thread_alive || (pollingState != State.Connected && pollingState != State.Recording))
                                {
                                    break;
                                }
                                Thread.Sleep(200);
                                counter++;
                                if (counter >= 38)
                                {
                                    lock (packetLock)
                                    {
                                        packet.BROKEN = true;
                                        sendTroubleShoot($"Spectrum receive timeout. total={packet.counter}");
                                    }
                                    setStatus(State.Reconnecting);
                                }
                            }
                            if (!thread_alive)
                            {
                                break;
                            }
                            int[] spectrum = null;
                            int elapsedTime = 0;
                            lock (packetLock)
                            {
                                State readyState;
                                lock (stateLock)
                                {
                                    readyState = state;
                                }
                                if (packet.BROKEN || (readyState != State.Connected && readyState != State.Recording) || packet.SPECTRUM == null)
                                {
                                    break;
                                }
                                spectrum = packet.SPECTRUM.ToArray();
                                elapsedTime = (int)packet.TIME_S;
                            }
                            sendTroubleShoot("Packet recieved");
                            sendTroubleShoot($"Spectrum real time: {elapsedTime}");
                            sendTroubleShoot($"Spectrum calibration: A0={packet.A0} A1={packet.A1} A2={packet.A2}");
                            spectrum.CopyTo(hystogram_buffered, 0);
                            long sum = hystogram_buffered.Sum(i => (long)i);
                            if (elapsedTime != 0)
                            {
                                lock (connectionLock)
                                {
                                    cps = sum / (double)elapsedTime;
                                }
                            }
                            sendTroubleShoot($"Spectrum cps: {cps}");
                            sendTroubleShoot($"Spectrum total counts: {sum}");
                            if (currentState != State.Recording)
                            {
                                setStatus(State.Recording);
                            }
                            if (this.trshoot)
                            {
                                sendTroubleShoot("QUIT");
                                Thread.Sleep(500);
                                thread_alive = false;
                                break;
                            }
                            RaiseDataReady(new RadiaCodeInDataReadyArgs(hystogram_buffered, elapsedTime, sum));
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Spectrum polling exception: {ex.Message} {ex.StackTrace}");
                            sendTroubleShoot($"Spectrum polling exception: {ex.Message}");
                            setStatus(State.Reconnecting);
                        }
                        break;

                    case State.Faulted:
                        Thread.Sleep(500);
                        break;
                }
            }
            Trace.WriteLine("RadiaCodeIn thread stopped " + guid);
            sendTroubleShoot($"RadiaCodeIn thread stopped {guid}");
        }

        public double CPS
        {
            get
            {
                lock (connectionLock)
                {
                    return cps;
                }
            }
        }

        public void Dispose()
        {
            if (readerThread != null)
            {
                Trace.WriteLine("RadiaCodeIn thread termination request");
                thread_alive = false;
                setStatus(State.Stopped);
                Trace.WriteLine("Try to disconnect..");
                DisconnectBLE();
                if (readerThread.IsAlive)
                {
                    readerThread.Join(5000);
                }
                if (discoveryThread != null && discoveryThread.IsAlive)
                {
                    discoveryThread.Join(5000);
                }
            }
        }

        private void DisconnectBLE()
        {
            // Diagnostics (troubleshoot form): the reader thread always calls DisconnectBLE before
            // each ConnectBLE, so on the very first Start this "reset" tears down nothing. Report
            // whether an actual connection existed and its last status, so a real drop is told
            // apart from the routine pre-connect cleanup.
            if (trshoot)
            {
                bool hadDevice, hadService, hadWrite, hadNotify;
                string connStatus = "n/a";
                State reasonState;
                lock (connectionLock)
                {
                    hadDevice = dev != null;
                    hadService = service != null;
                    hadWrite = characteristic != null;
                    hadNotify = characteristicNotify != null;
                    if (dev != null)
                    {
                        try { connStatus = dev.ConnectionStatus.ToString(); } catch (Exception) { connStatus = "unavailable"; }
                    }
                }
                lock (stateLock)
                {
                    reasonState = state;
                }
                if (hadDevice)
                {
                    sendTroubleShoot($"Disconnect BLE service: tearing down existing connection (state={GetStateString(reasonState)}, connStatus={connStatus}, service={hadService}, write={hadWrite}, notify={hadNotify})");
                }
                else
                {
                    sendTroubleShoot($"Disconnect BLE service: no active connection (routine cleanup before connect, state={GetStateString(reasonState)})");
                }
            }
            else
            {
                sendTroubleShoot("Disconnect BLE service");
            }
            Trace.WriteLine("Disconnect BLE service");
            BluetoothLEDevice deviceToDispose = null;
            GattDeviceService serviceToDispose = null;
            GattCharacteristic notifyToDetach = null;
            try
            {
                lock (connectionLock)
                {
                    notifyToDetach = characteristicNotify;
                    characteristicNotify = null;
                    characteristic = null;
                    serviceToDispose = service;
                    service = null;
                    deviceToDispose = dev;
                    dev = null;
                    cps = 0.0;
                }
                calibration_sent = false;
                ResetPacket();
                DisposeConnectionResources(deviceToDispose, serviceToDispose, notifyToDetach, detachDeviceEvent: true);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Exception during disconnect: {ex.Message} {ex.StackTrace}");
            }
        }
    }

    public class RadiaCodeInDataReadyArgs : EventArgs
    {
        private readonly int[] hystogram;
        private readonly int elapsed_time;
        private readonly long sum;

        public int[] Hystogram
        {
            get { return hystogram; }
        }

        public long SUM
        {
            get { return sum; }
        }

        public int ElapsedTime
        {
            get { return elapsed_time; }
        }

        public RadiaCodeInDataReadyArgs(int[] hyst, int elapsed_time, long sum)
        {
            this.hystogram = new int[hyst.Length];
            hyst.CopyTo(this.hystogram, 0);
            this.elapsed_time = elapsed_time;
            this.sum = sum;
        }
    }

    public class RadiaCodeTroubleShootArgs : EventArgs
    {
        private string text = "";

        public string Text
        {
            get { return text; }
        }

        public RadiaCodeTroubleShootArgs(string text)
        {
            this.text = text;
        }
    }

    public class RadiaCodeStatusArgs : EventArgs
    {
        private string status = "Unknown";

        public string Status
        {
            get { return status; }
        }

        public RadiaCodeStatusArgs(string status)
        {
            this.status = status;
        }
    }

    public class RCSpectrum
    {
        uint Time_s;
        float a0, a1, a2;
        int[] Spectrum;
        int size;
        public byte[] buffer;
        public int counter = 0;
        bool newPacket = true;
        bool complete = false;
        bool broken = false;

        public bool BROKEN
        {
            get { return this.broken; }
            set { this.broken = value; }
        }

        public bool COMPLETE
        {
            get { return this.complete; }
            set { this.complete = value; }
        }

        public uint TIME_S
        {
            get { return this.Time_s; }
            set { this.Time_s = value; }
        }

        public float A0
        {
            get { return this.a0; }
            set { this.a0 = value; }
        }

        public float A1
        {
            get { return this.a1; }
            set { this.a1 = value; }
        }

        public float A2
        {
            get { return this.a2; }
            set { this.a2 = value; }
        }

        public int[] SPECTRUM
        {
            get { return this.Spectrum; }
            set { this.Spectrum = value; }
        }

        public bool NEWPACKET
        {
            get { return this.newPacket; }
            set { this.newPacket = value; }
        }

        public int SIZE
        {
            get { return this.size; }
            set { this.size = value; }
        }

        public RCSpectrum() { }

        public List<float> GetCalibration()
        {
            return new List<float> { this.A0, this.A1,  this.A2 };
        }

        public void DecodePacket()
        {
            Time_s = BitConverter.ToUInt32(buffer, 16);
            a0 = BitConverter.ToSingle(buffer, 20);
            a1 = BitConverter.ToSingle(buffer, 24);
            a2 = BitConverter.ToSingle(buffer, 28);
            // ZipData spectrum starts from index = 32
            int last_value = 0;
            int result;
            List<int> sp = new List<int>();
            int i = 32;
            while (i < SIZE)
            {
                ushort position = (ushort)(((buffer[i + 1] & 0xFF) << 8) | (buffer[i] & 0xFF));
                i += 2;
                int count_occurences = (position >> 4) & 0x0FFF;
                int var_length = position & 0x0F;
                // Trace.WriteLine($"position {position}, count_occurences {count_occurences}, var_length {var_length},  last_value {last_value}, size {SIZE - i}");
                for (int j = 0; j < count_occurences; j++)
                {
                    switch (var_length)
                    {
                        case 0:
                            result = 0; break;
                        case 1:
                            result = (buffer[i] & 0xFF); i += 1; break;
                        case 2:
                            result = last_value + (sbyte)(buffer[i] & 0xFF); i += 1; break;
                        case 3:
                            result = last_value + (short)(((buffer[i + 1] & 0xFF) << 8) | (buffer[i] & 0xFF)); i += 2; break;
                        case 4:
                            {
                                // MDID_S24: signed 24-bit delta. Per the device firmware
                                // coder (RCSpectrumCoder::Decode_S24) exactly three bytes
                                // are consumed per value, with no padding after the block.
                                // Note: the spec's example decoder has a spurious
                                // "ZipPrm.SrcPtr = ++src" (extra byte skip) — that is a
                                // documentation bug, do not replicate it.
                                int diff24 = buffer[i]
                                    | (buffer[i + 1] << 8)
                                    | ((sbyte)buffer[i + 2] << 16);
                                result = unchecked(last_value + diff24);
                                i += 3;
                                break;
                            }
                        case 5:
                            // MDID_U32 is an absolute value, not a delta. This follows the
                            // device reference decoder's Decode_U32 implementation.
                            result = ((buffer[i + 3] & 0xFF) << 24) | ((buffer[i + 2] & 0xFF) << 16) | ((buffer[i + 1] & 0xFF) << 8) | (buffer[i] & 0xFF); i += 4; break;
                        default:
                            throw new Exception("Wtf");
                    }
                    last_value = result;
                    sp.Add(result);
                }
            }
            Spectrum = sp.ToArray();
        }
    }
}
