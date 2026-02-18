using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SleepSkip;

[HarmonyPatch(typeof(Game), nameof(Game.EverybodyIsTryingToSleep))]
internal static class GameEverybodyIsTryingToSleepPatch
{
    // Reusable collections to avoid per-frame allocations
    private static readonly HashSet<long> s_inBedPlayerIds = [];
    private static readonly HashSet<long> s_currentPlayerIds = [];

    // Previous vote display values to avoid spamming RPCs every frame
    private static int s_lastDisplayInBed;
    private static int s_lastDisplayYes;
    private static int s_lastDisplayNo;
    private static int s_lastDisplayWaiting;
    private static int s_lastDisplayTotal;

    static bool Prefix(Game __instance, ref bool __result)
    {
        List<ZDO> allCharacterZdos = ZNet.instance.GetAllCharacterZDOS();
        if (allCharacterZdos.Count == 0)
        {
            __result = false;
            return false;
        }

        int totalPlayers = allCharacterZdos.Count;
        DateTime now = DateTime.Now;

        // Collect in-bed player IDs (reuse static set)
        s_inBedPlayerIds.Clear();
        foreach (ZDO zdo in allCharacterZdos)
        {
            if (zdo.GetBool(ZDOVars.s_inBed))
                s_inBedPlayerIds.Add(zdo.m_uid.UserID);
        }

        int inBedCount = s_inBedPlayerIds.Count;

        // No one in bed - reset and bail
        if (inBedCount <= 0)
        {
            ResetIfActive();
            __result = false;
            return false;
        }

        // All players in bed or solo - skip voting entirely
        if (inBedCount >= totalPlayers)
        {
            ResetIfActive();
            __result = true;
            return false;
        }

        // Not enough players in bed to start the vote (solo servers bypass this)
        if (totalPlayers > 1 && inBedCount < SleepSkipPlugin.PlayersNeeded.Value)
        {
            ResetIfActive();
            __result = false;
            return false;
        }

        // Check cooldown from last sleep attempt
        if (SleepSkipPlugin.SleepCooldown.Value > 0 &&
            SleepSkipPlugin.LastSleepCompleted != DateTime.MinValue &&
            now < SleepSkipPlugin.LastSleepCompleted.AddSeconds(SleepSkipPlugin.SleepCooldown.Value))
        {
            __result = false;
            return false;
        }

        // --- Warning Period ---
        if (SleepSkipPlugin.LastSleepCheck == DateTime.MinValue)
        {
            SleepSkipPlugin.LastSleepCheck = now;
            SleepSkipPlugin.WarningTimeCounter = SleepSkipPlugin.WarningTime.Value;
            SendWarningToAll(SleepSkipPlugin.WarningTimeCounter);
            __result = false;
            return false;
        }

        DateTime warningEnd = SleepSkipPlugin.LastSleepCheck.AddSeconds(SleepSkipPlugin.WarningTime.Value);
        if (warningEnd > now)
        {
            int remainingTime = (int)(warningEnd - now).TotalSeconds;
            if (remainingTime != SleepSkipPlugin.WarningTimeCounter)
            {
                SleepSkipPlugin.WarningTimeCounter = remainingTime;
                SendWarningToAll(SleepSkipPlugin.WarningTimeCounter);
            }

            __result = false;
            return false;
        }

        // --- Voting Phase ---

        // Clean up votes from disconnected players (reuse static set)
        s_currentPlayerIds.Clear();
        foreach (ZNetPeer peer in ZNet.instance.m_peers)
            s_currentPlayerIds.Add(peer.m_characterID.UserID);
        if (!ZNet.instance.IsDedicated())
            s_currentPlayerIds.Add(ZNet.GetUID());

        SleepSkipPlugin.YesVoters.IntersectWith(s_currentPlayerIds);
        SleepSkipPlugin.NoVoters.IntersectWith(s_currentPlayerIds);

        // In-bed players are implicit yes - remove them from explicit vote sets
        SleepSkipPlugin.YesVoters.ExceptWith(s_inBedPlayerIds);
        SleepSkipPlugin.NoVoters.ExceptWith(s_inBedPlayerIds);

        int yesCount = SleepSkipPlugin.YesVoters.Count;
        int noCount = SleepSkipPlugin.NoVoters.Count;
        int unvotedCount = totalPlayers - inBedCount - yesCount - noCount;
        if (unvotedCount < 0) unvotedCount = 0;

        // Vote timeout check
        bool voteTimeoutEnabled = SleepSkipPlugin.VoteTimeout.Value > 0;
        bool voteTimedOut = voteTimeoutEnabled &&
                            SleepSkipPlugin.VoteStartTime != DateTime.MinValue &&
                            now > SleepSkipPlugin.VoteStartTime.AddSeconds(SleepSkipPlugin.VoteTimeout.Value);

        // After timeout, unvoted players are treated as abstaining (removed from denominator)
        int abstainCount = voteTimedOut ? unvotedCount : 0;
        int effectiveTotal = totalPlayers - abstainCount;
        int currentYes = inBedCount + yesCount;
        int waitingCount = voteTimedOut ? 0 : unvotedCount;

        // Only send vote display when values actually change (avoid RPC spam every frame)
        if (inBedCount != s_lastDisplayInBed || yesCount != s_lastDisplayYes ||
            noCount != s_lastDisplayNo || waitingCount != s_lastDisplayWaiting ||
            totalPlayers != s_lastDisplayTotal)
        {
            s_lastDisplayInBed = inBedCount;
            s_lastDisplayYes = yesCount;
            s_lastDisplayNo = noCount;
            s_lastDisplayWaiting = waitingCount;
            s_lastDisplayTotal = totalPlayers;
            SendVoteDisplayToAll(inBedCount, yesCount, noCount, waitingCount, totalPlayers);
        }

        // Send popup to non-bed players who haven't received it yet
        if (!SleepSkipPlugin.MenusOpened)
        {
            SleepSkipPlugin.VoteStartTime = now;
            SendPopupToEligiblePlayers(s_inBedPlayerIds);
            SleepSkipPlugin.MenusOpened = true;
        }
        else
        {
            // Handle late joiners or players who left bed
            SendPopupToNewPlayers(s_inBedPlayerIds);
        }

        // Check if threshold is met
        float ratio = effectiveTotal > 0 ? (float)currentYes / effectiveTotal * 100f : 0f;
        if (ratio >= SleepSkipPlugin.Ratio.Value)
        {
            SleepSkipPlugin.SleepSkipLogger.LogDebug(
                $"Threshold of {SleepSkipPlugin.Ratio.Value}% reached ({ratio:F0}%), sleeping...");
            SleepSkipPlugin.LastSleepCompleted = now;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, nameof(SleepSkipPlugin.SleepVoteResult),
                Localization.instance.Localize("$sleep_vote_passed"));
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, nameof(SleepSkipPlugin.ResetVariables));
            __result = true;
            return false;
        }

        // Check if it's mathematically impossible to reach threshold
        int bestCaseYes = currentYes + (voteTimedOut ? 0 : unvotedCount);
        float bestCaseRatio = effectiveTotal > 0 ? (float)bestCaseYes / effectiveTotal * 100f : 0f;

        if (bestCaseRatio < SleepSkipPlugin.Ratio.Value)
        {
            SleepSkipPlugin.SleepSkipLogger.LogDebug(
                $"Vote failed: best possible {bestCaseRatio:F0}% < {SleepSkipPlugin.Ratio.Value}% threshold");
            SleepSkipPlugin.LastSleepCompleted = now;
            // Kick everyone from bed and notify
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStop");
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, nameof(SleepSkipPlugin.SleepVoteResult),
                Localization.instance.Localize("$sleep_vote_failed"));
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, nameof(SleepSkipPlugin.ResetVariables));
            __result = false;
            return false;
        }

        // Still waiting for votes
        __result = false;
        return false;
    }

    private static void ResetIfActive()
    {
        if (SleepSkipPlugin.LastSleepCheck == DateTime.MinValue && !SleepSkipPlugin.MenusOpened) return;
        // Reset cached display values so next vote cycle sends fresh RPCs
        s_lastDisplayInBed = 0;
        s_lastDisplayYes = 0;
        s_lastDisplayNo = 0;
        s_lastDisplayWaiting = 0;
        s_lastDisplayTotal = 0;
        // Reset locally immediately so stale state doesn't persist between ticks
        SleepSkipPlugin.ResetVariables(0);
        // Also broadcast to all clients
        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, nameof(SleepSkipPlugin.ResetVariables));
    }

    private static void SendWarningToAll(int timeRemaining)
    {
        foreach (ZNetPeer peer in ZNet.instance.m_peers)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_characterID.UserID, nameof(SleepSkipPlugin.UpdateWarningTime), timeRemaining);
        }

        // Host on listen server
        if (!ZNet.instance.IsDedicated())
            SleepSkipPlugin.UpdateWarningTime(0, timeRemaining);
    }

    private static void SendPopupToEligiblePlayers(HashSet<long> inBedPlayerIds)
    {
        foreach (ZNetPeer peer in ZNet.instance.m_peers)
        {
            long peerId = peer.m_characterID.UserID;
            if (!inBedPlayerIds.Contains(peerId))
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(peerId, nameof(SleepSkipPlugin.OpenMenuOnClient));
                SleepSkipPlugin.PopupSentTo.Add(peerId);
            }
        }

        // Host on listen server
        if (!ZNet.instance.IsDedicated())
        {
            long hostId = ZNet.GetUID();
            if (!inBedPlayerIds.Contains(hostId))
            {
                SleepSkipPlugin.OpenMenuOnClient(0);
                SleepSkipPlugin.PopupSentTo.Add(hostId);
            }
        }
    }

    private static void SendPopupToNewPlayers(HashSet<long> inBedPlayerIds)
    {
        foreach (ZNetPeer peer in ZNet.instance.m_peers)
        {
            long peerId = peer.m_characterID.UserID;
            if (!inBedPlayerIds.Contains(peerId) &&
                !SleepSkipPlugin.PopupSentTo.Contains(peerId) &&
                !SleepSkipPlugin.YesVoters.Contains(peerId) &&
                !SleepSkipPlugin.NoVoters.Contains(peerId))
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(peerId, nameof(SleepSkipPlugin.OpenMenuOnClient));
                SleepSkipPlugin.PopupSentTo.Add(peerId);
            }
        }

        // Host on listen server
        if (!ZNet.instance.IsDedicated())
        {
            long hostId = ZNet.GetUID();
            if (!inBedPlayerIds.Contains(hostId) &&
                !SleepSkipPlugin.PopupSentTo.Contains(hostId) &&
                !SleepSkipPlugin.YesVoters.Contains(hostId) &&
                !SleepSkipPlugin.NoVoters.Contains(hostId))
            {
                SleepSkipPlugin.OpenMenuOnClient(0);
                SleepSkipPlugin.PopupSentTo.Add(hostId);
            }
        }
    }

    private static void SendVoteDisplayToAll(int inBed, int yes, int no, int waiting, int total)
    {
        string data = $"{inBed},{yes},{no},{waiting},{total}";
        foreach (ZNetPeer peer in ZNet.instance.m_peers)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_characterID.UserID,
                nameof(SleepSkipPlugin.UpdateVoteDisplay), data);
        }

        // Host on listen server
        if (!ZNet.instance.IsDedicated())
            SleepSkipPlugin.UpdateVoteDisplay(0, data);
    }
}

[HarmonyPatch(typeof(UnifiedPopup), nameof(UnifiedPopup.Awake))]
static class SwapYesNoButonsUnifiedPopupAwakePatch
{
    static void Postfix(UnifiedPopup __instance)
    {
        SwapAnchoredPositions(__instance);
    }

    public static void SwapAnchoredPositions(UnifiedPopup __instance)
    {
        if (__instance.buttonLeft == null || __instance.buttonRight == null)
            return;

        RectTransform rtYes = __instance.buttonRight.GetComponent<RectTransform>();
        RectTransform rtNo = __instance.buttonLeft.GetComponent<RectTransform>();

        if (rtYes == null || rtNo == null)
            return;

        (rtYes.anchoredPosition, rtNo.anchoredPosition) = (rtNo.anchoredPosition, rtYes.anchoredPosition);
    }
}

[HarmonyPatch(typeof(UnifiedPopup), nameof(UnifiedPopup.IsVisible))]
static class UnifiedPopupIsVisiblePatch
{
    static void Postfix(UnifiedPopup __instance, ref bool __result)
    {
        if (!__result) return;
        if (UnifiedPopup.instance == null) return;
        if (UnifiedPopup.instance.headerText == null) return;
        if (UnifiedPopup.instance.headerText.text != Localization.instance.Localize("$sleep_skip")) return;

        // Dynamically update the popup body with current vote status
        UnifiedPopup.instance.bodyText.text = SleepSkipPlugin.FormatVoteBody();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}

[HarmonyPatch(typeof(Game), nameof(Game.Start))]
internal static class GameStartPatch
{
    static void Postfix(Game __instance)
    {
        ZRoutedRpc.instance.Register(nameof(SleepSkipPlugin.OpenMenuOnClient),
            SleepSkipPlugin.OpenMenuOnClient);
        ZRoutedRpc.instance.Register(nameof(SleepSkipPlugin.SleepStopNotify),
            new Action<long, string>(SleepSkipPlugin.SleepStopNotify));
        ZRoutedRpc.instance.Register(nameof(SleepSkipPlugin.ResetVariables),
            SleepSkipPlugin.ResetVariables);
        ZRoutedRpc.instance.Register(nameof(SleepSkipPlugin.UpdateWarningTime),
            new Action<long, int>(SleepSkipPlugin.UpdateWarningTime));
        ZRoutedRpc.instance.Register(nameof(SleepSkipPlugin.VoteYes),
            new Action<long, long>(SleepSkipPlugin.VoteYes));
        ZRoutedRpc.instance.Register(nameof(SleepSkipPlugin.VoteNo),
            new Action<long, long>(SleepSkipPlugin.VoteNo));
        ZRoutedRpc.instance.Register(nameof(SleepSkipPlugin.UpdateVoteDisplay),
            new Action<long, string>(SleepSkipPlugin.UpdateVoteDisplay));
        ZRoutedRpc.instance.Register(nameof(SleepSkipPlugin.SleepVoteResult),
            new Action<long, string>(SleepSkipPlugin.SleepVoteResult));
    }
}
