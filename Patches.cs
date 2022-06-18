using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

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
        int count = allCharacterZdos.Count(zdo => zdo.GetBool("inBed"));

        // Don't run the rest of the code if no one is in bed
        if (count <= 0)
        {
            __result = false;
            return false;
        }

        // If people are sleeping
        SleepSkipPlugin.AcceptedSleepingCount = count + SleepSkipPlugin.AcceptedSleepCount;
        // Calculate current ratio of people sleeping
        int sleepRatio = SleepSkipPlugin.AcceptedSleepingCount / allCharacterZdos.Count;

        // Update number display on the client
        foreach (ZNetPeer instanceMPeer in ZNet.instance.m_peers)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(instanceMPeer.m_characterID.m_userID, "UpdateMenuNumberOnClient",
                SleepSkipPlugin.AcceptedSleepingCount);
        }

        if (!SleepSkipPlugin.MenusOpened)
        {
            foreach (ZNetPeer instanceMPeer in ZNet.instance.m_peers)
            {
                // Open menu on the client
                ZRoutedRpc.instance.InvokeRoutedRPC(instanceMPeer.m_characterID.m_userID, "OpenMenuOnClient");
            }

            SleepSkipPlugin.MenusOpened = true;
        }


        // If the ratio of the amount of players sleeping vs awake reaches the threshold, return true to sleep
        if ((sleepRatio * 100) >= SleepSkipPlugin.ratio.Value)
        {
            SleepSkipPlugin.SleepSkipLogger.LogDebug(
                $"Threshold of {SleepSkipPlugin.ratio.Value} reached, sleeping...");
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ResetEveryone");
            __result = true;
            return false;
        }

        // Otherwise, result false
        __result = false;

        return false;
    }
}

[HarmonyPatch(typeof(Menu), nameof(Menu.Start))]
internal static class MenuStartPatch
{
    static void Postfix(Menu __instance)
    {
        SleepSkipPlugin.Dialog = UnityEngine.Object.Instantiate(Menu.instance.m_quitDialog.gameObject,
            Hud.instance.m_rootObject.transform.parent.parent, true);
        Button.ButtonClickedEvent noClicked = new();
        noClicked.AddListener(OnDeclineSleep);
        SleepSkipPlugin.Dialog.transform.Find("dialog/Button_no").GetComponent<Button>().onClick = noClicked;
        Button.ButtonClickedEvent yesClicked = new();
        yesClicked.AddListener(OnAcceptSleep);
        SleepSkipPlugin.Dialog.transform.Find("dialog/Button_yes").GetComponent<Button>().onClick = yesClicked;
    }

    private static void OnDeclineSleep()
    {
        SleepSkipPlugin.Dialog!.SetActive(false);
        // Send RPC to kick everyone from their bed
        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStop");
        // Notify everyone that they canceled sleep
        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStopNotify",
            $"Sleep canceled by {Player.m_localPlayer.GetPlayerName()}");

        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ResetEveryone");
    }

    private static void OnAcceptSleep()
    {
        SleepSkipPlugin.Dialog!.SetActive(false);
        //
        // Should update the value of how many accepted here.
        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "UpdateSleepCount");
        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStopNotify", 1,
            $"Sleep canceled by {Player.m_localPlayer.GetPlayerName()}");
    }
}

[HarmonyPatch(typeof(Menu), nameof(Menu.IsVisible))]
internal static class MenuIsVisiblePatch
{
    static void Postfix(Menu __instance, ref bool __result)
    {
        if (!SleepSkipPlugin.Dialog || SleepSkipPlugin.Dialog?.activeSelf != true) return;
        string person = SleepSkipPlugin.AcceptedSleepingCount > 1 ? "people want" : "person wants";
        SleepSkipPlugin.Dialog!.transform.Find("dialog/Exit").GetComponent<Text>().text =
            $"{SleepSkipPlugin.AcceptedSleepingCount} {person} to sleep. Do you?";
        __result = true;
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
        ZRoutedRpc.instance.Register("SleepStopNotify",
            new Action<long, string>(SleepSkipPlugin.SleepStopNotify));

        // Update Clients that Accepted Value
        ZRoutedRpc.instance.Register("UpdateSleepCount", SleepSkipPlugin.UpdateSleepCount);

        // Client Reset, "extra measure" just in case.
        ZRoutedRpc.instance.Register("ResetEveryone", SleepSkipPlugin.ResetVariables);

        // Update the menu's number display on the client
        ZRoutedRpc.instance.Register("UpdateMenuNumberOnClient",
            new Action<long, int>(SleepSkipPlugin.UpdateMenuNumberOnClient));
    }
}