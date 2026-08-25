using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeUserAdmissionControl
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, uint> _denyIps =
            new(StringComparer.Ordinal);
        private Dictionary<string, uint> _ipCounts =
            new(StringComparer.Ordinal);
        private Func<IReadOnlyList<string>> _snapshotUsers =
            () => Array.Empty<string>();
        private Action<string> _disconnectIp = _ => { };
        private Action _drainQueue = () => { };
        private Action<byte[]> _disconnectAccount = _ => { };
        private Action<byte[], string> _updateOnlineAccountText = (_, _) => { };
        private Action<byte[], ushort> _updateOnlineAccountLoginTime = (_, _) => { };

        public bool QueueEnabled { get; private set; }

        public void Attach(Func<IReadOnlyList<string>> snapshotUsers,
            Action<string> disconnectIp, Action drainQueue,
            Action<byte[]> disconnectAccount = null,
            Action<byte[], string> updateOnlineAccountText = null,
            Action<byte[], ushort> updateOnlineAccountLoginTime = null)
        {
            lock (_sync)
            {
                _snapshotUsers = snapshotUsers ?? (() => Array.Empty<string>());
                _disconnectIp = disconnectIp ?? (_ => { });
                _drainQueue = drainQueue ?? (() => { });
                _disconnectAccount = disconnectAccount ?? (_ => { });
                _updateOnlineAccountText = updateOnlineAccountText
                    ?? ((_, _) => { });
                _updateOnlineAccountLoginTime = updateOnlineAccountLoginTime
                    ?? ((_, _) => { });
            }
        }

        public void SetDenyIp(string ip, uint value)
        {
            ip ??= string.Empty;
            Action<string> disconnect;
            lock (_sync)
            {
                _denyIps[ip] = value;
                disconnect = _disconnectIp;
            }
            DBShare.MainOutMessage($"[Add DenyIP]:{ip} {value}s");
            if (value != 0) disconnect(ip);
        }

        public bool TryGetDenyIp(string ip, out uint value)
        {
            lock (_sync) return _denyIps.TryGetValue(ip ?? string.Empty, out value);
        }

        public void RecountAndSetMaximum(int maximum)
        {
            Func<IReadOnlyList<string>> snapshot;
            lock (_sync) snapshot = _snapshotUsers;
            var owners = snapshot() ?? Array.Empty<string>();
            lock (_sync)
            {
                var remove = new List<string>();
                var keys = new List<string>(_ipCounts.Keys);
                foreach (var key in keys)
                {
                    if (_ipCounts[key] == 0) remove.Add(key);
                    else _ipCounts[key] = 0;
                }
                foreach (var key in remove) _ipCounts.Remove(key);

                foreach (var ip in owners)
                {
                    var key = ip ?? string.Empty;
                    if (_ipCounts.TryGetValue(key, out var count))
                        _ipCounts[key] = unchecked(count + 1);
                }
            }
            DBShare.MaxSingleIpHumanCount = maximum;
        }

        public uint GetIpCount(string ip)
        {
            lock (_sync)
                return _ipCounts.TryGetValue(ip ?? string.Empty, out var count)
                    ? count : 0;
        }

        public bool TryIncrementNativeOwnerIp(string ip, int maximum,
            Func<string, bool> isException)
        {
            ip ??= string.Empty;
            lock (_sync)
            {
                if (!_ipCounts.TryGetValue(ip, out var count))
                {
                    _ipCounts.Add(ip, 1);
                    return true;
                }

                count = unchecked(count + 1);
                _ipCounts[ip] = count;

                if (maximum >= 0 && count <= unchecked((uint)maximum))
                    return true;
                return isException?.Invoke(ip) == true;
            }
        }

        public bool ReleaseNativeConnection(string account, string ip)
        {
            if (string.IsNullOrEmpty(account)) return false;
            ip ??= string.Empty;
            lock (_sync)
            {
                if (!_ipCounts.TryGetValue(ip, out var count) || count == 0)
                    return false;
                count--;
                if (count == 0) _ipCounts.Remove(ip);
                else _ipCounts[ip] = count;
                return true;
            }
        }

        public void ClearNativeIpCounts()
        {
            lock (_sync) _ipCounts.Clear();
        }

        public bool IsDenyTokenMatch(string candidate)
        {
            candidate ??= string.Empty;
            lock (_sync)
                foreach (var token in _denyIps.Keys)
                    if (!string.IsNullOrEmpty(token)
                        && candidate.StartsWith(token, StringComparison.Ordinal))
                        return true;
            return false;
        }

        public void DisconnectAccount(byte[] account)
        {
            Action<byte[]> callback;
            lock (_sync) callback = _disconnectAccount;
            callback(account ?? Array.Empty<byte>());
        }

        public void UpdateOnlineAccountText(byte[] account, string text)
        {
            Action<byte[], string> callback;
            lock (_sync) callback = _updateOnlineAccountText;
            callback(account ?? Array.Empty<byte>(), text ?? string.Empty);
        }

        public void UpdateOnlineAccountLoginTime(byte[] account, ushort flag)
        {
            Action<byte[], ushort> callback;
            lock (_sync) callback = _updateOnlineAccountLoginTime;
            callback(account ?? Array.Empty<byte>(), flag);
        }

        public bool SetQueueEnabled(int value)
        {
            lock (_sync) QueueEnabled = value != 0;
            DBShare.NativeQueueEnabled = value != 0;
            return value != 0;
        }

        public void DrainQueue()
        {
            Action drain;
            lock (_sync) drain = _drainQueue;
            drain();
        }
    }

    public static class NativeType2AdmissionProtocol
    {
        public const ushort DenyIpCommand = 0x0041;
        public const ushort ControlCommand = 0x0187;
        public const ushort ResponseCommand = 0x0132;
        public const int DenyIpBodySize = 20;

        public static bool TryDecodeDenyIp(NativeType2Message message,
            out string ip, out uint value)
        {
            ip = string.Empty;
            value = 0;
            if (message == null || message.Command != DenyIpCommand
                                || message.Suffix == null
                                || message.Suffix.Length != DenyIpBodySize)
                return false;
            var length = message.Suffix[0];
            if (length > 15) return false;
            var bytes = message.Suffix.AsSpan(1, length);
            foreach (var current in bytes)
                if (current != (byte)'.'
                    && current is not (>= (byte)'0' and <= (byte)'9'))
                    return false;
            ip = System.Text.Encoding.ASCII.GetString(bytes);
            value = BinaryPrimitives.ReadUInt32LittleEndian(
                message.Suffix.AsSpan(16, 4));
            return true;
        }

        public static bool IsControlRequest(NativeType2Message message) =>
            message != null && message.Command == ControlCommand
                            && message.Param1 is 0 or 1;

        public static LegacyDbServerFrame CreateControlResponse(
            NativeType2Message request)
        {
            if (!IsControlRequest(request))
                throw new ArgumentException(
                    "native 0x0187 control request is invalid", nameof(request));
            byte[] text;
            if (request.Param1 == 0)
            {
                text = LegacyGbkText.Encode(
                    "单IP最大在线人数已被设置为"
                    + request.Param2.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                text = LegacyGbkText.Encode(request.Param2 != 0
                    ? "排队系统开启" : "排队系统关闭");
            }

            var payload = new byte[NativeType2Protocol.HeaderSize + 0x3C
                                   + text.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, ResponseCommand);
            if (request.Param1 == 0)
            {
                var suffix = request.Suffix ?? Array.Empty<byte>();
                var length = Math.Min(15, suffix.Length);
                payload[0x25] = (byte)length;
                suffix.AsSpan(0, length).CopyTo(payload.AsSpan(0x26));
            }
            text.CopyTo(payload, 0x48);
            return new LegacyDbServerFrame(1, 0, payload);
        }
    }
}
