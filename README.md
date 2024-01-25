# OpenPrefirePrac (WIP)
An open-source CounterStrikeSharp powered server-side practicing plugin for CS2. It provides multiple prefire practices on competitive map pool maps and support multiplayer practicing concurrently.

## How to use

Currently I only provide the Linux (amd64) version. Download the release file and put it into your counterstrikesharp plugin folder.

When starting a server, I recommend using these parameters.

```base
[CS2 Installation Directory]/game/bin/linuxsteamrt64/cs2 -dedicated -insecure +map de_inferno -maxplayers_override 64
```

Note: "-maxplayers_override 64" is the most important one. It allows the server to add more than 5 bots on one team, which is crucial to achieve the goal of allowing multiplayer training simultaneously.

## How to create a practice profile?

The folder "map" is organize as follows. Each sub-folder in "map" contains practice profiles for the corresponding map. Each text file in that sub-folder is a practice profile.

A practice profile consists of 3 parts.

The first line contains the name of incompatible practices, separated by spaces.

The second line indicates how many bots are needed in this practice.

The third line instructs the place and facing direction of the player. The first 3 floating numbers are the position and the other 3 are the rotation.

```
pos_x pos_y pos_z ang_x ang_y ang_z
```

The rest lines describe how to place bots. The first 3 numbers is position, following 3 numbers of the rotation. The 7thvalue is either True of False indicating whether the bot is crouching.

The positions and facing rotations can be retrived from in-game get\_pos command.

```
pos_x pos_y pos_z ang_x ang_y ang_z is_crouching
```

## Current development progress

Current it's still under active developing.

Finished practices:

- de_inferno
    - A short to A site
    - A apartments to A site
    - Banana to B site
- de_dust2
    - Some dummy practices of little if not no use.

TODO:

1. Create prefire profiles for all maps.
2. Add localization support.
3. Allow bots to shoot and set bots difficulty.
4. Give player weapons.
5. Reroute saperate logs into one gathered place for better debug experience.