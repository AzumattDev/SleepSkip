Server and Client mod that allows players to skip the night when a percentage of players agree to skip.

---

```
The mod must be installed on the server to work properly. The client must have the mod, or they will be kicked for a version mismatch.

NOTE: This mod uses ServerSync and a File watcher. Changes to the server config file will sync to clients, no server reboot needed.
      Changes via the BepInEx Configuration Manager by an admin will change the server config and sync to all clients. (Admin must be in game)
```

---

Popup will not appear if the player is in combat. This will automatically deny the sleep request.
Configuration options are available for the amount of players needed in a bed to begin a vote as well as the amount needed to accept, the amount of warning time before a popup appears, and the ability to always accept/deny sleep requests.

### Made at the request of `Kysen#6031` on discord.
---
<img align="center" width="1920" height="1080" src="https://i.imgur.com/gETGfPd.png">

---
> ## Installation Instructions
***You must have BepInEx installed correctly! I can not stress this enough.***

#### Windows (Steam)

1. Locate your game folder manually or start Steam client and :

* Right click the Valheim game in your steam library
* "Go to Manage" -> "Browse local files"
* Steam should open your game folder

2. Extract the contents of the archive into the BepInEx\plugins folder.
3. Locate Azumatt.SleepSkip.cfg under BepInEx\config and configure the mod to your needs

#### Server

`Must be installed on both the client and the server for syncing to work properly.`

1. Locate your main folder manually and :

* Extract the contents of the archive into the BepInEx\plugins folder.
* Launch your game at least once to generate the config file needed if you haven't already done so.
* Locate Azumatt.SleepSkip.cfg under BepInEx\config on your machine and configure the mod to your needs

2. Reboot your server. All clients will now sync to the server's config file even if theirs differs.

`Feel free to reach out to me on discord if you need manual download assistance.`

# Author Information

### Azumatt

`DISCORD:` Azumatt#2625

`STEAM:` https://steamcommunity.com/id/azumatt/

For Questions or Comments, find me in the Odin Plus Team Discord or in mine:

[![https://i.imgur.com/XXP6HCU.png](https://i.imgur.com/XXP6HCU.png)](https://discord.gg/Pb6bVMnFb2)
<a href="https://discord.gg/pdHgy6Bsng"><img src="https://i.imgur.com/Xlcbmm9.png" href="https://discord.gg/pdHgy6Bsng" width="175" height="175"></a>
