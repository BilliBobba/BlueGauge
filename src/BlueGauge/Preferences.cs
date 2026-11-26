using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;

namespace BlueGauge
{
    [DataContract]
    internal sealed class Preferences
    {
        private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueGauge");
        private static readonly string FilePath = Path.Combine(Folder, "settings.json");
        private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValue = "BlueGauge";

        [DataMember] public int LowThreshold { get; set; } = 20;
        [DataMember] public int RefreshSeconds { get; set; } = 60;
        [DataMember] public bool Alerts { get; set; } = true;

        [OnDeserializing] private void SetDefaults(StreamingContext _) { LowThreshold = 20; RefreshSeconds = 60; Alerts = true; }

        internal static Preferences Load()
        {
            try
            {
                using (var file = File.OpenRead(FilePath))
                {
                    var settings = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(file);
                    if (settings != null)
                    {
                        if (settings.LowThreshold < 5 || settings.LowThreshold > 50) settings.LowThreshold = 20;
                        if (settings.RefreshSeconds != 30 && settings.RefreshSeconds != 60 && settings.RefreshSeconds != 120) settings.RefreshSeconds = 60;
                        return settings;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SerializationException) { }
            return new Preferences();
        }

        internal void Save()
        {
            Directory.CreateDirectory(Folder);
            string staging = FilePath + ".tmp";
            using (var file = File.Create(staging)) new DataContractJsonSerializer(typeof(Preferences)).WriteObject(file, this);
            if (File.Exists(FilePath)) File.Replace(staging, FilePath, null);
            else File.Move(staging, FilePath);
        }

        private static string StartupCommand => "\"" + Application.ExecutablePath + "\" --background";

        internal static bool IsStartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(StartupKey))
                return string.Equals(key?.GetValue(StartupValue) as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
        }

        internal static void SetStartup(bool enabled)
        {
            if (enabled)
            {
                using (var key = Registry.CurrentUser.CreateSubKey(StartupKey)) key.SetValue(StartupValue, StartupCommand);
            }
            else
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StartupKey, writable: true)) key?.DeleteValue(StartupValue, false);
            }
        }
    }
}
