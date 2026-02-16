# ModHearth Mod Manager for Dwarf Fortress
This is a modified mod manager for the steam version of Dwarf Fortress, made to interact with both DFHack and steam workshop mods. Yes this was vibe coded with the assistance of codex and claude, because I have 0 idea how c# and lua work. Yes you have absolutely the right to kill me and spit on my grave for this.

## User Information:

### Requirements:
- Dwarf Fortress steam version
- Windows PC (TODO: eventual mac and linux support planned)
- DFHack Installed
- Game has been launched at least once

### Installation Guide
1. Go to [releases]([https://www.google.ca](https://github.com/EggleEgg/ModHearth/releases)) and download the most recent version 
2. Extract zip to suitable location
3. Run ModHearth.exe
4. Locate Dwarf Fortress.exe

### Instructions
Information on the four buttons from left to right:
- Save button: saves the current modlist to file.
- Undo button: undoes changes made to the current modlist. Can only undo mod order/enable/disable changes, not renaming or deletion.
- Play button: plays the game.
- Reload button: not yet implemented.

## Contributor Information

### General Functionality
This tool works by pulling mods from the dwarf fortress mods folder, and pulling modpacks from the dfhack config mod-manager.json.
ModReferences are generated from found mod folders, while modpacks are generated from the dfhack config.
Loading modpacks into the game is done via altering mod-manager.json and using dfhacks normal mod management once the game loads.

### Term Definitions
#### DFHMod
DFHack only deals with mod name and mod version. This is all that's saved and loaded.
These are set up to act like a value type.  

#### ModReference
A more comprehensive object storing more information about mods, mostly for displaying. Is not saved.
Can easily be converted to a DFHMod.

#### DFHModPack
An object representing a modpack matching how DFHack handles them. Only has a name, a bool default, and a list of DFHMods.
DFHack stores a list of these in a json file, which it loads into the game. 
