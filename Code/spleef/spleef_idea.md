# Spleef Game

Idea: The platform is made up of individual tiles, the tiles can be broken and fall away. There are several layers of the floor. If you fall through all the floors to the zone below you are out. The goal is to be the last man standing.

Game modes: 
- Traditional Spleef: Walking over the tile has a small delay before it falls away
- Spleef attack: Players use a weapon/tool to knock away the tiles
- Pattern match mode: Players are prompted to stand on a specific type of tile, they have limited time to get on a tile matching that style, when time is up if they are standing on the wrong one, they fall and are out. 


Game Components:
- Player

- Platform (composed of multiple tiles in various patterns)
    - Tiles
        - Health
        - OnPlayerEnter -> TimeUntil to cause it to break/fall
        - Wobble effect before falling
        - BreakTile()
            - Tile falls, collision off, TimeUntil -> Destroy obj

- KillZone
    - Area where player falls into
    - Kill the player, force them into a spectate mode


- Game level scene
    - variable for how many platform levels to have, auto create them and space apart
    - Needs a game state for when starting since it will spawn you on a tile and we dont want it to activate right away
    - We should show a UI countdown from 10 to 1, GO. After that enable all the triggers. 
        - during the countdown the players should be locked in place, not able to walk around, but can still move camera
    - need to add spawn points, make sure the spawn points are centered on a tile
        - can randomly pick a spawn point from an available tile, just ensure only one playe rspawns on a tile

Future enhancement for the coundown:
    - Put it in the world as a billboard, make all players spawn in looking at it. it can bounce up and down , and then when times up it can kind lift off and disappear
    - maybe its some prop on like a lil balloon or something and floats off and despawns when starts

- Scoreboard / Leaderboard

Scenes:
- Lobby Scene
    - players join in, maybe theres a lil area where they can run around and goof off while ppl are joining
    - a way to invite players, setting etc
    - a start button they can press from within the world when ready

- Game level (could have multiple variations)
