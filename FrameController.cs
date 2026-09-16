using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Ports;
using System.Threading;
using System.Windows.Forms;

namespace PLCCompare
{
    // Equivalent of Java's "@FunctionalInterface ScanHandler" — a delegate is
    // C#'s version of a single-method functional interface.
    internal delegate void ScanHandler(string scannedValue);

    public class FrameController
    {
        private readonly MainForm ui;
        private readonly PlcClient plc;

        // TODO: adjust these to whatever bit addresses your ladder program expects

        private int count1 = 0; // Batch No and Rank 1 compare counter
        private int count2 = 0; // Batch No and Rank 2 compare counter

        private readonly DataLogger rank1DataLogger;
        private readonly DataLogger rank2DataLogger;

        private readonly List<SerialPort> openPorts = new List<SerialPort>();
        private readonly List<string> failedPorts = new List<string>();
        private readonly List<PendingPort> retryPendings = new List<PendingPort>();

        // For preventing duplicate data input
        private readonly Dictionary<string, string> lastValueByPort = new Dictionary<string, string>();
        private readonly Dictionary<string, long> lastTimeByPort = new Dictionary<string, long>();

        // Guards the three lists above, since they're now touched from both the UI thread
        // and background retry/reader threads (same fix we applied on the Java side).
        private readonly object portListLock = new object();

        private const string BatchNoPort = "COM3";
        private const string Rank1Port = "COM5";
        private const string Rank2Port = "COM6";

         public FrameController(MainForm ui)
        {
            this.ui = ui;

            rank1DataLogger = new DataLogger(@"C:\CompareResult", "Rank1");
            rank2DataLogger = new DataLogger(@"C:\CompareResult", "Rank2");

            rank1DataLogger.OnFileError = ShowMissingLoggingFileWarning;
            rank2DataLogger.OnFileError = ShowMissingLoggingFileWarning;

            WireResetButton();
            SetupClosePortsOnExit();

            // Replace with actual scanner COM ports
            SetupScannerPort(BatchNoPort, ui.text1, ui.text2, OnBatchNoReceived); // Batch No scanner
            SetupScannerPort(Rank1Port, ui.text3, ui.text4, OnRank1Received);   // Rank 1 scanner
            SetupScannerPort(Rank2Port, ui.text5, ui.text6, OnRank2Received);   // Rank 2 scanner

            // TODO: replace with your actual PLC IP/port
            plc = new PlcClient("192.168.100.5", 5001);
            plc.Logger = msg => Console.WriteLine(msg); // swap for ui.status.Text = msg if you want it on-screen
            plc.StartAutoConnect();

            ConnectionStatus(); // Status of COM port
            StartPortRetryTimer();
            StartPlcStatusTimer();
        }

        // ---------- Function 1: Batch No ----------

        private void OnBatchNoReceived(string value)
        {
            HandleScan(ui.text1, ui.text2, value, ResetCount2);
        }

        private void ResetCount2()
        {
            count2 = 0;
            ui.count2.Text = "0";
            plc.WritePlcWordAscii("D", 66, "0", 6);
            plc.WritePlcWordAscii("D", 0, ui.text2.Text, 20);
        }

        // ---------- Function 2: Rank 1 ----------

        private void OnRank1Received(string value)
        {
            if (!IsBatchNoScanned() || !IsPlcConnected())
            {
                return; // Block the scan, no compare
            }
            HandleScan(ui.text3, ui.text4, value, CompareRank1);
        }

        private void CompareRank1()
        {
            string batchNo = ui.text2.Text;
            string rank1 = ui.text4.Text;
            string status;
            bool isMatch = batchNo == rank1;

            if (batchNo == rank1)
            {
                status = "OK";
                ui.condition1.Text = status;
                FlashCondition(ui.condition1, status, Color.Green);
                TriggerRank1Output(isMatch);
                plc.WritePlcWordAscii("D", 20, rank1, 20);
                plc.WritePlcWordAscii("D",80, status, 3);
            }
            else
            {
                status = "NG";
                ui.condition1.Text = status;
                FlashCondition(ui.condition1, status, Color.Red);
                TriggerRank1Output(isMatch);
                plc.WritePlcWordAscii("D", 20, rank1, 20);
                plc.WritePlcWordAscii("D",80, status, 3);
            }

            count1++;
            ui.count1.Text = count1.ToString();
            plc.WritePlcWordAscii("D", 60, count1.ToString(), 6);
            rank1DataLogger.LogComparison(batchNo, rank1, status);
        }

        // ---------- Function 3: Rank 2 ----------

        private void OnRank2Received(string value)
        {
            if (!IsBatchNoScanned() || !IsPlcConnected())
            {
                return; // Block the scan, no compare
            }

            HandleScan(ui.text5, ui.text6, value, CompareRank2);
        }

        private void CompareRank2()
        {
            string batchNo = ui.text2.Text;
            String truncateBatchNo = batchNo.Length > 5 ? batchNo.Substring(0, batchNo.Length - 3) : batchNo;
            string rank2 = ui.text6.Text;
            String truncateRank2 = rank2.Length > 5 ? rank2.Substring(0, rank2.Length - 3) : rank2;
            string status;
            bool isMatch = truncateBatchNo == truncateRank2;

            if (isMatch)
            {
                status = "OK";
                FlashCondition(ui.condition2, status, Color.Green);
                TriggerRank2Output(isMatch);
                plc.WritePlcWordAscii("D", 40, rank2, 20);
                plc.WritePlcWordAscii("D",86, status, 3);
            }
            else
            {
                status = "NG";
                FlashCondition(ui.condition2, status, Color.Red);
                TriggerRank2Output(isMatch);
                plc.WritePlcWordAscii("D", 40, rank2, 20);
                plc.WritePlcWordAscii("D",86, status, 3);
            }

            count2++;
            ui.count2.Text = count2.ToString();
            plc.WritePlcWordAscii("D", 66, count2.ToString(), 6);
            rank2DataLogger.LogComparison(batchNo, rank2, status);
        }

        private void FlashCondition(Label box, string newText, Color newColor)
        {
            box.Text = "";
            box.BackColor = Color.White;

            var flashTimer = new System.Windows.Forms.Timer { Interval = 150 };
            flashTimer.Tick += (s, e) =>
            {
                flashTimer.Stop();
                flashTimer.Dispose();
                box.Text = newText;
                box.ForeColor = Color.Black;
                box.BackColor = newColor;
                box.Parent.BackColor = newColor;
            };
            flashTimer.Start();
        }

        private void TriggerRank1Output(bool isMatch)
        {
            // Force both bits OFF first, so any downstream ladder logic watching for
            // a rising edge sees a genuine OFF -> ON transition every time — even if
            // the result is identical to the previous comparison (OK->OK or NG->NG).
            plc.SetBit("M*", 0, false);
            plc.SetBit("M*", 1, false);

            var resetTimer = new System.Windows.Forms.Timer { Interval = 100 };
            resetTimer.Tick += (s, e) =>
            {
                resetTimer.Stop();
                resetTimer.Dispose();
                plc.SetBit("M*", 0, isMatch);
                plc.SetBit("M*", 1, !isMatch);
            };
            resetTimer.Start();
        }

        private void TriggerRank2Output(bool isMatch)
        {
            plc.SetBit("M*", 2, false);
            plc.SetBit("M*", 3, false);

            var resetTimer = new System.Windows.Forms.Timer { Interval = 100 };
            resetTimer.Tick += (s, e) =>
            {
                resetTimer.Stop();
                resetTimer.Dispose();
                plc.SetBit("M*", 2, isMatch);
                plc.SetBit("M*", 3, !isMatch);
            };
            resetTimer.Start();
        }

        // ---------- Shared: Received -> Current transfer ----------

        // Equivalent of SwingUtilities.invokeLater(...) — marshals the given action onto
        // the UI thread, whether called from the UI thread itself or a background thread.
        private void SafeInvoke(Control control, Action action)
        {
            if (control.InvokeRequired)
            {
                control.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        private void HandleScan(TextBox receivedField, TextBox currentField, string scannedValue, Action afterUpdate)
        {
            SafeInvoke(receivedField, () =>
            {
                try
                {
                    receivedField.Text = scannedValue;

                    // One-shot delayed transfer, equivalent to Java's Timer(300, ...) with setRepeats(false).
                    var transferTimer = new System.Windows.Forms.Timer { Interval = 300 };
                    transferTimer.Tick += (s, e) =>
                    {
                        transferTimer.Stop();
                        transferTimer.Dispose();
                        currentField.Text = scannedValue;
                        receivedField.Text = "";
                        afterUpdate?.Invoke();
                    };
                    transferTimer.Start();
                }
                catch (Exception)
                {
                    receivedField.Text = "Error";

                    var errorTimer = new System.Windows.Forms.Timer { Interval = 300 };
                    errorTimer.Tick += (s, e) =>
                    {
                        errorTimer.Stop();
                        errorTimer.Dispose();
                        currentField.Text = "Error";
                        receivedField.Text = "";
                    };
                    errorTimer.Start();
                }
            });
        }

        // ---------- Reset button ----------

        private void WireResetButton()
        {
            ui.reset.Click += (s, e) =>
            {
                count1 = 0;
                ui.count1.Text = "0";
                plc.WritePlcWordAscii("D", 60, "0", 6);
            };
        }

        // ---------- Serial port setup ----------

        // Sets up a COM port; tracks it as failed/pending-retry if it can't be opened.
        private void SetupScannerPort(string portName, TextBox receivedField, TextBox currentField, ScanHandler handler)
        {
            var port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One);
            port.ReadTimeout = 500;
            port.NewLine = "\r\n";
            port.DtrEnable = true; // ← add this — many devices only assert DSR back once they see our DTR go high
            port.RtsEnable = true; // ← add this too — some devices key off RTS/CTS instead, cheap to enable both

            try
            {
                port.Open();
            }
            catch (Exception)
            {
                lock (portListLock)
                {
                    if (!failedPorts.Contains(portName))
                    {
                        failedPorts.Add(portName);
                        retryPendings.Add(new PendingPort(portName, receivedField, currentField, handler));
                    }
                }
                ConnectionStatus();
                return;
            }

            lock (portListLock)
            {
                openPorts.Add(port);
            }
            StartReaderThread(port, portName, receivedField, currentField, handler);
        }

        private void StartReaderThread(SerialPort port, string portName, TextBox receivedField, TextBox currentField, ScanHandler handler)
        {
            var readerThread = new Thread(() =>
            {
                while (port.IsOpen)
                {
                    string value;

                    try
                    {
                        value = port.ReadLine();
                    }
                    catch (TimeoutException)
                    {
                        if (!IsPortStillPresent(portName))
                        {
                            break;
                        }
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Scan error on " + portName + ": " + ex.Message);

                        Thread.Sleep(300);

                        if (!IsPortStillPresent(portName))
                        {
                            break;
                        }
                        TriggerError(portName, handler);
                        continue;
                    }

                    value = value?.Trim();

                    if (string.IsNullOrEmpty(value))
                    {
                        TriggerError(portName, handler);
                        continue;
                    }

                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    lastValueByPort.TryGetValue(portName, out string lastValue);
                    bool hasLastTime = lastTimeByPort.TryGetValue(portName, out long lastTime);

                    bool isDuplicate = value == lastValue && hasLastTime && (now - lastTime) < 100;

                    if (!isDuplicate)
                    {
                        lastValueByPort[portName] = value;
                        lastTimeByPort[portName] = now;
                        handler(value);
                    }
                }

                HandlePortDisconnected(port, portName, receivedField, currentField, handler);
            });

            readerThread.IsBackground = true; // equivalent of setDaemon(true)
            readerThread.Start();
        }

        // Practical substitute for jSerialComm's disconnect event — see note above.
        private bool IsPortStillPresent(string portName)
        {
            foreach (string name in SerialPort.GetPortNames())
            {
                if (string.Equals(name, portName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // Handle when the COM port is pulled out mid-session
        private void HandlePortDisconnected(SerialPort port, string portName, TextBox receivedField, TextBox currentField, ScanHandler handler)
        {
            lock (portListLock)
            {
                openPorts.Remove(port);
            }

            if (port.IsOpen)
            {
                port.Close();
            }

            lock (portListLock)
            {
                if (!failedPorts.Contains(portName))
                {
                    failedPorts.Add(portName);
                    retryPendings.Add(new PendingPort(portName, receivedField, currentField, handler));
                }
            }

            SafeInvoke(receivedField, () =>
            {
                receivedField.Text = "";
                currentField.Text = "";
            });

            ConnectionStatus();
        }

        // Handle "Error" when the COM port can't read a proper value
        private void TriggerError(string portName, ScanHandler handler)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool hasLastError = lastTimeByPort.TryGetValue(portName + "_error", out long lastErrorTime);
            bool isDuplicateError = hasLastError && (now - lastErrorTime) < 500;

            if (!isDuplicateError)
            {
                lastTimeByPort[portName + "_error"] = now;
                handler("Error");
            }
        }

        // ---------- Pending port nested class ----------
        private class PendingPort
        {
            public string PortName;
            public TextBox ReceivedField;
            public TextBox CurrentField;
            public ScanHandler Handler;

            public PendingPort(string portName, TextBox receivedField, TextBox currentField, ScanHandler handler)
            {
                PortName = portName;
                ReceivedField = receivedField;
                CurrentField = currentField;
                Handler = handler;
            }
        }

        // Show COM port connection status
        private void ConnectionStatus()
        {
            SafeInvoke(ui.status, () =>
            {
                List<string> failedCopy;
                lock (portListLock)
                {
                    failedCopy = new List<string>(failedPorts);
                }

                if (failedCopy.Count == 0)
                {
                    ui.status.Text = "All COM ports connected";
                    ui.status.ForeColor = Color.Green;
                }
                else
                {
                    string unopenPort = string.Join(",", failedCopy);
                    ui.status.Text = "Failed to open port: " + unopenPort;
                    ui.status.ForeColor = Color.Red;
                }
            });
        }


        // Timer to recheck the status of failed COM port connections.
        // IMPROVEMENT carried over from our recent Java lag-debugging: the actual retry work
        // (opening ports) runs on a background thread, NOT directly in the Timer's Tick —
        // otherwise repeatedly probing missing ports would freeze the UI thread, exactly like
        // the lag issue we tracked down before.
        private void StartPortRetryTimer()
        {
            var retryTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            retryTimer.Tick += (s, e) =>
            {
                var retryThread = new Thread(RetryFailedPorts);
                retryThread.IsBackground = true;
                retryThread.Start();
            };
            retryTimer.Start();
        }

        // Reconnect failed COM ports (runs on a background thread — see note above)
        private void RetryFailedPorts()
        {
            List<PendingPort> currentPending;
            lock (portListLock)
            {
                if (retryPendings.Count == 0)
                {
                    return;
                }
                currentPending = new List<PendingPort>(retryPendings);
            }

            var stillFailed = new List<PendingPort>();

            foreach (PendingPort pending in currentPending)
            {
                var port = new SerialPort(pending.PortName, 115200, Parity.None, 8, StopBits.One);
                port.ReadTimeout = 500;
                port.NewLine = "\r\n";
                port.DtrEnable = true; // ← add this — many devices only assert DSR back once they see our DTR go high
                port.RtsEnable = true; // ← add this too — some devices key off RTS/CTS instead, cheap to enable both

                try
                {
                    port.Open();
                    lock (portListLock)
                    {
                        openPorts.Add(port);
                        failedPorts.Remove(pending.PortName);
                    }
                    StartReaderThread(port, pending.PortName, pending.ReceivedField, pending.CurrentField, pending.Handler);
                }
                catch (Exception)
                {
                    stillFailed.Add(pending);
                }
            }

            lock (portListLock)
            {
                retryPendings.Clear();
                retryPendings.AddRange(stillFailed);
            }

            ConnectionStatus(); // refresh the label after every retry attempt
        }

        private int loggingFileWarningVisible = 0;

        // Data Logger error pop out window
        private void ShowMissingLoggingFileWarning()
        {
            if (Interlocked.CompareExchange(ref loggingFileWarningVisible, 1, 0) == 0)
            {
                SafeInvoke(ui, () =>
                {
                    MessageBox.Show(ui, "Missing logging File", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Interlocked.Exchange(ref loggingFileWarningVisible, 0);
                });
            }
        }

         // Rank 1 and 2 cannot have a value before Batch No is scanned
        private int batchNoWarningVisible = 0; // 0 = not showing, 1 = currently showing

        private bool IsBatchNoScanned()
        {
            string batchNo = ui.text2.Text;
            if (string.IsNullOrWhiteSpace(batchNo))
            {
                // Atomically check-and-set: only the thread that successfully flips
                // 0 -> 1 gets to show the dialog. Any other thread calling this while
                // one is already open just sees 1 and skips showing a second one.
                if (Interlocked.CompareExchange(ref batchNoWarningVisible, 1, 0) == 0)
                {
                    SafeInvoke(ui, () =>
                    {
                        MessageBox.Show(ui, "Batch No cannot be blank", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        Interlocked.Exchange(ref batchNoWarningVisible, 0); // reset once the user closes it
                    });
                }
                return false;
            }
            return true;
        }

        // Rank 1 and 2 cannot compare while the PLC is disconnected
        private int plcWarningVisible = 0; // 0 = not showing, 1 = currently showing

        private bool IsPlcConnected()
        {
            if (!plc.IsConnected)
            {
                if (Interlocked.CompareExchange(ref plcWarningVisible, 1, 0) == 0)
                {
                    SafeInvoke(ui, () =>
                    {
                        MessageBox.Show(ui, "Connect PLC to run the program", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        Interlocked.Exchange(ref plcWarningVisible, 0); // reset once the user closes it
                    });
                }
                return false;
            }
            return true;
        }

       private void SetupClosePortsOnExit()
        {
            ui.FormClosing += (s, e) =>
            {
                e.Cancel = true;
                ui.Hide();

                // Watchdog: guarantees the process actually terminates even if cleanup
                // hangs — a known issue with SerialPort.Close() when a background thread
                // is still blocked inside a read call on that same port.
                var watchdog = new Thread(() =>
                {
                    Thread.Sleep(3000);
                    Environment.Exit(0); // force-kill if graceful cleanup hasn't finished by now
                });
             watchdog.IsBackground = true;
                watchdog.Start();

                var cleanupThread = new Thread(() =>
                {
                    List<SerialPort> portsCopy;
                    lock (portListLock)
                    {
                        portsCopy = new List<SerialPort>(openPorts);
                    }

                    foreach (SerialPort port in portsCopy)
                    {
                        try
                        {
                            if (port.IsOpen)
                            {
                                port.Close();
                            }
                        }
                        catch
                        {
                            // ignore — the watchdog above will force-terminate if this hangs
                        }
                    }

                    try { plc.Close(); } catch { }
                    try { rank1DataLogger.Close(); } catch { }
                    try { rank2DataLogger.Close(); } catch { }

                    Environment.Exit(0); // guaranteed termination once cleanup actually finishes
                });
                cleanupThread.IsBackground = true;
                cleanupThread.Start();
            };
        }

        // Check PLC Status
        private void StartPlcStatusTimer()
        {
            var plcStatusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            plcStatusTimer.Tick += (s, e) => UpdatePlcStatus();
            plcStatusTimer.Start();
        }

        private void UpdatePlcStatus()
        {
            if (plc.IsConnected)
            {
                ui.plcStatus.Text = "PLC is connected";
                ui.plcStatus.ForeColor = Color.Green;
            }
            else
            {
                ui.plcStatus.Text = "PLC is not connected";
                ui.plcStatus.ForeColor = Color.Red;
            }
        }
    }
}