using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LocalizationManager;
using ServerSync;

namespace SleepSkip;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class SleepSkipPlugin : BaseUnityPlugin
{
    internal const string ModName = "SleepSkip";
    internal const string ModVersion = "1.3.0";
    internal const string Author = "Azumatt";
    private const string ModGUID = Author + "." + ModName;
    private static string ConfigFileName = ModGUID + ".cfg";
    private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
    internal static string ConnectionError = "";
    private readonly Harmony _harmony = new(ModGUID);
    public static readonly ManualLogSource SleepSkipLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

    private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion, ModRequired = true };

    // Server-side vote tracking
    internal static HashSet<long> YesVoters = [];
    internal static HashSet<long> NoVoters = [];
    internal static HashSet<long> PopupSentTo = [];
    internal static DateTime VoteStartTime = DateTime.MinValue;
    internal static DateTime LastSleepCompleted = DateTime.MinValue;

    // Shared state
    internal static int WarningTimeCounter = 0;
    internal static bool MenusOpened = false;
    internal static DateTime LastSleepCheck = DateTime.MinValue;

    // Client-side display variables (updated by server via RPC)
    internal static int DisplayInBed = 0;
    internal static int DisplayYes = 0;
    internal static int DisplayNo = 0;
    internal static int DisplayWaiting = 0;
    internal static int DisplayTotal = 0;

    private enum Toggle
    {
        On = 1,
        Off = 0
    }

    internal enum AutoChoice
    {
        Ask,
        AlwaysAccept,
        AlwaysDecline,
    }

    public void Awake()
    {
        Localizer.Load();
        _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
        _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);

        Ratio = config("1 - General", "Percent of players", 50, new ConfigDescription("Threshold of players that need to agree to sleep.\nValues are in percentage 1% - 100%.", new AcceptableValueRange<int>(1, 100)));
        PlayersNeeded = config("1 - General", "Players Needed", 2, new ConfigDescription("Amount of players needed in bed to start the sleep vote.", new AcceptableValueRange<int>(1, 100)));
        WarningTime = config("1 - General", "Warning Time", 15, new ConfigDescription("Time in seconds before the sleep popup is displayed on the client.\nAccepted values are from 1 - 60.", new AcceptableValueRange<int>(1, 60)));
        VoteTimeout = config("1 - General", "Vote Timeout", 45, new ConfigDescription("Time in seconds before non-responding players are counted as abstaining.\nSet to 0 to disable (votes wait indefinitely).", new AcceptableValueRange<int>(0, 120)));
        SleepCooldown = config("1 - General", "Sleep Cooldown", 0, new ConfigDescription("Cooldown in seconds between sleep vote attempts.\nSet to 0 to disable.", new AcceptableValueRange<int>(0, 600)));

        PopupAutoChoice = config("2 - Client Choices", "Popup Auto Choice", AutoChoice.Ask, new ConfigDescription("Auto choice for the popup on the client. Change only if you don't want to be asked and want to either always accept or always deny."), false);


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

    #region RPC Handlers

    internal static void ResetVariables(long senderId)
    {
        YesVoters.Clear();
        NoVoters.Clear();
        PopupSentTo.Clear();
        VoteStartTime = DateTime.MinValue;
        MenusOpened = false;
        LastSleepCheck = DateTime.MinValue;
        WarningTimeCounter = 0;
        DisplayInBed = 0;
        DisplayYes = 0;
        DisplayNo = 0;
        DisplayWaiting = 0;
        DisplayTotal = 0;
    }

    internal static void OpenMenuOnClient(long senderId)
    {
        if (Player.m_localPlayer == null) return;
        Player p = Player.m_localPlayer;

        // Check combat locally (not a sticky static)
        bool inCombat = false;
        List<Character> characters = [];
        Character.GetCharactersInRange(p.transform.position, 30f, characters);
        foreach (Character character in characters)
        {
            if (character == null) continue;
            MonsterAI ai = character.GetComponent<MonsterAI>();
            if (ai != null && ai.IsAlerted())
            {
                inCombat = true;
                break;
            }
        }

        if (inCombat)
        {
            p.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$sleep_denied_combat"));
            // Auto-decline due to combat (just sends VoteNo, does NOT cancel for everyone)
            CreatePopup();
            UnifiedPopup.instance.buttonLeft.onClick.Invoke();
            return;
        }

        switch (PopupAutoChoice.Value)
        {
            case AutoChoice.AlwaysAccept:
                CreatePopup();
                UnifiedPopup.instance.buttonRight.onClick.Invoke();
                break;
            case AutoChoice.AlwaysDecline:
                CreatePopup();
                UnifiedPopup.instance.buttonLeft.onClick.Invoke();
                break;
            default:
                CreatePopup();
                break;
        }
    }

    internal static void CreatePopup()
    {
        UnifiedPopup.Push(
            new YesNoPopup(
                Localization.instance.Localize("$sleep_skip"),
                FormatVoteBody(),
                OnAcceptSleep,
                OnDeclineSleep
            )
        );
    }

    internal static string FormatVoteBody()
    {
        int wantSleep = DisplayInBed + DisplayYes;
        string request = string.Format(Localization.instance.Localize("$sleep_request"),
            wantSleep, DisplayTotal, Ratio.Value);
        string status = string.Format(Localization.instance.Localize("$sleep_vote_status"),
            DisplayInBed, DisplayYes, DisplayNo, DisplayWaiting);
        string prompt = Localization.instance.Localize("$sleep_vote_prompt");
        return $"{request}\n\n{status}\n\n{prompt}";
    }

    internal static void OnDeclineSleep()
    {
        SleepSkipLogger.LogDebug("Declined sleep request");
        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, nameof(VoteNo), ZNet.GetUID());
        UnifiedPopup.Pop();
    }

    internal static void OnAcceptSleep()
    {
        SleepSkipLogger.LogDebug("Accepted sleep request");
        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, nameof(VoteYes), ZNet.GetUID());
        UnifiedPopup.Pop();
    }

    internal static void VoteYes(long senderId, long voterId)
    {
        if (!ZNet.instance.IsServer()) return;
        YesVoters.Add(voterId);
        NoVoters.Remove(voterId);
        SleepSkipLogger.LogDebug($"VoteYes from {voterId}. Yes: {YesVoters.Count}, No: {NoVoters.Count}");
    }

    internal static void VoteNo(long senderId, long voterId)
    {
        if (!ZNet.instance.IsServer()) return;
        NoVoters.Add(voterId);
        YesVoters.Remove(voterId);
        SleepSkipLogger.LogDebug($"VoteNo from {voterId}. Yes: {YesVoters.Count}, No: {NoVoters.Count}");
    }

    internal static void UpdateVoteDisplay(long senderId, string data)
    {
        string[] parts = data.Split(',');
        if (parts.Length != 5) return;
        int.TryParse(parts[0], out DisplayInBed);
        int.TryParse(parts[1], out DisplayYes);
        int.TryParse(parts[2], out DisplayNo);
        int.TryParse(parts[3], out DisplayWaiting);
        int.TryParse(parts[4], out DisplayTotal);
    }

    internal static void UpdateWarningTime(long senderId, int time)
    {
        WarningTimeCounter = time;
        if (Player.m_localPlayer != null)
            Player.m_localPlayer.Message(WarningTimeCounter == WarningTime.Value ? MessageHud.MessageType.Center : MessageHud.MessageType.TopLeft, string.Format(Localization.instance.Localize("$sleep_warning_time"), WarningTimeCounter));
    }

    internal static void SleepVoteResult(long senderId, string message)
    {
        // Dismiss sleep popup if it's open
        if (UnifiedPopup.instance != null && UnifiedPopup.IsVisible() && UnifiedPopup.instance.headerText != null && UnifiedPopup.instance.headerText.text == Localization.instance.Localize("$sleep_skip"))
        {
            UnifiedPopup.Pop();
        }

        if (Player.m_localPlayer != null)
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, message);
    }

    internal static void SleepStopNotify(long senderId, string s)
    {
        if (Player.m_localPlayer != null)
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, s);
    }

    #endregion

    #region ConfigOptions

    private static ConfigEntry<Toggle> _serverConfigLocked = null!;
    internal static ConfigEntry<int> Ratio = null!;
    internal static ConfigEntry<int> PlayersNeeded = null!;
    internal static ConfigEntry<int> WarningTime = null!;
    internal static ConfigEntry<int> VoteTimeout = null!;
    internal static ConfigEntry<int> SleepCooldown = null!;
    internal static ConfigEntry<AutoChoice> PopupAutoChoice = null!;

    private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        string synced = synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]";
        ConfigDescription extendedDescription = new(description.Description + synced, description.AcceptableValues, description.Tags);
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);

        SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
    {
        return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
    }

    class AcceptableShortcuts() : AcceptableValueBase(typeof(KeyboardShortcut))
    {
        public override object Clamp(object value) => value;
        public override bool IsValid(object value) => true;

        public override string ToDescriptionString() => "# Acceptable values: " + string.Join(", ", UnityInput.Current.SupportedKeyCodes);
    }

    #endregion
}