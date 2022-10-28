using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace SleepSkip
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class SleepSkipPlugin : BaseUnityPlugin
    {
        internal const string ModName = "SleepSkip";
        internal const string ModVersion = "1.0.2";
        internal const string Author = "Azumatt";
        private const string ModGUID = Author + "." + ModName;
        private static string ConfigFileName = ModGUID + ".cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;

        internal static string ConnectionError = "";

        private readonly Harmony _harmony = new(ModGUID);

        public static readonly ManualLogSource SleepSkipLogger =
            BepInEx.Logging.Logger.CreateLogSource(ModName);

        private static readonly ConfigSync ConfigSync = new(ModGUID)
            { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

        public static GameObject? Dialog;
        internal static int AcceptedSleepCount;
        internal static int AcceptedSleepingCount = 0;
        internal static bool MenusOpened = false;

        private enum Toggle
        {
            On = 1,
            Off = 0
        }

        public void Awake()
        {
            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On,
                "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);


            ratio = config("1 - General", "Percent of players", 50,
                new ConfigDescription(
                    "Threshold of players that need to be sleeping.\nValues are in percentage 0% - 100%.",
                    new AcceptableValueRange<int>(1, 100)));


            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();
        }

        private void OnDestroy()
        {
            Config.Save();
        }

        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                SleepSkipLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                SleepSkipLogger.LogError($"There was an issue loading your {ConfigFileName}");
                SleepSkipLogger.LogError("Please check your config entries for spelling and format!");
            }
        }

        internal static void ResetVariables(long senderId)
        {
            AcceptedSleepCount = 0;
            AcceptedSleepingCount = 0;
            MenusOpened = false;
            Dialog!.SetActive(false);
        }

        internal static void OpenMenuOnClient(long senderId)
        {
            Dialog!.SetActive(true);
        }

        internal static void UpdateSleepCount(long senderId)
        {
            AcceptedSleepCount += 1;
        }

        internal static void UpdateMenuNumberOnClient(long senderId, int numberCount)
        {
            AcceptedSleepingCount = numberCount;
        }

        internal static void SleepStopNotify(long senderId, string s)
        {
            if (!ZNet.instance.IsServer())
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, s);
        }


        #region ConfigOptions

        private static ConfigEntry<Toggle> _serverConfigLocked = null!;
        internal static ConfigEntry<int> ratio = null!;

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
            bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription =
                new(
                    description.Description +
                    (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
                    description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description,
            bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            public bool? Browsable = false;
        }

        class AcceptableShortcuts : AcceptableValueBase
        {
            public AcceptableShortcuts() : base(typeof(KeyboardShortcut))
            {
            }

            public override object Clamp(object value) => value;
            public override bool IsValid(object value) => true;

            public override string ToDescriptionString() =>
                "# Acceptable values: " + string.Join(", ", KeyboardShortcut.AllKeyCodes);
        }

        #endregion
    }
}