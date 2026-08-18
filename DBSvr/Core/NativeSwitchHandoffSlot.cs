using System;

namespace DBSvr.Core
{
    /// <summary>
    /// The original UserSoc connection's single runtime-only +0x70 switch slot.
    /// It has no TTL and is consumed by the first selected-human load attempt.
    /// </summary>
    public sealed class NativeSwitchHandoffSlot
    {
        private readonly object _sync = new();
        private string _currentCharacterName = string.Empty;
        private byte[] _extension;

        public string CurrentCharacterName
        {
            get
            {
                lock (_sync) return _currentCharacterName;
            }
        }

        public void SetCurrentCharacter(string characterName)
        {
            lock (_sync)
                _currentCharacterName = characterName ?? string.Empty;
        }

        public bool TryStore(string characterName, byte[] extension)
        {
            if (extension == null
                || extension.Length != NativeDbServerProtocol.LoginExtensionSize)
                return false;
            lock (_sync)
            {
                if (!string.Equals(_currentCharacterName,
                        characterName, StringComparison.Ordinal))
                    return false;
                _extension = (byte[])extension.Clone();
                return true;
            }
        }

        public byte[] Consume()
        {
            lock (_sync)
            {
                var result = _extension;
                _extension = null;
                return result == null ? null : (byte[])result.Clone();
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _extension = null;
                _currentCharacterName = string.Empty;
            }
        }
    }
}
