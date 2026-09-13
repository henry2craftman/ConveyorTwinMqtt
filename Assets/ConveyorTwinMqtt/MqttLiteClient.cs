using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MPS
{
    /// <summary>
    /// 교육용 MQTT 3.1.1 최소 클라이언트.
    /// QoS 0 publish/subscribe만 지원하므로 실제 제품 코드에서는 MQTTnet 같은 검증된 라이브러리로 교체하는 것을 권장합니다.
    /// </summary>
    public sealed class MqttLiteClient : IDisposable
    {
        public event Action<string, string> MessageReceived;
        public event Action<Exception> Error;

        public bool IsConnected
        {
            get { return _tcpClient != null && _tcpClient.Connected && _stream != null; }
        }

        TcpClient _tcpClient;
        NetworkStream _stream;
        CancellationTokenSource _cts;
        Task _receiveTask;
        Task _keepAliveTask;
        readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        ushort _packetId;
        int _keepAliveSeconds = 30;

        public async Task ConnectAsync(string host, int port, string clientId, int keepAliveSeconds = 30)
        {
            Disconnect();

            _keepAliveSeconds = Math.Max(keepAliveSeconds, 5);
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, port);
            _stream = _tcpClient.GetStream();

            byte[] variableHeader = BuildMqttString("MQTT")
                .Concat(new byte[] { 0x04, 0x02, (byte)(_keepAliveSeconds >> 8), (byte)_keepAliveSeconds });
            byte[] payload = BuildMqttString(clientId);
            byte[] connectPayload = variableHeader.Concat(payload);

            await WritePacketAsync(0x10, connectPayload);

            MqttPacket connAck = await ReadPacketAsync();
            if (connAck.PacketType != 2 || connAck.Payload.Length < 2 || connAck.Payload[1] != 0)
                throw new IOException("MQTT CONNACK failed");

            _cts = new CancellationTokenSource();
            _receiveTask = ReceiveLoopAsync(_cts.Token);
            _keepAliveTask = KeepAliveLoopAsync(_cts.Token);
        }

        public async Task PublishAsync(string topic, string payload, bool retain = false)
        {
            if (!IsConnected)
                return;

            byte[] topicBytes = BuildMqttString(topic);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload ?? string.Empty);
            byte[] packetPayload = topicBytes.Concat(payloadBytes);
            await WritePacketAsync((byte)(retain ? 0x31 : 0x30), packetPayload);
        }

        public async Task SubscribeAsync(string topic)
        {
            if (!IsConnected)
                return;

            ushort packetId = NextPacketId();
            byte[] payload = new byte[] { (byte)(packetId >> 8), (byte)packetId }
                .Concat(BuildMqttString(topic))
                .Concat(new byte[] { 0x00 });

            await WritePacketAsync(0x82, payload);
        }

        public void Disconnect()
        {
            try
            {
                if (_stream != null && _tcpClient != null && _tcpClient.Connected)
                    WritePacketAsync(0xE0, Array.Empty<byte>()).Wait(100);
            }
            catch
            {
                // Disconnect is best effort.
            }

            try { _cts?.Cancel(); } catch { }
            try { _stream?.Close(); } catch { }
            try { _tcpClient?.Close(); } catch { }

            _cts = null;
            _stream = null;
            _tcpClient = null;
            _receiveTask = null;
            _keepAliveTask = null;
        }

        public void Dispose()
        {
            Disconnect();
            _writeLock.Dispose();
        }

        async Task ReceiveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && IsConnected)
            {
                try
                {
                    MqttPacket packet = await ReadPacketAsync();
                    if (packet.PacketType == 3)
                        HandlePublish(packet);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Error?.Invoke(ex);
                    break;
                }
            }
        }

        async Task KeepAliveLoopAsync(CancellationToken token)
        {
            int delayMs = Math.Max(1000, _keepAliveSeconds * 500);

            while (!token.IsCancellationRequested && IsConnected)
            {
                try
                {
                    await Task.Delay(delayMs, token);
                    await WritePacketAsync(0xC0, Array.Empty<byte>());
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Error?.Invoke(ex);
                    break;
                }
            }
        }

        void HandlePublish(MqttPacket packet)
        {
            byte[] data = packet.Payload;
            if (data.Length < 2)
                return;

            int topicLength = (data[0] << 8) | data[1];
            if (topicLength <= 0 || data.Length < topicLength + 2)
                return;

            string topic = Encoding.UTF8.GetString(data, 2, topicLength);
            int payloadOffset = 2 + topicLength;
            string payload = Encoding.UTF8.GetString(data, payloadOffset, data.Length - payloadOffset);
            MessageReceived?.Invoke(topic, payload);
        }

        async Task WritePacketAsync(byte fixedHeader, byte[] payload)
        {
            if (_stream == null)
                return;

            byte[] remainingLength = EncodeRemainingLength(payload.Length);
            byte[] packet = new byte[1 + remainingLength.Length + payload.Length];
            packet[0] = fixedHeader;
            Buffer.BlockCopy(remainingLength, 0, packet, 1, remainingLength.Length);
            Buffer.BlockCopy(payload, 0, packet, 1 + remainingLength.Length, payload.Length);

            await _writeLock.WaitAsync();
            try
            {
                await _stream.WriteAsync(packet, 0, packet.Length);
                await _stream.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        async Task<MqttPacket> ReadPacketAsync()
        {
            byte[] first = await ReadExactAsync(1);
            int multiplier = 1;
            int remainingLength = 0;
            byte encoded;

            do
            {
                byte[] digit = await ReadExactAsync(1);
                encoded = digit[0];
                remainingLength += (encoded & 127) * multiplier;
                multiplier *= 128;

                if (multiplier > 128 * 128 * 128)
                    throw new IOException("Malformed MQTT remaining length");
            }
            while ((encoded & 128) != 0);

            byte[] payload = remainingLength > 0
                ? await ReadExactAsync(remainingLength)
                : Array.Empty<byte>();

            return new MqttPacket(first[0], payload);
        }

        async Task<byte[]> ReadExactAsync(int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;

            while (offset < length)
            {
                int read = await _stream.ReadAsync(buffer, offset, length - offset);
                if (read <= 0)
                    throw new IOException("MQTT connection closed");

                offset += read;
            }

            return buffer;
        }

        ushort NextPacketId()
        {
            _packetId++;
            if (_packetId == 0)
                _packetId = 1;

            return _packetId;
        }

        static byte[] BuildMqttString(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            byte[] result = new byte[2 + bytes.Length];
            result[0] = (byte)(bytes.Length >> 8);
            result[1] = (byte)bytes.Length;
            Buffer.BlockCopy(bytes, 0, result, 2, bytes.Length);
            return result;
        }

        static byte[] EncodeRemainingLength(int length)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                do
                {
                    int digit = length % 128;
                    length /= 128;

                    if (length > 0)
                        digit |= 128;

                    stream.WriteByte((byte)digit);
                }
                while (length > 0);

                return stream.ToArray();
            }
        }

        struct MqttPacket
        {
            public readonly int PacketType;
            public readonly byte[] Payload;

            public MqttPacket(byte fixedHeader, byte[] payload)
            {
                PacketType = fixedHeader >> 4;
                Payload = payload;
            }
        }
    }

    static class MqttByteArrayExtensions
    {
        public static byte[] Concat(this byte[] first, byte[] second)
        {
            byte[] result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }
    }
}
