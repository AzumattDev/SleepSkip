using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SleepSkip;

[HarmonyPatch(typeof(Game), nameof(Game.EverybodyIsTryingToSleep))]
internal static class GameEverybodyIsTryingToSleepPatch
{
    // All of this code will run on the server. You need to tell the client the information here.
    static bool Prefix(Game __instance, ref bool __result)
    {
        // Amount of players in bed

        // Get all players
        List<ZDO> allCharacterZdos = ZNet.instance.GetAllCharacterZDOS();
        // Return false if none
        if (allCharacterZdos.Count == 0)
        {
            __result = false;
            return false;
        }

        // Count number of players in bed
        int count = allCharacterZdos.Count(zdo => zdo.GetBool(ZDOVars.s_inBed));

        // Don't run the rest of the code if no one is in bed
        if (count <= 0)
        {
            __result = false;
            return false;
        }

        // If the current DateTime.UtcNow is more than X minutes after SleepSkipPlugin.LastSleepCheck, allow sleeping
        /*if (DateTime.UtcNow > SleepSkipPlugin.LastSleepCheck.AddMinutes(SleepSkipPlugin.SleepDelayInMinutes.Value))
        {*/
        //SleepSkipPlugin.LastSleepCheck = DateTime.UtcNow;


        // If people are sleeping
        SleepSkipPlugin.AcceptedSleepingCount = count + SleepSkipPlugin.AcceptedSleepCount;
        // Calculate current ratio of people sleeping
        int sleepRatio = SleepSkipPlugin.AcceptedSleepingCount / allCharacterZdos.Count;

        // Update number display on the client
        foreach (ZNetPeer instanceMPeer in ZNet.instance.m_peers)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(instanceMPeer.m_characterID.UserID, "UpdateMenuNumberOnClient", SleepSkipPlugin.AcceptedSleepingCount);
        }

        if (!SleepSkipPlugin.MenusOpened)
        {
            foreach (ZNetPeer instanceMPeer in ZNet.instance.m_peers)
            {
                // Open menu on the client
                ZRoutedRpc.instance.InvokeRoutedRPC(instanceMPeer.m_characterID.UserID, "OpenMenuOnClient");
            }

            SleepSkipPlugin.MenusOpened = true;
        }


        // If the ratio of the amount of players sleeping vs awake reaches the threshold, return true to sleep
        if ((sleepRatio * 100) >= SleepSkipPlugin.ratio.Value)
        {
            SleepSkipPlugin.SleepSkipLogger.LogDebug($"Threshold of {SleepSkipPlugin.ratio.Value} reached, sleeping...");
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ResetEveryone");
            __result = true;
            return false;
        }

        // Otherwise, result false
        __result = false;

        return false;
        /*}*/
        /*else
        {*/
        __result = false;
        return false;
        /*}*/
    }
}

[HarmonyPatch(typeof(UnifiedPopup), nameof(UnifiedPopup.IsVisible))]
static class UnifiedPopupIsVisiblePatch
{
    static void Postfix(UnifiedPopup __instance, ref bool __result)
    {
        if (__result)
        {
            if(UnifiedPopup.instance == null) return; // Sometimes this code can run before the actual instance is ready
            if(UnifiedPopup.instance.headerText == null) return;
            if (UnifiedPopup.instance.headerText.text != Localization.instance.Localize("$sleep_skip")) return;
            string person = SleepSkipPlugin.AcceptedSleepingCount > 1
                ? Localization.instance.Localize("$want")
                : Localization.instance.Localize("$want_multiple");
            UnifiedPopup.instance.bodyText.text = string.Format(Localization.instance.Localize("$sleep_request"), SleepSkipPlugin.AcceptedSleepingCount, person);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}

[HarmonyPatch(typeof(Game), nameof(Game.Start))]
internal static class GameStartPatch
{
    static void Postfix(Game __instance)
    {
        // OpenMenu on the client
        ZRoutedRpc.instance.Register("OpenMenuOnClient", SleepSkipPlugin.OpenMenuOnClient);

        // SleepStopNotify
        ZRoutedRpc.instance.Register("SleepStopNotify", new Action<long, string>(SleepSkipPlugin.SleepStopNotify));

        // Update Clients that Accepted Value
        ZRoutedRpc.instance.Register("UpdateSleepCount", SleepSkipPlugin.UpdateSleepCount);

        // Client Reset, "extra measure" just in case.
        ZRoutedRpc.instance.Register("ResetEveryone", SleepSkipPlugin.ResetVariables);

        // Update the menu's number display on the client
        ZRoutedRpc.instance.Register("UpdateMenuNumberOnClient", new Action<long, int>(SleepSkipPlugin.UpdateMenuNumberOnClient));
    }
}