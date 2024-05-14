# OpenPrefirePrac (WIP)

An open-source CounterStrikeSharp powered server-side practicing plugin for CS2. It provides multiple prefire practices on competitive map pool maps and support multiplayer practicing concurrently.

## Get started

### Requirement

- CounterStrikeSharp

### Installation

Download [the latest release files](https://github.com/lengran/OpenPrefirePrac/releases) and extract all files into "**game/csgo/addons/counterstrikesharp/plugins/OpenPrefirePrac/**".

To install the latest version of CounterStrikeSharp, please refer to this [guide](https://docs.cssharp.dev/docs/guides/getting-started.html).

## How to use

### Tips on operating a practice server

When starting a server, I recommend using these parameters.

```bash
[CS2 Installation Directory]/game/bin/linuxsteamrt64/cs2 -dedicated -insecure +map de_inferno -maxplayers_override 64 +game_alias competitive +sv_hibernate_when_empty 0
```

Note: "**-maxplayers_override 64**" is the most important one. It allows the server to add more than 5 bots on one team, which is crucial to achieve the goal of allowing multiplayer training simultaneously.

### Start prefire practice in game

Send "**!prefire**" in chatbox or use command "**css_prefire**" in console. This will bring up the main menu.

There are also some shortcut commands you can use.

  - !prefire prac [number]: Start practicing on a selected route.
  - !prefire map [map name]: Switch to another map.
  - !prefire df [1-5]: Set the difficulty.
  - !prefire mode [rand/full]: Set training mode.
  - !prefire bw [rand/ump/ak/sct/awp]: Set weapons for bots.
  - !prefire lang [en/pt/zh]: Set language. en for English, pt para português, 中文选择 zh。
  - !prefire exit: Stop practicing.

You can always use **!prefire help** to see how to use them.

### Adjust default settings

Now the plugin supports loading default settings of difficulty and training mode from a json file. You can rename *default_cfg.json.example* to *default_cfg.json* and modify the value as you like.

Explanation of values:

- Difficulty
  - 0: No healing.
  - 1: Init hp 500 with no healing.
  - 2: +25hp for each kill.
  - 3: +100hp for each kill.
  - 4: +500hp for each kill.
- Training Mode
  - 0: Random mode, randomly spawn some targets.
  - 1: Full mode, all targets.
- Bot Weapon
  - 0: Bots buy weapons randomly.
  - 1: Bots use UMP45.
  - 2: Bots use AK47.
  - 3: Bots use Scout.
  - 4: Bots use AWP.
- Aim lock for bots
  - true: Bots always aim at players' heads. But this may conflict with CS2's native bot logic, causing bots to not react under certain circumstances.
  - false: CS2's native bot behavior. It works in a consistant manner but is less powerful.

To customize bot difficulty, please refer to [this issue](https://github.com/lengran/OpenPrefirePrac/issues/17).

## Development

### Provide translation

First you can make a copy of an existing translation profile from the *lang* folder and start translate the sentences into your language.

Then you might want to create a pull request. Because the plugin uses player's IP to decide the language shown to each player, a mapping from country to language needs to be hard coded into the translator module.

### How to customize a practice profile?

The folder "*maps*" is organized as follows: Each sub-folder in "*maps*" contains practice profiles for the corresponding map. Each text file in that sub-folder is a practice profile.

A practice profile consists of five parts:

The first line contains the name of incompatible practices, separated by spaces.

The second line indicates how many bots are needed in this practice.

The third line instructs the place and facing direction of the player. The first 3 floating numbers are the position and the other 3 are the rotation.

```text
pos_x pos_y pos_z ang_x ang_y ang_z
```

The fourth part with an arbitrary number of lines describes how to place bots. The first 3 numbers is position, following 3 numbers of the rotation. The 7th value is either True of False indicating whether the bot is crouching.

```text
pos_x pos_y pos_z ang_x ang_y ang_z is_crouching
```

The positions and facing rotations can be retrieved from in-game get\_pos command. But please notice that, the height values used in profiles should be the values returned by get\_pos minus 65. I made a python script that does this calculation for you. You can stack the strings returned by get\_pos and put them in a txt file, and pass the file to the python script as described below and the script will automatically print out the formatted bot positions.

```bash
python3 calculate_height.py [PATH TO YOUR FILE]
```

The fifth part with an arbitrary number of lines describes joint points of a guiding line. The guiding line is used to provide a better narration of how the practice is designed to be played.

```text
pos_x pos_y pos_z
```

This can also be extracted from get\_pos using the python script. It will read in lines composed of 4 parts (e.g. "setpos 1348.844727 -989.403198 -103.968750") and calculate the height values for you.

```bash
python3 calculate_height.py [PATH TO YOUR FILE]
```

### Current development progress

Currently it's still under active developing.

Finished practices:

- de_inferno
  - A short to A site
  - A long to A site
  - A apartments to A site
  - Banana to B site
  - Retake B from CT spawn
- de_ancient
  - B ramp to B site
  - B house to B site
  - Mid to A site
  - A main to A site
  - Retake A from CT spawn
- de_mirage
  - Attack A site from A ramp (to CT spawn)
  - Attack B site from B apartments
  - Attack A site from A palace (to jungle)
  - Attack B site from mid
  - Attack A site from underpass
  - Retake B site from CT spawn
- de_overpass
  - Attack B site from B long
  - Attack B site from B short
  - Clear underpass and go upwards to mid
  - Clear underpass and go towards B short
  - Attack A site from A long
  - Attack A site from A short (mid)
- de_anubis
  - Attack B site from B main
  - Attack B site from mid (B palace)
  - Attack B site from water (B connector/E-Box)
  - Attack A site from mid (A connector)
  - Attack A site from A main
  - CT aggressively pushing from A main
  - CT aggressively pushing from B main
- de_dust2
  - Attack A site from A long
  - Attack A site from A short
  - Attack B site from tunnel
  - Attack B site from mid
- de_nuke
  - Attack A site from hut
  - Attack B site from ramp
  - Entrance of lobby (T side)

TODO:

1. Create prefire profiles for all maps.
2. Improve bot logic.
3. Improve localization support (The supporting framework is done. Submitting translations is warmly welcomed.).
4. Reorganize the files and code structure. Try to put code into submodules to improve readability.
5. Reroute separate logs into one gathered place for better debug experience.

## Reference

I have referred these open-source projects during the development.

- [shobhit-pathak/MatchZy: MatchZy is a plugin for CS2 (Counter Strike 2) for running and managing practice/pugs/scrims/matches with easy configuration and Get5 (G5API/G5V) support as well!](https://github.com/shobhit-pathak/MatchZy)
- [B3none/cs2-retakes: CS2 implementation of retakes. Based on the version for CS:GO by Splewis.](https://github.com/B3none/cs2-retakes)
- [aprox2/GeoLocationLanguageManagerPlugin: Language manager plugin for CSSharp that uses users ip for geo location.](https://github.com/aprox2/GeoLocationLanguageManagerPlugin)
- [daffyyyy/CS2-SimpleAdmin: Manage your Counter-Strike 2 server by simple commands :)](https://github.com/daffyyyy/CS2-SimpleAdmin)

This project is inspired by a close-source prefire plugin developed by https://space.bilibili.com/283758782.

Huge thanks.
