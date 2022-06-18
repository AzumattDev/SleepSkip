using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace SleepSkip;

[HarmonyPatch(typeof(Game), nameof(Game.EverybodyIsTryingToSleep))]
static class Game_EverybodyIsTryingToSleep_Patch
{
    static bool Prefix(Game __instance, ref bool __result)
    {
        // Amount of players in bed
        int count = 0;

        // Get all players
        List<ZDO> allCharacterZdos = ZNet.instance.GetAllCharacterZDOS();
        // Return false if none
        if (allCharacterZdos.Count == 0)
        {
            __result = false;
            return false;
        }

        // Count number of players in bed
        foreach (ZDO zdo in allCharacterZdos)
        {
            if (zdo.GetBool("inBed"))
                count++;
        }

        // Don't run the rest of the code if no one is in bed
        /*if (count <= 0)
        {
            __result = false;
            return false;
        }*/

        // Calculate current ratio of people sleeping
        double sleepRatio = (double)count / allCharacterZdos.Count;

        // If showMessage is true
        if (SleepSkipPlugin.message.Value)
        {
            // If people are sleeping
            if (count <= 1 || count >= 1)
            {
                SleepSkipPlugin.acceptedSleepCount = count;
                SleepSkipPlugin.Dialog!.SetActive(true);

                foreach (ZNetPeer instanceMPeer in ZNet.instance.m_peers)
                {
                    // Open menu on the client
                    ZRoutedRpc.instance.InvokeRoutedRPC(instanceMPeer.m_characterID.m_userID, "OpenMenuOnClient");
                    /*// Send message to everyone at everyone's position
                    ZRoutedRpc.instance.InvokeRoutedRPC(instanceMPeer.m_characterID.m_userID, "ChatMessage",
                        ZNet.instance.m_zdoMan.GetZDO(instanceMPeer.m_characterID).GetPosition(), 2,
                        "SkipSleep",
                        $"{count}/{allCharacterZdos.Count} sleeping ({Math.Round(sleepRatio * 100)} %)");*/
                }
            }
        }

        //SleepSkipPlugin.SleepSkipLogger.LogDebug($"Players sleeping: {count}");
        //SleepSkipPlugin.SleepSkipLogger.LogDebug($"Ratio of players sleeping: {sleepRatio}");
        //SleepSkipPlugin.SleepSkipLogger.LogDebug($"Threshold needed: {SleepSkipPlugin.ratio.Value}");

        // If the ratio of the amount of players sleeping vs awake reaches the threshold, return true to sleep
        if ((sleepRatio * 100) >= SleepSkipPlugin.ratio.Value)
        {
            SleepSkipPlugin.SleepSkipLogger.LogDebug(
                $"SkipSleep: Threshold of {SleepSkipPlugin.ratio.Value} reached, sleeping...");
            __result = true;
            return false;
        }

        // Otherwise, result false
        __result = false;

        return false;
    }
}

[HarmonyPatch(typeof(Menu), nameof(Menu.Start))]
static class Menu_Start_Patch
{
    static void Postfix(Menu __instance)
    {
        SleepSkipPlugin.Dialog = UnityEngine.Object.Instantiate(Menu.instance.m_quitDialog.gameObject,
            Hud.instance.m_rootObject.transform.parent.parent, true);
        Button.ButtonClickedEvent noClicked = new();
        noClicked.AddListener(onDeclineSleep);
        SleepSkipPlugin.Dialog.transform.Find("dialog/Button_no").GetComponent<Button>().onClick = noClicked;
        Button.ButtonClickedEvent yesClicked = new();
        yesClicked.AddListener(onAcceptSleep);
        SleepSkipPlugin.Dialog.transform.Find("dialog/Button_yes").GetComponent<Button>().onClick = yesClicked;
    }

    public static void onDeclineSleep()
    {
        SleepSkipPlugin.Dialog!.SetActive(false);
        SleepSkipPlugin.acceptedSleepCount = 0;
        // Notify everyone that they canceled sleep
        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStop", 2, "SkipSleepCanceled",
            $"Sleep canceled by {Player.m_localPlayer.GetPlayerName()}");
        // Send RPC to kick everyone from their bed
    }

    public static void onAcceptSleep()
    {
        //SleepSkipPlugin.Dialog!.SetActive(false);
        SleepSkipPlugin.acceptedSleepCount += 1;

        // Should update the value of how many accepted here.
    }
}

[HarmonyPatch(typeof(Menu), nameof(Menu.IsVisible))]
static class Menu_IsVisible_Patch
{
    static void Postfix(Menu __instance, ref bool __result)
    {
        if (SleepSkipPlugin.Dialog && SleepSkipPlugin.Dialog?.activeSelf == true)
        {
            SleepSkipPlugin.Dialog!.transform.Find("dialog/Exit").GetComponent<Text>().text =
                $"{SleepSkipPlugin.acceptedSleepCount} people want to sleep. Do you?";
            __result = true;
        }
    }
}

[HarmonyPatch(typeof(Game), nameof(Game.Start))]
static class Game_Start_Patch
{
    static void Postfix(Game __instance)
    {
        // OpenMenu on the client
        ZRoutedRpc.instance.Register("OpenMenuOnClient",SleepSkipPlugin.OpenMenuOnClient);

        // Send client choice to the server
        ZRoutedRpc.instance.Register<Vector3, int, string, string>("SleepSkipSendToServer",
            new RoutedMethod<Vector3, int, string, string>.Method(this.RPC_ChatMessage));

        // Update Clients that Accepted Value
        ZRoutedRpc.instance.Register<Vector3, int, string, string>("UpdateSleepCount",
            new RoutedMethod<Vector3, int, string, string>.Method(this.RPC_ChatMessage));
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.SetLocalPlayer))]
static class SetCharacterId
{
    private static void Postfix(Player __instance)
    {
        ZNet.instance.m_characterID = __instance.GetZDOID();
    }
}