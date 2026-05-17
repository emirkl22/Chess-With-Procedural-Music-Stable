using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Chess
{
    // Minimal OSC sender over UDP — no external packages required.
    // Supports float (/f) and string (/s) argument types.
    public static class OSCSender
    {
        static UdpClient _udp;
        static IPEndPoint _endpoint;

        public static void Init(string host, int port)
        {
            _udp      = new UdpClient();
            _endpoint = new IPEndPoint(IPAddress.Parse(host), port);
        }

        public static void Send(string address, float value)
        {
            if (_udp == null) return;
            byte[] msg = BuildMessage(address, value);
            _udp.Send(msg, msg.Length, _endpoint);
        }

        public static void Send(string address, string value)
        {
            if (_udp == null) return;
            byte[] msg = BuildMessage(address, value);
            _udp.Send(msg, msg.Length, _endpoint);
        }

        public static void Close() => _udp?.Close();

        // ---- OSC packet construction ----

        static byte[] BuildMessage(string address, float value)
        {
            var packet = new System.Collections.Generic.List<byte>();
            AppendString(packet, address);
            AppendString(packet, ",f");
            AppendFloat(packet, value);
            return packet.ToArray();
        }

        static byte[] BuildMessage(string address, string value)
        {
            var packet = new System.Collections.Generic.List<byte>();
            AppendString(packet, address);
            AppendString(packet, ",s");
            AppendString(packet, value);
            return packet.ToArray();
        }

        // OSC strings are null-terminated and padded to 4-byte boundary
        static void AppendString(System.Collections.Generic.List<byte> buf, string s)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(s);
            buf.AddRange(bytes);
            buf.Add(0); // null terminator
            int pad = (4 - ((bytes.Length + 1) % 4)) % 4;
            for (int i = 0; i < pad; i++) buf.Add(0);
        }

        // OSC floats are big-endian IEEE 754
        static void AppendFloat(System.Collections.Generic.List<byte> buf, float value)
        {
            byte[] bytes = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) System.Array.Reverse(bytes);
            buf.AddRange(bytes);
        }
    }
}
