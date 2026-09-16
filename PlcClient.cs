using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PLCCompare
{
    /// <summary>
    /// Handles the TCP/ASCII MC-protocol connection to the PLC.
    /// Kept separate from FrameController so the scanning/comparison logic
    /// doesn't need to know anything about socket framing or protocol details.
    /// </summary>
    public class PlcClient
    {
        private readonly string ip;
        private readonly int port;
        private readonly object plcLock = new object();
        private TcpClient client;

        // Optional hook so the caller can surface connection/write messages
        // in its own UI/log instead of this class hardcoding Console.WriteLine.
        public Action<string> Logger { get; set; }

        public PlcClient(string ip, int port)
        {
            this.ip = ip;
            this.port = port;
        }

        private void Log(string message) => Logger?.Invoke(message);

        /// <summary>
        /// Connects to the PLC on a background thread so callers (e.g. a
        /// constructor running on the UI thread) never block on this.
        /// </summary>
        private volatile bool keepRunning = false;
        private Thread monitorThread;

        private volatile bool isConnectedFlag = false;

        public bool IsConnected => isConnectedFlag; // instant, lock-free — safe to call from the UI thread anytime

        /// <summary>
        /// Starts the connection and a background monitor thread that keeps checking
        /// health and auto-reconnecting forever — whether the PLC was unreachable at
        /// startup, or drops mid-session. Call this once instead of Connect().
        /// </summary>
        public void StartAutoConnect(int retryIntervalMs = 3000)
        {
            keepRunning = true;
            monitorThread = new Thread(() =>
            {
                while (keepRunning)
                {
                    bool currentlyConnected;
                    lock (plcLock)
                    {
                        currentlyConnected = client != null && client.Connected && IsSocketReallyConnected();
                    }
                    isConnectedFlag = currentlyConnected;

                    if (!currentlyConnected)
                    {
                        TryConnectOnce(); // still holds plcLock internally, but only this
                                        // background thread ever waits on it now
                    }
                    Thread.Sleep(retryIntervalMs);
                }
            });
            monitorThread.IsBackground = true;
            monitorThread.Start();
        }

        private void TryConnectOnce()
        {
            lock (plcLock)
            {
                try
                {
                    client?.Close(); // drop any stale/half-dead socket before retrying
                    client = new TcpClient();
                    var result = client.BeginConnect(ip, port, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(3000, true);

                    if (success && client.Connected)
                    {
                        client.EndConnect(result);
                        client.ReceiveTimeout = 2000;
                        client.SendTimeout = 2000;
                        // OS-level keepalive — makes the socket stack itself probe the
                        // connection periodically, so a silently-dead peer (cable pull,
                        // PLC power loss) gets detected instead of looking "connected" forever.
                        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                        isConnectedFlag = true;

                        // Customize the keepalive timing so a hard disconnect (cable pull, power loss) in few second
                        byte[] keepAliveValues = new byte[12];
                        BitConverter.GetBytes(1).CopyTo(keepAliveValues, 0);    // enable
                        BitConverter.GetBytes(1000).CopyTo(keepAliveValues, 4); // time before first probe (ms)
                        BitConverter.GetBytes(1000).CopyTo(keepAliveValues, 8); // interval between probes (ms)
                        client.Client.IOControl(IOControlCode.KeepAliveValues, keepAliveValues, null);
                        Log("Connected to PLC.");
                    }
                    else
                    {
                        Log("PLC connection timed out — will retry.");
                    }
                }
                catch (Exception ex)
                {
                    Log("PLC connection failed: " + ex.Message + " — will retry.");
                }
            }
        }

        /// <summary>
        /// Non-blocking check for whether the socket is genuinely still alive.
        /// client.Connected alone is unreliable — it only reflects the result of the
        /// last send/receive, not the socket's current real state.
        /// </summary>
        private bool IsSocketReallyConnected()
        {
            try
            {
                if (client?.Client == null) return false;
                var socket = client.Client;
                bool readable = socket.Poll(0, SelectMode.SelectRead);
                bool noData = socket.Available == 0;
                // Poll returns true+Available==0 specifically when the remote end closed —
                // that combination is the standard trick for detecting a dead TCP peer.
                if (readable && noData) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Stop()
        {
            keepRunning = false;
        }

        /// <summary>
        /// Writes a single bit device (e.g. M100) via MC Protocol 3E Frame ASCII.
        /// The socket write blocks the calling thread — use PulseBit (or your
        /// own background thread) if calling this from the UI thread.
        /// </summary>
        public void WriteBitAscii(string deviceType, int address, bool value)
        {
            if (!IsConnected)
            {
                Log("PLC not connected — attempting immediate reconnect before write.");
                TryConnectOnce();
            }

            if (!IsConnected)
            {
                Log("PLC still not connected — skipping bit write.");
                return;
            }

            bool success = TryWriteOnce(deviceType, address, value);

            if (!success)
            {
                // The write failed even though IsConnected looked fine beforehand —
                // this is the classic symptom of a PLC that silently closes the socket
                // after each command. Force a fresh reconnect and retry once immediately,
                // instead of waiting for the background monitor's next cycle.
                Log("Write failed — forcing reconnect and retrying once.");
                TryConnectOnce();
                if (IsConnected)
                {
                    TryWriteOnce(deviceType, address, value);
                }
            }
        }

        // Writing Bit Format 
        private bool TryWriteOnce(string deviceType, int address, bool value)
        {
            lock (plcLock)
            {
                try
                {
                    NetworkStream stream = client.GetStream();

                    string subheader = "5000";
                    string networkNo = "00";
                    string plcNo = "FF";
                    string ioNo = "03FF";
                    string stationNo = "00";
                    string cpuTimer = "0010";
                    string command = "1401";
                    string subcommand = "0001";
                    string deviceCode = deviceType.PadRight(2, '*');
                    string headAddress = address.ToString("D6");
                    string numberOfPoints = "0001";
                    string bitValue = value ? "1" : "0";

                    string contentData = cpuTimer + command + subcommand + deviceCode + headAddress + numberOfPoints + bitValue;
                    string dataLength = contentData.Length.ToString("X4");
                    string asciiPayload = subheader + networkNo + plcNo + ioNo + stationNo + dataLength + contentData;

                    byte[] sendBytes = Encoding.ASCII.GetBytes(asciiPayload);
                    stream.Write(sendBytes, 0, sendBytes.Length);
                    stream.Flush();

                    byte[] responseBuffer = new byte[1024];
                    int bytesRead = stream.Read(responseBuffer, 0, responseBuffer.Length);
                    string responseString = Encoding.ASCII.GetString(responseBuffer, 0, bytesRead);

                    if (responseString.Length < 22)
                            throw new Exception("Incomplete ASCII response from PLC.");

                    string endCode = responseString.Substring(18, 4);
                    if (endCode != "0000")
                        throw new Exception($"PLC returned error code: {endCode}");

                    return true;
                    }
                catch (Exception ex)
                {
                    Log($"PLC write failed ({deviceType}{address}): {ex.Message}");
                    return false;
                }
            }
        }
        public void SetBit(string deviceType, int address, bool value)
        {
            var writeThread = new Thread(() => WriteBitAscii(deviceType, address, value));
            writeThread.IsBackground = true;
            writeThread.Start();
        }

        // Writing word format
        public void WriteWordsAscii(string deviceType, int startAddress, ushort[] values)
        {
            if (!IsConnected)
            {
                Log("PLC not connected — attempting immediate reconnect before write.");
                TryConnectOnce();
            }

            if (!IsConnected)
            {
                Log("PLC still not connected — skipping word write.");
                return;
            }

            bool success = TryWriteWordsOnce(deviceType, startAddress, values);

            if (!success)
            {
                Log("Word write failed — forcing reconnect and retrying once.");
                TryConnectOnce();
                if (IsConnected)
                {
                    TryWriteWordsOnce(deviceType, startAddress, values);
                }
            }
        }

        private bool TryWriteWordsOnce(string deviceType, int startAddress, ushort[] values)
        {
            lock (plcLock)
            {
                try
                {
                    NetworkStream stream = client.GetStream();

                    string subheader = "5000";
                    string networkNo = "00";
                    string plcNo = "FF";
                    string ioNo = "03FF";
                    string stationNo = "00";
                    string cpuTimer = "0010";
                    string command = "1401";     // Batch write
                    string subcommand = "0000";  // Word units (0001 was bit units)
                    string deviceCode = deviceType.PadRight(2, '*');
                    string headAddress = startAddress.ToString("D6");
                    string numberOfPoints = values.Length.ToString("X4");

                    var dataBuilder = new StringBuilder();
                    foreach (ushort value in values)
                    {
                        dataBuilder.Append(value.ToString("X4")); // each word as 4-digit hex ASCII
                    }

                    string contentData = cpuTimer + command + subcommand + deviceCode + headAddress + numberOfPoints + dataBuilder;
                    string dataLength = contentData.Length.ToString("X4");
                    string asciiPayload = subheader + networkNo + plcNo + ioNo + stationNo + dataLength + contentData;

                    byte[] sendBytes = Encoding.ASCII.GetBytes(asciiPayload);
                    stream.Write(sendBytes, 0, sendBytes.Length);
                    stream.Flush();

                    byte[] responseBuffer = new byte[1024];
                    int bytesRead = stream.Read(responseBuffer, 0, responseBuffer.Length);
                    string responseString = Encoding.ASCII.GetString(responseBuffer, 0, bytesRead);

                    if (responseString.Length < 22)
                        throw new Exception("Incomplete ASCII response from PLC.");

                    string endCode = responseString.Substring(18, 4);
                    if (endCode != "0000")
                        throw new Exception($"PLC returned error code: {endCode}");

                    return true;
                }
                catch (Exception ex)
                {
                    Log($"PLC word write failed ({deviceType}{startAddress}): {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Writes a text string into a block of word (D register) devices, one ASCII
        /// character per register, starting at startAddress. Registers beyond the text's
        /// length are cleared to 0, so a shorter new value fully replaces a longer old one
        /// with no leftover characters.
        /// </summary>
        public void WritePlcWordAscii(string deviceType, int startAddress, string text, int registerCount)
        {
            ushort[] values = new ushort[registerCount];

            for (int i = 0; i < registerCount; i++)
            {
                values[i] = i < text.Length ? (ushort)text[i] : (ushort)0;
            }

            var writeThread = new Thread(() => WriteWordsAscii(deviceType, startAddress, values));
            writeThread.IsBackground = true;
            writeThread.Start();
        }

        public void Close()
        {
            Stop();
            try
            {
                client?.Close();
            }
            catch (Exception ex)
            {
                Log("Error closing PLC connection: " + ex.Message);
            }
        }
    }
}