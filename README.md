# OpenPrefirePrac (WIP)
An open-source CounterStrikeSharp powered server-side practicing plugin for CS2. It provides multiple prefire practices on competitive map pool maps and support multiplayer practicing concurrently.

## How to use

Currently we only provides the Linux (amd64) version. Download the release file and put it into your counterstrikesharp plugin folder.

## How to create a practice profile?

The folder "map" is organize as follows. Each sub-folder in "map" contains practice profiles for the corresponding map. Each text file in that sub-folder is a practice profile.

A practice profile consists of 3 parts.

The first line contains the name of incompatible practices, separated by spaces.

The second line instructs the place and facing direction of the player. The first 3 floating numbers are the position and the other 3 are the rotation.

```
pos_x pos_y pos_z ang_x ang_y ang_z
```

The rest lines describe how to place bots. The first 3 numbers is position, following 3 numbers of the rotation. The 7thvalue is either True of False indicating whether the bot is crouching.

The positions and facing rotations can be retrived from in-game get\_pos command.

```
pos_x pos_y pos_z ang_x ang_y ang_z is_crouching
```

## Current development progress

Current it's still under active developing. I only did one practice profile (de\_inferno banana\_to\_b) other's are dummy profiles and might not be useful.

TODO:

1. Create prefire profiles for all maps.
2. Optimize how bots are spawned (bug fixes).
3. Add localization support.
4. Allow bots to shoot.
5. Give player weapons.
