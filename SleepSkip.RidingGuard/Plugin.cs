using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace SleepSkip.RidingGuard;

[BepInPlugin(ModGuid, ModName, ModVersion)]
[BepInDependency("Azumatt.SleepSkip", BepInDependency.DependencyFlags.HardDependency)]
public class RidingGuardPlugin : BaseUnityPlugin
{
    internal const string ModGuid = "Azumatt.SleepSkip.RidingGuard";
    internal const string ModName = "SleepSkip Riding Guard";
    internal const string ModVersion = "1.0.0";

    private static readonly Harmony HarmonyInstance = new(ModGuid);
    internal static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource(ModName);

    private void Awake()
    {
        HarmonyInstance.PatchAll();
        GuardLog.Info($"{ModName} loaded and patches applied.");
    }

    private void OnDestroy()
    {
        HarmonyInstance.UnpatchSelf();
    }
}

[HarmonyPatch]
internal static class SleepSkipOpenMenuPatch
{
    private const string TargetTypeName = "SleepSkip.SleepSkipPlugin";
    private const string TargetMethodName = "OpenMenuOnClient";
    private const string VoteYesRpcName = "VoteYes";
    private const string VoteNoRpcName = "VoteNo";

    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method($"{TargetTypeName}:{TargetMethodName}");
    }

    private static bool Prefix()
    {
        Player? player = Player.m_localPlayer;
        if (player == null)
            return true;

        if (!SleepSkipRidingState.IsPlayerRidingSaddle(player))
            return true;

        SleepSkipRidingState.PopupAutoChoiceSetting choice = SleepSkipRidingState.GetPopupAutoChoice();
        switch (choice)
        {
            case SleepSkipRidingState.PopupAutoChoiceSetting.AlwaysAccept:
                SleepSkipRidingState.SendVote(VoteYesRpcName);
                return false;
            case SleepSkipRidingState.PopupAutoChoiceSetting.AlwaysDecline:
                SleepSkipRidingState.SendVote(VoteNoRpcName);
                return false;
            default:
                return true;
        }
    }
}

[HarmonyPatch(typeof(Game), nameof(Game.SleepStop))]
internal static class GameSleepStopLogPatch
{
    private static void Prefix()
    {
        Player? player = Player.m_localPlayer;
        bool riding = player != null && SleepSkipRidingState.IsPlayerRidingSaddle(player);
        if (riding)
            SleepSkipRidingState.ArmDetachBlock();
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.AttachStop))]
internal static class CharacterAttachStopPatch
{
    private static bool Prefix(Character __instance)
    {
        if (__instance != Player.m_localPlayer)
            return true;

        if (!SleepSkipRidingState.TryConsumeDetachBlock(nameof(Character.AttachStop)))
            return true;

        return false;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.AttachStop))]
internal static class PlayerAttachStopPatch
{
    private static bool Prefix(Player __instance)
    {
        if (__instance != Player.m_localPlayer)
            return true;

        if (!SleepSkipRidingState.TryConsumeDetachBlock(nameof(Player.AttachStop)))
            return true;

        return false;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.StopDoodadControl))]
internal static class PlayerStopDoodadControlPatch
{
    private static bool Prefix(Player __instance)
    {
        if (__instance != Player.m_localPlayer)
            return true;

        if (!SleepSkipRidingState.TryConsumeDetachBlock(nameof(Player.StopDoodadControl)))
            return true;

        return false;
    }
}

internal static class SleepSkipRidingState
{
    private const string SleepSkipTypeName = "SleepSkip.SleepSkipPlugin";
    private const string PopupAutoChoiceFieldName = "PopupAutoChoice";
    private const int MaxDetachBlocksPerSleepStop = 4;
    private const double BlockTimeoutSeconds = 2.0;

    private static int s_remainingDetachBlocks;
    private static DateTime s_detachBlockArmedAtUtc = DateTime.MinValue;
    private static double s_detachBlockTimeoutSeconds = BlockTimeoutSeconds;

    private static readonly Type? SleepSkipType = AccessTools.TypeByName(SleepSkipTypeName);
    private static readonly FieldInfo? PopupAutoChoiceField = SleepSkipType != null
        ? AccessTools.Field(SleepSkipType, PopupAutoChoiceFieldName)
        : null;

    internal enum PopupAutoChoiceSetting
    {
        Ask,
        AlwaysAccept,
        AlwaysDecline
    }

    internal static bool IsPlayerRidingSaddle(Player player)
    {
        if (!player.IsAttached())
            return false;

        try
        {
            IDoodadController controller = player.GetDoodadController();
            return controller is Sadle;
        }
        catch (Exception e)
        {
            GuardLog.Debug($"Riding detection failed: {e.Message}");
            return false;
        }
    }

    internal static void ArmDetachBlock()
    {
        s_remainingDetachBlocks = MaxDetachBlocksPerSleepStop;
        s_detachBlockArmedAtUtc = DateTime.UtcNow;
        s_detachBlockTimeoutSeconds = BlockTimeoutSeconds;
    }

    internal static bool TryConsumeDetachBlock(string source)
    {
        if (s_remainingDetachBlocks <= 0)
            return false;

        if ((DateTime.UtcNow - s_detachBlockArmedAtUtc).TotalSeconds > s_detachBlockTimeoutSeconds)
        {
            s_remainingDetachBlocks = 0;
            return false;
        }

        s_remainingDetachBlocks--;
        return true;
    }

    internal static PopupAutoChoiceSetting GetPopupAutoChoice()
    {
        if (PopupAutoChoiceField == null)
            return PopupAutoChoiceSetting.Ask;

        try
        {
            object? configEntry = PopupAutoChoiceField.GetValue(null);
            if (configEntry == null)
                return PopupAutoChoiceSetting.Ask;

            PropertyInfo? valueProperty = configEntry.GetType().GetProperty("Value");
            object? value = valueProperty?.GetValue(configEntry);
            string setting = value?.ToString() ?? string.Empty;

            return setting switch
            {
                "AlwaysAccept" => PopupAutoChoiceSetting.AlwaysAccept,
                "AlwaysDecline" => PopupAutoChoiceSetting.AlwaysDecline,
                _ => PopupAutoChoiceSetting.Ask
            };
        }
        catch (Exception e)
        {
            GuardLog.Debug($"Failed to read SleepSkip PopupAutoChoice: {e.Message}");
            return PopupAutoChoiceSetting.Ask;
        }
    }

    internal static void SendVote(string rpcName)
    {
        if (ZRoutedRpc.instance == null)
        {
            GuardLog.Debug($"Skipped sending {rpcName} because ZRoutedRpc.instance is null.");
            return;
        }

        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, rpcName, ZNet.GetUID());
    }
}

internal static class GuardLog
{
    private static readonly string FilePath = Path.Combine(Paths.BepInExRootPath, "SleepSkip.RidingGuard.log");

    internal static void Info(string message)
    {
        RidingGuardPlugin.Log.LogInfo(message);
        WriteToFile("INFO", message);
    }

    internal static void Debug(string message)
    {
        RidingGuardPlugin.Log.LogDebug(message);
        WriteToFile("DEBUG", message);
    }

    private static void WriteToFile(string level, string message)
    {
        try
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
            File.AppendAllText(FilePath, line);
        }
        catch
        {
            // Ignore file logging failures; BepInEx logging still applies.
        }
    }
}
