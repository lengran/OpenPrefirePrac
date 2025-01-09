# OpenPrefirePrac

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
  - !prefire df [1-6]: Set the difficulty.
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
  - 3: Reheal to 100hp after a kill.
  - 4: +100hp for each kill.
  - 5: +500hp for each kill.
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
  - 0: CS2's native bot behavior. It works in a consistant manner but is less powerful.
  - 1: CSS based aim lock: Bots always aim at players' heads. But this may conflict with CS2's native bot logic, causing bots to not react under certain circumstances.
  - 2: Behavior tree based aim lock: Hard mode.
- EquipPlayer:
  - 0: Disabled. To avoid interfering with other weapon related plugins
  - 1: Enabled (default). Players will be equipped with AK47, deagle and nades every time they respawn.

For detailed discussion on bot difficulty, please refer to [this issue](https://github.com/lengran/OpenPrefirePrac/issues/17).

## Development

### Provide translation

First you can make a copy of an existing translation profile from the *lang* folder and start translate the sentences into your language.

Then you might want to create a pull request. Because the plugin uses player's IP to decide the language shown to each player, a mapping from country to language needs to be hard coded into the translator module.

### How to customize a practice profile?

The folder "*maps*" is organized as follows: Each sub-folder in "*maps*" contains practice profiles for the corresponding map. Each text file in that sub-folder is a practice profile.

A practice profile consists of five parts:

The first line contains **the file names of practice profiles** that might interfere with this practice, separated by spaces.

The second line contains **2 numbers**, indicating how many bots are needed in this practice and a spawn ratio between 0 and 1 (how many of them should be spawned in random practice mode) respectively.

The third line instructs the place and facing direction of the player. The first 3 floating numbers are the position and the other 3 are the rotation. This line should only contain **6 numbers**.

```text
pos_x pos_y pos_z ang_x ang_y ang_z
```

The fourth part with an arbitrary number of lines describes spawn positions of bots. Bot will be spawn in the same order as that of the lines. 

There should be **at least 6 numbers and a True or False value** in each line. The first 3 numbers describe position, following 3 numbers indicating the rotation angle. The 7th value is either a *True* or a *False* indicating whether the bot is crouching. You can have comments at the end of the line if needed (optional). 

The positions and rotations can be retrieved from in-game get\_pos command. But please notice that, the height values used in profiles should be the values returned by get\_pos minus 64. I made a python script that does this calculation for you. The usage is explained in [next section](https://github.com/lengran/OpenPrefirePrac?tab=readme-ov-file#how-to-use-the-python-helper-script-to-convert-heights).

```text
pos_x pos_y pos_z ang_x ang_y ang_z is_crouching
```

The fifth part with an arbitrary number of lines describes joint points of a guiding line. The guiding line is used to provide a better narration of how the practice is designed to be played. This also requires a bit calculation on height of joints and it can also be handled by the python script.

Each line contains **3 numbers**.

```text
pos_x pos_y pos_z
```

### How to use the python helper script to convert heights

The script *calculate_height.py* receives output of the *getpos* in-game function and automatically convert them to the proper format of bot spawn positions and joint positions of a guiding line. The steps of how it is used is described as follows.

Step 1: Open the game console and use the *getpos* command while you are standing not crouching.

Step 2: Copy the output to a txt file. 

    For bot spawn points, copy the entire line of output. If you want the bot to crouch, append a *True* at the end of the line. You can also put a *False* if you want the bot to stand. But it's not required since the python script can do that for you. If you want to give a comment to make the practice profile more reader-friendly, feel free to attach your words after the *True* or *False*. 
  
    For joints of a guiding line, only the position part (starting from *"getpos"* to the semicolon in the middle of the line) is needed. Comments are not supported for joints.

Step 3: After finish stacking the lines, you can simply pass the txt file to the python script and the script will automatically print out the formatted lines. Just copy the output and paste them at the end of your customized practice profile.

```bash
python3 calculate_height.py [PATH TO YOUR FILE]
```

Here's an example of the file you put in the text file and the output of the script.

Your text file should look like this:

```text
setpos 1.11111 1.222222 64.333333;setang 1.444444 1.555555 0.000000
setpos 2.11111 2.222222 64.333333;setang 2.444444 2.555555 0.000000 False
setpos 3.11111 3.222222 64.333333;setang 3.444444 3.555555 0.000000 True
setpos 4.11111 4.222222 64.333333;setang 4.444444 4.555555 0.000000 Here's some info about this position.
setpos 5.11111 5.222222 64.333333;setang 5.444444 5.555555 0.000000 False Here's some info about this position.
setpos 6.11111 6.222222 64.333333;setang 6.444444 6.555555 0.000000 True Here's some info about this position.
setpos 1.11111 1.222222 64.333333
setpos 2.11111 2.222222 64.333333
setpos 3.11111 3.222222 64.333333
```

The corresponding output would be:

```text
1.11111 1.222222 0.3333329999999961 1.444444 1.555555 0.000000 False
2.11111 2.222222 0.3333329999999961 2.444444 2.555555 0.000000 False
3.11111 3.222222 0.3333329999999961 3.444444 3.555555 0.000000 True
4.11111 4.222222 0.3333329999999961 4.444444 4.555555 0.000000 False # Here's some info about this position.
5.11111 5.222222 0.3333329999999961 5.444444 5.555555 0.000000 False # Here's some info about this position.
6.11111 6.222222 0.3333329999999961 6.444444 6.555555 0.000000 True # Here's some info about this position.
1.11111 1.222222 9.333332999999996
2.11111 2.222222 9.333332999999996
3.11111 3.222222 9.333332999999996
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
  - CT aggressively push A Palace
- de_overpass
  - Attack B site from B long
  - Attack B site from B short
  - Clear underpass and go upwards to mid
  - Clear underpass and go towards B short
  - Attack A site from A long
  - Attack A site from A short (mid)
  - Retake B site from CT spawn
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
  - CT aggressively push from lower tunnel
  - CT aggressively push top mid
- de_nuke
  - Attack A site from hut
  - Attack B site from ramp
  - Entrance of lobby (T side)
  - From radio to ramp
  - Attack A site from Ramp/J-Hall
  - From T-side outside to secret
  - Attack B site from secret
  - Fast pace rush MINI from Silo
  - Attack A site from MINI
- de_vertigo
  - Attack B site from stairs
  - From mid to CT spawn
  - Attack A site from A ramp
  - Attack A site from scaffold
  - Retake B site from elevator
- de_train
  - Attack A site from A main
  - Attack A site from ivy
  - Attack A site from ladder room

TODO:

1. Create prefire profiles for all maps.
2. Improve behavior tree to allow each bot only aimlocks its owner.
3. Improve localization support (The supporting framework is done. Submitting translations is warmly welcomed.).
4. Reorganize the files and code structure. Try to put code into submodules to improve readability.
5. Reroute separate logs into one gathered place for better debug experience.

### About the future of this project

Since there are already a lot of awesome prefire maps in the workshop, I might not spend as much energy on this project as I used to do. But if you find any bugs or want some cool features, feel free to leave a comment. Thanks for the support and love of this project for such a long time. <3

## Reference

I have referred these open-source projects during the development.

- [shobhit-pathak/MatchZy: MatchZy is a plugin for CS2 (Counter Strike 2) for running and managing practice/pugs/scrims/matches with easy configuration and Get5 (G5API/G5V) support as well!](https://github.com/shobhit-pathak/MatchZy)
- [B3none/cs2-retakes: CS2 implementation of retakes. Based on the version for CS:GO by Splewis.](https://github.com/B3none/cs2-retakes)
- [aprox2/GeoLocationLanguageManagerPlugin: Language manager plugin for CSSharp that uses users ip for geo location.](https://github.com/aprox2/GeoLocationLanguageManagerPlugin)
- [daffyyyy/CS2-SimpleAdmin: Manage your Counter-Strike 2 server by simple commands :)](https://github.com/daffyyyy/CS2-SimpleAdmin)
- [5e-prac-mirage-prefire](https://steamcommunity.com/sharedfiles/filedetails/?id=3232466864)

This project is inspired by a close-source prefire plugin developed by https://space.bilibili.com/283758782.

Huge thanks.
