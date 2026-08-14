using System;
using System.IO;
using SystemModule;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// Simple ini loader for Encryptor.dll configuration.
    /// Reads encryption DLL settings and returns default values on parse failure.
    /// </summary>
    public class EncryptorDllValidator : IniFile
    {
        private const string DefaultDllName = "Encryptor.dll";
        private const bool DefaultEnabled = true;

        public string DllName { get; private set; } = DefaultDllName;
        public bool Enabled { get; private set; } = DefaultEnabled;

        public EncryptorDllValidator(string fileName) : base(fileName)
        {
            try
            {
                Load();
            }
            catch (Exception ex)
            {
                LogError($"Failed to load Encryptor config: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads Encryptor.dll configuration from ini file.
        /// Returns default values on parse failure.
        /// </summary>
        public void LoadConfig()
        {
            try
            {
                DllName = ReadString("Encryptor", "DllName", DefaultDllName);
                Enabled = ReadBool("Encryptor", "Enabled", DefaultEnabled);

                if (string.IsNullOrWhiteSpace(DllName))
                {
                    LogError("DllName is empty, using default: " + DefaultDllName);
                    DllName = DefaultDllName;
                }

                if (!File.Exists(DllName))
                {
                    LogError($"Encryptor DLL not found: {DllName}, using default: {DefaultDllName}");
                    DllName = DefaultDllName;
                }
            }
            catch (Exception ex)
            {
                LogError($"Error parsing Encryptor config: {ex.Message}");
                DllName = DefaultDllName;
                Enabled = DefaultEnabled;
            }
        }

        private void LogError(string message)
        {
            if (M2Share.LogSystem != null)
            {
                M2Share.LogSystem.Error($"[EncryptorDllValidator] {message}", MessageType.Error, MessageLevel.None);
            }
        }
    }
}
