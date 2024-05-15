using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LocalizationManager;
using ServerSync;

namespace SleepSkip
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class SleepSkipPlugin : BaseUnityPlugin
    {
        internal const string ModName = "SleepSkip";
        internal const string ModVersion = "1.1.1";
        internal const string Author = "Azumatt";
        private const string ModGUID = Author + "." + ModName;
        private static string ConfigFileName = ModGUID + ".cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        internal static string ConnectionError = "";
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource SleepSkipLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

        private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

        internal static int AcceptedSleepCount;
        internal static int AcceptedSleepingCount = 0;
        internal static bool MenusOpened = false;
        internal static DateTime LastSleepCheck = DateTime.MinValue;
        internal static bool InCombat = false;

        private enum Toggle
        {
            On = 1,
            Off = 0
        }

        public void Awake()
        {
            Localizer.Load();
            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);


            ratio = config("1 - General", "Percent of players", 50, new ConfigDescription("Threshold of players that need to be sleeping.\nValues are in percentage 0% - 100%.", new AcceptableValueRange<int>(1, 100)));
            /*SleepDelayInMinutes = config("1 - General", "Sleep Delay", 5, new ConfigDescription("Delay in minutes before allowing the sleep request again.\nValues are in minutes 0 - 60.", new AcceptableValueRange<int>(0, 60)));*/


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
            InCombat = false;
        }

        internal static void OpenMenuOnClient(long senderId)
        {
            if (Player.m_localPlayer == null) return;
            Player? p = Player.m_localPlayer;
            List<Character> characters = new();
            Character.GetCharactersInRange(p.transform.position, 30f, characters);
            if (characters.Where(character => character != null && character.GetComponent<MonsterAI>()).Any(character => character.GetComponent<MonsterAI>().IsAlerted()))
            {
                InCombat = true;
                p.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$sleep_denied_combat"));
            }

            if (!InCombat)
            {
                CreatePopup();
            }
            else
            {
                CreatePopup();
                UnifiedPopup.instance.buttonLeft.onClick.Invoke();
            }
        }

        internal static void CreatePopup()
        {
            string person = AcceptedSleepingCount > 1 ? Localization.instance.Localize("$want") : Localization.instance.Localize("$want_multiple");
            UnifiedPopup.Push(
                new YesNoPopup(
                    Localization.instance.Localize("$sleep_skip"),
                    string.Format(Localization.instance.Localize("$sleep_request"), AcceptedSleepingCount, person),
                    OnAcceptSleep,
                    OnDeclineSleep
                )
            );
        }

        internal static void OnDeclineSleep()
        {
            SleepSkipPlugin.SleepSkipLogger.LogDebug("Declined sleep request");
            // Send RPC to kick everyone from their bed
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStop");
            // Notify everyone that they canceled sleep
            if (SleepSkipPlugin.InCombat)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStopNotify", string.Format(Localization.instance.Localize("$sleep_canceled_reason"), Player.m_localPlayer.GetPlayerName()));
            else
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStopNotify", string.Format(Localization.instance.Localize("$sleep_canceled_by"), Player.m_localPlayer.GetPlayerName()));

            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ResetEveryone");
            UnifiedPopup.Pop();
        }

        internal static void OnAcceptSleep()
        {
            SleepSkipPlugin.SleepSkipLogger.LogDebug("Accepted sleep request");
            // Should update the value of how many accepted here.
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "UpdateSleepCount");
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStopNotify", 1, string.Format(Localization.instance.Localize("$sleep_canceled_by"), Player.m_localPlayer.GetPlayerName()));
            UnifiedPopup.Pop();
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
        internal static ConfigEntry<int> SleepDelayInMinutes = null!;

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
        {
            string synced = synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]";
            ConfigDescription extendedDescription = new(description.Description + synced, description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
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

            public override string ToDescriptionString() => "# Acceptable values: " + string.Join(", ", UnityInput.Current.SupportedKeyCodes);
        }

        #endregion
    }
}