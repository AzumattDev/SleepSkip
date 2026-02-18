> # Update Information (Latest listed first)
> ### 1.3.0
> #### Bug Fixes
> - Fix single decline canceling sleep for the entire server. Declining now records a "no" vote instead of vetoing everyone. The vote only fails when it's mathematically impossible to reach the threshold.
> - Fix malformed RPC in OnAcceptSleep that caused accept notifications to silently fail.
> - Fix in-bed players being double-counted when they also received and accepted the vote popup. Popup is now only sent to players who are not in bed.
> - Fix vote state (AcceptedSleepCount) not resetting when all players leave bed, causing stale votes to carry over.
> - Fix listen server host never seeing warning countdowns or sleep cancel/result notifications.
> - Fix InCombat flag being a sticky static that could persist across vote cycles. Now uses a local variable per check.
> - Remove unused SleepDelayInMinutes config field.
> #### New Features
> - **Per-player vote tracking**: Votes are now tracked by player ID using HashSets, preventing double-voting and enabling accurate vote counts.
> - **Vote timeout**: New "Vote Timeout" config (default 45s). After the timeout, non-responding players are counted as abstaining and removed from the vote denominator, so AFK players can't block sleep.
> - **Sleep cooldown**: New "Sleep Cooldown" config (default 0, disabled). Prevents repeated sleep vote attempts within a configurable window.
> - **Live vote display in popup**: The popup now shows real-time vote status including in-bed count, yes votes, no votes, waiting count, total players, and the required threshold percentage.
> - **Late joiner support**: Players who join or leave bed mid-vote now receive the popup and can participate.
> - **Vote result notifications**: All players (including the host) receive a clear message when a vote passes or fails, and the popup is automatically dismissed.
> ### 1.2.4
> - Update to fix localization issues in 0.221.10
> ### 1.2.3
> - Swap buttons in the yes/no popup to make Shroud's team happy. He's a nice guy, so I'm nice back.
> ### 1.2.2
> - Update SeverSync internally
> ### 1.2.1
> - Allow skipping time if one player is alone in bed and the only one on the server.
> - Fix small issue with UI reset.
> ### 1.2.0
> - Add a configuration option to always accept/deny sleep requests. (Client option, not synced with server)
> - Add configuration option for the amount of players needed in a bed to begin a vote. Default 2 (Server option, synced with server)
> - Add a configuration option to control the amount of warning time before a popup appears. Default: 15 seconds (Server option, synced with server)
> - Update Localization files (English, Chinese, Dutch, French, German, Italian, Japanese, Korean, Polish, Portuguese, Russian, Spanish, Swedish)
> ### 1.1.1
> - Recompile for Ashlands
> ### 1.1.0
> - If you had fast enough FPS, the popup instance wouldn't be ready when the mod tried to use it, causing NRE spam. This should fix that issue.
> ### 1.0.9
> - Move to using the game's UnifiedPopup system. This should fix the issue with the popup.
> - Add some new translations for the new popup and a few that I left out.
> ### 1.0.8
> - Fix issue with menu popup. Mouse now appears correctly.
> - Prevent the popup from appearing if the player is in combat. This automatically denies the popup resulting in a denied sleep request.
> ### 1.0.7
> - Update for Valheim 0.217.22
> ### 1.0.6
> - Update for Valheim 0.216.9
> ### 1.0.5
> - Revert the cooldown change. It was causing issues with the mod. I will look into it more in the future.
> ### 1.0.4
> - Update to use a cooldown in minutes. This will prevent spamming the sleep request.
> - Add localization support.
> ### 1.0.3
> - Update ServerSync internally
> ### 1.0.2
> - Update ServerSync internally
> ### 1.0.1
> - Update ServerSync internally
> ### v1.0.0
> - Initial Release
