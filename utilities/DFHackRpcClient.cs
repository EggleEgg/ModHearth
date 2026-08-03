using System.Collections;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ModHearth.Utilities
{
    /// <summary> 
    /// For more information on the source visit https://github.com/DFHack/dfhack/blob/develop/library/RemoteServer.cpp 
    /// </summary>
    internal static class DFHackRpcClient
    {
        public static string? ExecuteDFHackCommandViaRpc(string command, List<string> args, string? dfFolderPath, out string error)
        {
            error = string.Empty;

            if (!DFMonitor.Shared.IsProcessRunning())
            {
                error = "Dwarf Fortress is not running";
                return null;
            }

            int port = ResolveDFHackPort(dfFolderPath);

            // This will be spammed if modhearth modlist changes exist but DF is not yet in the world creation screen
            if (DevMode.IsEnabled)
                Console.WriteLine($"DFHackRpcClient: Attempting RPC connection to 127.0.0.1:{port} for command \'{command}\'");

            try
            {
                using TcpClient client = new TcpClient();
                // Brief timeout to avoid blocking the UI if DF is not running or listening
                var connectTask = client.ConnectAsync("127.0.0.1", port);
                if (!connectTask.Wait(1000))
                {
                    error = "Connection timeout";
                    if (!DFMonitor.Shared.IsBooting())
                        Console.WriteLine($"DFHackRpcClient: {error}");
                    return null;
                }

                using NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 5000;
                stream.WriteTimeout = 5000;

                // 1. Handshake
                byte[] handshakeRequest = new byte[12];
                Encoding.ASCII.GetBytes("DFHack?\n").CopyTo(handshakeRequest, 0);
                BitConverter.GetBytes(1).CopyTo(handshakeRequest, 8); // Handshake version 1
                stream.Write(handshakeRequest, 0, handshakeRequest.Length);

                byte[] handshakeReply = ReadExactly(stream, 12);
                string magic = Encoding.ASCII.GetString(handshakeReply, 0, 8);
                int version = BitConverter.ToInt32(handshakeReply, 8);
                if (magic != "DFHack!\n" || version != 1)
                {
                    error = "Invalid handshake reply from DFHack";
                    Console.WriteLine($"DFHackRpcClient: {error}");
                    return null;
                }
                LogRpcClient($"Handshake successful. Magic: {magic}, Version: {version}");

                // 2. Serialize RunCommand request payload (CoreRunCommandRequest protobuf)
                byte[] rpcPayload = SerializeRunCommandRequest(command, args);

                // 3. Send RunCommand request header + payload
                byte[] requestHeader = new byte[8];
                BitConverter.GetBytes((short)1).CopyTo(requestHeader, 0); // Method ID 1: RunCommand
                BitConverter.GetBytes((short)0).CopyTo(requestHeader, 2); // Reserved / Padding
                BitConverter.GetBytes(rpcPayload.Length).CopyTo(requestHeader, 4); // Payload size

                stream.Write(requestHeader, 0, requestHeader.Length);
                if (rpcPayload.Length > 0)
                {
                    stream.Write(rpcPayload, 0, rpcPayload.Length);
                }
                LogRpcClient($"Sent RunCommand request. Command: {command}, Args: {string.Join(", ", args)}");

                // 4. Read response loop
                StringBuilder outputSb = new StringBuilder();
                while (true)
                {
                    byte[] responseHeader = ReadExactly(stream, 8);
                    short id = BitConverter.ToInt16(responseHeader, 0);
                    int size = BitConverter.ToInt32(responseHeader, 4);

                    byte[] payload = size > 0 ? ReadExactly(stream, size) : Array.Empty<byte>();

                    if (id == -3) // RPC_REPLY_TEXT (Payload is CoreTextNotification)
                    {
                        string text = DecodeTextNotification(payload);
                        _ = outputSb.Append(text);
                        LogRpcClient($"Received text fragment: {text.TrimEnd()}");
                    }
                    else if (id == -1) // RPC_REPLY_RESULT (success / completion)
                    {
                        LogRpcClient("Received RPC_REPLY_RESULT (Success).");
                        break;
                    }
                    else if (id == -2) // RPC_REPLY_FAIL
                    {
                        error = "DFHack RPC command execution failed";
                        Console.WriteLine($"DFHackRpcClient: {error}");
                        return null;
                    }
                    else
                    {
                        error = $"Unexpected DFHack RPC reply ID: {id}";
                        Console.WriteLine($"DFHackRpcClient: {error}");
                        return null;
                    }
                }

                return outputSb.ToString();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (!DFMonitor.Shared.IsBooting())
                {
                    Console.WriteLine($"DFHackRpcClient: Exception - {ex.Message}");
                }
                return null;
            }
        }

        private static byte[] SerializeRunCommandRequest(string command, List<string> args)
        {
            using MemoryStream ms = new MemoryStream();

            // Field 1: command (string) -> tag 0x0A (1 << 3 | 2)
            byte[] cmdBytes = Encoding.UTF8.GetBytes(command);
            ms.WriteByte(0x0A);
            WriteVarint(ms, cmdBytes.Length);
            ms.Write(cmdBytes, 0, cmdBytes.Length);

            // Field 2: arguments (repeated string) -> tag 0x12 (2 << 3 | 2)
            foreach (string arg in args)
            {
                byte[] argBytes = Encoding.UTF8.GetBytes(arg);
                ms.WriteByte(0x12);
                WriteVarint(ms, argBytes.Length);
                ms.Write(argBytes, 0, argBytes.Length);
            }

            return ms.ToArray();
        }

        private static void WriteVarint(Stream stream, int value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            stream.WriteByte((byte)value);
        }

        private static string DecodeTextNotification(byte[] payload)
        {
            StringBuilder sb = new StringBuilder();
            int index = 0;
            while (index < payload.Length)
            {
                int tag = ReadVarint(payload, ref index);
                int wireType = tag & 0x07;
                int fieldNumber = tag >> 3;

                switch (fieldNumber) // fragments (repeated message TextFragment)
                {
                    case 1 when wireType == 2:
                        {
                            int fragLen = ReadVarint(payload, ref index);
                            int fragEnd = index + fragLen;

                            // Inside TextFragment
                            while (index < fragEnd)
                            {
                                int fTag = ReadVarint(payload, ref index);
                                int fWireType = fTag & 0x07;
                                int fFieldNumber = fTag >> 3;

                                switch (fFieldNumber) // text (string)
                                {
                                    case 1 when fWireType == 2:
                                        {
                                            int textLen = ReadVarint(payload, ref index);
                                            string text = Encoding.UTF8.GetString(payload, index, textLen);
                                            _ = sb.Append(text);
                                            index += textLen;
                                            break;
                                        }

                                    default:
                                        SkipField(payload, ref index, fWireType);
                                        break;
                                }
                            }

                            break;
                        }

                    default:
                        SkipField(payload, ref index, wireType);
                        break;

                }
            }
            return sb.ToString();
        }

        private static int ReadVarint(byte[] data, ref int index)
        {
            int value = 0;
            int shift = 0;
            while (true)
            {
                byte b = data[index++];
                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
            }
            return value;
        }

        private static void SkipField(byte[] data, ref int index, int wireType)
        {
            switch (wireType) // Varint
            {
                case 0:
                    _ = ReadVarint(data, ref index);
                    break;
                case 1:
                    index += 8;
                    break;
                case 2:
                    {
                        int len = ReadVarint(data, ref index);
                        index += len;
                        break;
                    }

                case 5:
                    index += 4;
                    break;
                default:
                    throw new NotSupportedException($"Unsupported wire type: {wireType}");

            }
        }

        private static byte[] ReadExactly(NetworkStream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException("Socket closed prematurely");
                offset += read;
            }
            return buffer;
        }

        public static bool IsDFHackRunning(string? dfFolderPath)
        {
            if (!DFMonitor.Shared.IsProcessRunning())
                return false;

            int port = ResolveDFHackPort(dfFolderPath);
            try
            {
                using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Blocking = false;
                try
                {
                    socket.Connect("127.0.0.1", port);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
                {
                    var writeList = new ArrayList { socket };
                    var errorList = new ArrayList { socket };
                    Socket.Select(null, writeList, errorList, 100000); // 100ms in microseconds
                    if (writeList.Count > 0 && socket.Connected)
                    {
                        return true;
                    }
                }
                if (socket.Connected)
                    return true;
            }
            catch
            {
                // Ignore connection failures
            }
            return false;
        }

        private static int ResolveDFHackPort(string? dfFolderPath)
        {
            string? envPort = Environment.GetEnvironmentVariable("DFHACK_PORT");
            if (!string.IsNullOrWhiteSpace(envPort) && int.TryParse(envPort, out int p))
            {
                LogRpcClient($"Resolved port from env var DFHACK_PORT: {p}");
                return p;
            }

            // Fallback to reading remote-server.json from a known location relative to dfFolderPath or AppContext.BaseDirectory
            string dfFolder = dfFolderPath ?? AppContext.BaseDirectory;
            string remoteServerJsonPath = Path.Combine(dfFolder, "dfhack-config", "remote-server.json");
            if (File.Exists(remoteServerJsonPath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(remoteServerJsonPath);
                    using JsonDocument doc = JsonDocument.Parse(jsonContent);
                    if (doc.RootElement.TryGetProperty("port", out JsonElement portElem) && portElem.ValueKind == JsonValueKind.Number)
                    {
                        int getInt = portElem.GetInt32();
                        // Comented out to avoid spamming logs
                        //LogRpcClient(" Resolved port from {remoteServerJsonPath}: {getInt}");
                        return getInt;
                    }
                }
                catch (Exception ex)
                {
                    LogRpcClient($"Error reading {remoteServerJsonPath}: {ex.Message}");
                    // Ignore parsing errors and fallback
                }
            }
            LogRpcClient(" Falling back to default port 5000.");
            return 5000;
        }

        private static void LogRpcClient(string message)
        {
            if (DevMode.IsEnabled)
                Console.WriteLine($"DFHackRpcClient: {message}");
        }
    }

}