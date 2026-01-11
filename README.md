# nsnipes

> Snipes (diminutive for Snipers) is a text-mode networked computer game that was created in 1983 by SuperSet Software. Snipes is officially credited as being the original inspiration for NetWars.[1][2] It was one of the earliest text mode multi player games, running on Novell NetWare systems.




This is **My** 'interpretation' of the classic [Snipes](https://en.wikipedia.org/wiki/Snipes_(video_game)) game 

I first encountered this game on early versions of [Novell NetWare](https://en.wikipedia.org/wiki/NetWare).  A fantastic game that could be played across the network - alot of fun was had during 'lunchtimes', and I spent many an hour battling with colleagues across the network.  This project does not intend to be faithful to the original, the map will be different, the game play will be different (but similar);  but, what I hope to achieve with this is the ability to play this game with other players across the internet and bring back some of the nostalgia and FUN that the original had.
So this will be a 'stylised' version.  It's also 'my' version because I'm not trying to rip anyone off, or profit by copying.

This is an exercise in programming and networking as much as it is a journey into my distant memories.

## Building and Running

### Starting the gRPC Server

The multiplayer game requires a gRPC server to be running. Start it first:

**Linux/macOS (Bash):**
```bash
# Run without rebuilding
./run-server.sh

# Build and run
./run-server.sh -build
```

**Windows (PowerShell):**
```powershell
# Run without rebuilding
.\run-server.ps1

# Build and run
.\run-server.ps1 -build
```

The server will start on `http://localhost:5000` by default. You can configure the server address in the client code if needed.

### Starting the Game Client

Use the provided scripts to run the game:

**Linux/macOS (Bash):**

**Run without rebuilding** (uses already-built executable):
```bash
./run.sh
```

**Build and run** (rebuilds before running):
```bash
./run.sh -build
```

**Windows (PowerShell):**

**Run without rebuilding** (uses already-built executable):
```powershell
.\run.ps1
```

**Build and run** (rebuilds before running):
```powershell
.\run.ps1 -build
```

**Note**: The default behavior (without `-build` flag) runs the game without rebuilding, which is useful for multiplayer testing where you want to build in one terminal and run in multiple terminals without rebuilding each time.

## Starting a Game

### Single Player Game

1. Start the game client (see "Starting the Game Client" above)
2. From the intro screen menu, select **"Start a New Game"** (or press **S**)
3. You'll be prompted to **"Select Starting Level"** (default: 1, limit: 50)
   - Type a number (1-50) or press ENTER to use the default (1)
4. The game will start immediately with a clearing effect animation showing the level information
5. You can play solo - no network connection required for single player mode

### Multiplayer Game

**Prerequisites:**
- The gRPC server must be running (see "Starting the gRPC Server" above)
- All players must be able to connect to the same server (default: `localhost:5000`)

**To Host a Multiplayer Game:**

1. Start the gRPC server (if not already running)
2. Start the game client
3. From the intro screen menu, select **"Start Multiplayer"** (or press **M**)
4. You'll be prompted to **"Select Starting Level"** (default: 1, limit: 50)
   - Type a number (1-50) or press ENTER to use the default (1)
5. You'll be prompted for **"How many players?"** (1-5)
   - Type a number (1-5) or press ENTER to use the default (2)
   - **Note**: If you select 1 player, the game will start immediately in single-player mode (no network)
6. If you selected 2-5 players, you'll see a waiting screen with:
   - Your **6-character Game ID** (e.g., "ABC123")
   - Current player count (e.g., "1 of 3 players joined")
   - Join notifications as players join
7. Share the Game ID with other players
8. The game will automatically start when:
   - The maximum number of players join, OR
   - 60 seconds elapse (whichever comes first)

**To Join a Multiplayer Game:**

1. Ensure the gRPC server is running (same server as the host)
2. Start the game client
3. From the intro screen menu, select **"Join Multiplayer"** (or press **J**)
4. Enter the **6-character Game ID** provided by the host
5. You'll see a waiting screen showing:
   - Current player count
   - Join notifications as other players join
6. Wait for the game to start (when max players join or 60 seconds elapse)
7. The game will begin with all players synchronized

**Multiplayer Tips:**
- All players should use the same starting level for consistency
- The host controls game state (hives, snipes) - clients receive updates
- All players can move and shoot independently
- Player positions, bullets, and game state are synchronized in real-time
- If the host disconnects, the game may become unstable (host migration not yet implemented)

![Intro Screen](./nsnipes-intro.png)
The 'Intro Screen' will change quite a bit as multi-player gaming is added

![Game Play](./nsnipes-game.png)
Game play, your player remains central - as you move the map moves around you and is endlessly scrolling. i.e. if you go off the top of the map you seemlessly rejoin the bottom - the map feels massive.  Hives release snipes,  and snipes will wander around the maze, but be careful, as soon as they get a sniff of you, they'll start to home in on you.

You can shoot snipes, you have bullets that can be shot in any direction and will bounce off walls too!   You can shoot hives, though it will take 3 shots to destroy a hive - hives are valuable to shoot, as you'll gain points for shooting the hive plus points for all of the un-released snipes within the hive -- shoot them quickly to gain more points!


## Preface
When I started this project, I was in between jobs and had alot of spare time.  Since then i've been fortunate enough to be very busy working.  The downside, is that this project over the last 10 months or so has seen no activity.  While I am very keen to get this project completed to my initial vision, time is still very valuable to me.   So I decided to accellerate the development of this by using 'Vibe Coding' - I really do hate that term, there is nothing 'vibby' about what i'm doing - I'm using an AI tool 'Cursor', giving it instructions and letting it build some code for me.  When it gets it wrong i'm re-iterating my intent and coercing it down a more correct path.

I chose this route so that I could get this project closer to completion, but also to extend my skills in working with tools like Cursor.

It's not perfect, I know that - some of the code committed, I'm far from being 100% happy with - but each prompt, each code commit is moving me closer to a working game and closer to my vision of having NSnipes run multi-player over the internet.   I do believe there will be an exercise of 'hand-refinement' on the code,  but i'm reserving that till i'm closer to the end.

It's been an interesting and sometimes frustrating journey so far working in this way, and while as I say above the code is far from being 100% perfect, the game has developed at a pace that I wasn't able to commit my own personal time to.

So what's left to do:
- Multiplayer Enhancements
  - ✅ Start Multiplayer Game (implemented - prompts for player count, generates 6-character game ID, 60-second join window)
  - ✅ Join Multiplayer Game (implemented - prompts for game ID, waits for game to start)
  - ✅ Network game play (implemented - real-time synchronization of player positions, bullets, hives, snipes)
  - ✅ Player visibility synchronization (fixed - all players can now see each other)
  - ✅ Hive synchronization (fixed - hives visible to all players on game start and when joining)
  - ✅ Server configuration UI (implemented - configure server address/port, status display)
  - ⚠️ Bullet synchronization (partially working, needs refinement)
  - ⚠️ Full game state synchronization (scores, lives) - partially implemented, needs refinement
  - ✅ Multiplayer game end/results screen (implemented - shows rankings and scores when all players lose lives)
  - ❌ Option to restart another game with all the same players
  - ❌ Level progression synchronization in multiplayer (currently host-only)
- Technical Debt
  - ⚠️ Update Terminal.Gui library to latest develop branch (may require significant rework)
  - ⚠️ Fix global [ESC] key handling across all screens
  - ⚠️ Extensive testing needed for multiplayer stability



## Gameplay Summary

### Intro Screen and Menu System

**Intro Screen**
- Animated NSNIPES banner that scrolls in from the left over 2 seconds
- Menu system with the following options:
  - **Start a New Game**: Begins a new single-player game with a clearing effect animation
  - **Start Multiplayer**: Host a new multiplayer game (1-5 players, 60-second join window)
  - **Join Multiplayer**: Join an existing multiplayer game by entering a 6-character game ID
  - **Initials**: Allows setting 2-character player initials (A-Z, 0-9)
  - **Configure Server**: Configure gRPC server address and port (saved to nsnipes.json)
  - **Exit**: Exits the application
- Menu navigation:
  - Arrow keys or numeric keypad (2/8) to navigate
  - ENTER to select
  - Keyboard shortcuts: S (Start), J (Join), I (Initials), E/X (Exit)
- Initials are saved to `nsnipes.json` and persist between game sessions
- Default initials are "AA" if not set

**Clearing Effects**
- Animated clearing effect when starting a new game, respawning, or starting a new level
- Expanding rectangle of '*' characters reveals the map underneath
- Messages displayed during clearing:
  - "LEVEL n - x HIVES with y SNIPES" when starting a new level
  - "X Lives Left" when player loses a life (but still has lives remaining)

**Game Over Screen**
- When all players lose all lives, animated "GAME OVER" banner scrolls in from the left
- Banner displays "GAME OVER" with space between words (white block text on blue background)
- Shows "-< SCORES >-" header followed by player scores sorted by score (descending)
- Top player displayed in cyan, all other players in yellow
- Game stops (no movement, bullets, snipes)
- Press ENTER to return to the intro screen
- Game state is fully reset when starting a new game after game over

### Current Features

**Player**
- Player starts with 5 lives
- Player is represented as a 2x3 character sprite showing:
  - Animated eyes (◄► / ◂▸) that blink
  - Player initials (customizable, 2 characters)
- Player can move in 8 directions (cardinal and diagonal)
- Smooth continuous movement while keys are held down
- Player respawns at a random valid position when hit by a snipe
- Player position resets to random location at the start of each new level
- Game ends when all players lose all lives (multiplayer) or player loses all lives (single player)

**Map**
- Forever-scrolling maze map that wraps around both horizontally and vertically
- Map fills the entire console window (no border)
- Collision detection prevents player from walking through walls
- Player position is tracked by top-left corner of the 2x3 sprite
- Map viewport is centered on the player

**Hives**
- Hives are small 2x2 rectangular boxes made of corner characters (╔ ╗ ╚ ╝)
- Hives glow between cyan and green colors, changing every 75ms
- Each hive has its own flash rate that decreases by 1/3 each time it's hit (minimum 10ms)
- **Level-based configuration**:
  - Level 1: 4 hives, each with 10 snipes
  - Each level: +1 snipe per hive
  - Every 4 levels: +1 hive
  - Example: Level 1 = 4 hives × 10 snipes, Level 2 = 4 hives × 11 snipes, Level 5 = 5 hives × 14 snipes
- Hives spawn snipes over time (snipes split evenly between type 'A' and type 'B')
- Hives are positioned randomly but never overlap walls or the player
- **Hives can be destroyed**: Hives require 3 direct bullet hits to be destroyed
- When destroyed, all unreleased snipes from that hive are killed, and the player gains 500 points plus 25 points per unreleased snipe
- Destroyed hives are properly removed from the screen

**Snipes**
- Two types of snipes: Type 'A' (magenta) and Type 'B' (green)
- Each snipe displays as '@' symbol followed by a direction arrow
- Snipes spawn randomly from hives over time (roughly every 3 seconds per hive)
- Snipes spawn in random directions from their hive
- Snipes move intelligently:
  - Maintain their current direction unless they hit a wall, collide with another snipe, or the player gets close
  - Use a "heat radius" system: closer to player = more attracted, further away = more random movement
  - Maximum heat radius is 20 cells - beyond this, snipes move randomly
  - When player is within heat radius, snipes are attracted toward the player
- Snipes cannot walk through walls
- When a snipe hits a wall, it randomly chooses a new direction
- Snipes bounce off each other when they collide (reverse direction)
- Snipes move every 200ms
- If a snipe touches the player, the snipe explodes and the player loses 1 life
- Both the '@' character and arrow are properly cleared when snipes move or are killed

**Bullets**
- Player can fire bullets in 8 directions using QWEASDZXC keys
- Maximum of 10 bullets active at any time
- Bullets move at 1 cell per 10ms update (fast movement)
- Bullets bounce off walls:
  - Horizontal walls reverse Y direction
  - Vertical walls reverse X direction
  - Corners use approach direction to determine bounce
- Bullets expire after 2 seconds
- Bullets are displayed as flashing red '*' characters (alternating bright red and red)
- Bullets fire from the appropriate player edge/corner based on direction
- **Bullets can kill snipes**: When a bullet hits a snipe (or snipe moves into bullet), both are removed and player gains 25 points
- **Bullets can damage hives**: When a bullet hits a hive, the bullet stops and is removed, and the hive takes 1 hit (3 hits to destroy)
- Bullets are properly cleared from the screen when they expire or hit targets

**Status Bar**
- Two rows at the top of the screen with dark blue background and white text
- Displays: Hives (remaining/total), Snipes (remaining/total), Lives, Level, and Score
- Status bar is updated periodically and shows current game state

**Game State**
- Tracks current level (starts at 1)
- Tracks player score (25 points per snipe killed, 500 points for hive + 25 per unreleased snipe)
- Tracks total and remaining hives
- Tracks total and remaining snipes
- Game state is fully reset when starting a new game

**Combat System**
- **Bullet-Snipe Collision**: When a bullet hits a snipe (or snipe moves into bullet), the snipe is killed, bullet is removed, and player gains 25 points
- **Bullet-Hive Collision**: When a bullet hits a hive, the bullet stops and is removed. Hives require 3 direct hits to be destroyed
- **Hive Destruction**: When a hive is destroyed (after 3 hits), all unreleased snipes from that hive are killed, and the player gains 500 points plus 25 points per unreleased snipe
- **Player-Snipe Collision**: When a snipe touches the player, the snipe explodes, player loses 1 life, and player respawns at a random position

## Controls

### Movement
- **Arrow Keys** or **Numeric Keypad (2, 4, 6, 8)**: Move in cardinal directions (up, down, left, right)
- **Numeric Keypad (1, 3, 7, 9)**: Move diagonally
  - 7: Up-Left
  - 8/↑: Up
  - 9: Up-Right
  - 4/←: Left
  - 6/→: Right
  - 1: Down-Left
  - 2/↓: Down
  - 3: Down-Right

### Shooting
- **Q**: Fire diagonally up-left
- **W**: Fire up
- **E**: Fire diagonally up-right
- **A**: Fire left
- **D**: Fire right
- **Z**: Fire diagonally down-left
- **X**: Fire down
- **C**: Fire diagonally down-right

### Menu Navigation (Intro Screen)
- **Arrow Keys** or **Numeric Keypad (2, 8)**: Navigate menu up/down
- **ENTER**: Select current menu option
  - **S**: Quick select "Start a New Game"
  - **M**: Quick select "Start Multiplayer"
  - **J**: Quick select "Join Multiplayer"
  - **I**: Quick select "Initials"
  - **C**: Quick select "Configure Server"
  - **E** or **X**: Quick select "Exit"
- **ESC**: From intro screen exits application; from game returns to intro screen

### Initials Input
- When "Initials" option is selected, type 2 characters (A-Z, 0-9)
- Characters are automatically uppercased
- Input ends automatically after 2 characters are entered
- Initials are saved to `nsnipes.json` and persist between sessions
- **Backspace**: Delete last character
- **ESC**: Cancel input

## Recent Changes

### Multiplayer Synchronization Fixes (Latest)
- **Player Visibility Fix**: Fixed issue where Player 2 couldn't see Player 1
  - Problem: Position updates were received but network players weren't being created if player wasn't in game session
  - Solution: Network players are now created from position updates even if not yet in game session (with default values)
  - Players are automatically added to game session for consistency
- **Hive Synchronization**: Fixed issue where hives weren't visible to joining players
  - Host now sends complete game state snapshot when players join (includes hives, snipes, and all player positions)
  - Game state snapshot sent both when game starts and when players join mid-game
  - Clients properly process and display all hives from the snapshot
- **Game State Snapshot Improvements**:
  - Host includes all players in snapshot (from game session, not just network players)
  - Ensures newly joined players receive complete game state
  - Snapshot includes hives, snipes, and all player positions with world coordinates
- **Position Update Reliability**:
  - Periodic position updates (every 200ms) ensure players are visible even when stationary
  - Position updates create network players if they don't exist yet
  - Improved handling of position updates arriving before game state snapshot

### Server Configuration UI (Latest)
- **Server Configuration Menu**: Added "Configure Server" option to intro screen menu
  - Allows players to set custom gRPC server address and port
  - Configuration saved to `nsnipes.json` and persists between sessions
  - Defaults to `localhost:5000` if not configured
- **Server Status Display**: Real-time server connectivity status on intro screen
  - Green indicator when server is online and reachable
  - Red indicator when server is offline or unreachable
  - Status checked periodically using lightweight gRPC connectivity test
  - Positioned at bottom of screen (one row up from absolute bottom)

### Bug Fixes (Latest)
- **Bullet Removal**: Fixed visual artifacts where bullets weren't removed from screen after hitting targets
  - Bullets now properly cleared when hitting players, snipes, or hives
  - Bullets cleared when expired or hit on remote clients
- **Window Resize Handling**: Fixed map not redrawing when terminal window is resized
  - Map now redraws immediately when window dimensions change
  - Proper clearing and redrawing of entire game area on resize
  - Cached map viewport invalidated on resize
- **Multiplayer Game Start Flow**: Fixed intro screen being redisplayed instead of waiting for players
  - Host now immediately shows waiting screen with "Connecting..." game ID
  - Game ID updates to actual ID once received from server
  - Proper waiting screen display with player count and join notifications
- **Menu Display Fixes**:
  - Fixed missing "Exit" option in menu (incorrect menu item count calculation)
  - Fixed screen jump when first navigating menu
  - Moved server status message up by one row for better visibility
- **Intro Screen Animation**: Fixed menu not appearing after player character exits intro screen
  - Player character now properly leads banner across screen and exits
  - Menu appears correctly after animation completes
  - Proper timing and positioning for smooth animation

### gRPC Multiplayer Implementation
- **Replaced MQTT with gRPC**: Complete migration from MQTT to gRPC for multiplayer networking
  - **Why gRPC?**: Lower latency, better performance, type-safe protocol buffers, built-in .NET support
  - **Server Architecture**: Dedicated gRPC server (`NSnipes.GrpcServer`) manages game rooms and message routing
  - **Client Architecture**: `GrpcGameClient` replaces `MqttGameClient` with bidirectional streaming
  - **Protocol Buffers**: All game messages defined in `game.proto` for efficient binary serialization
- **Server Scripts**: Added `run-server.sh` and `run-server.ps1` for easy server startup
  - Default server address: `http://localhost:5000`
  - Same `-build` flag support as client scripts
- **Improved Latency**: gRPC's binary protocol and HTTP/2 provide significantly lower latency than MQTT
- **Type Safety**: Protocol buffers provide compile-time type checking for all game messages
- **Better Error Handling**: Structured error responses and connection management
- **Bidirectional Streaming**: Real-time game messages flow through a single persistent connection
- **Game Room Management**: Server manages game rooms, player connections, and message routing
- **Backward Compatibility**: Single-player mode unchanged (no network required)

### Build Script Enhancement
- **run.sh Script Update**: Modified `run.sh` to support optional building
  - Default behavior: `./run.sh` runs without rebuilding (uses `--no-build` flag)
  - Build flag: `./run.sh -build` rebuilds the project before running
  - Useful for multiplayer testing: build once in one terminal, run in multiple terminals without rebuilding
- **run.ps1 Script Added**: Created PowerShell version for Windows compatibility
  - Default behavior: `.\run.ps1` runs without rebuilding (uses `--no-build` flag)
  - Build flag: `.\run.ps1 -build` rebuilds the project before running
  - Same functionality as `run.sh` for cross-platform support

### Bug Fixes (Latest)
- **Snipe Count Display Fix**: Fixed incorrect snipe count display (was showing 80/40 instead of 40/40)
  - Issue: `SnipesUndestroyed` was being incremented when snipes spawned, causing double counting
  - Fix: `SnipesUndestroyed` now correctly represents all snipes (in hives + spawned), only decreases when snipes are killed or hives are destroyed
  - Status bar now correctly shows "40/40" at start of Level 1 (40 snipes undestroyed out of 40 total)

### Level System Implementation
- **Level Progression**: Implemented complete level system with automatic progression
  - Level 1 starts with 4 hives, each with 10 snipes
  - Each level increases snipes per hive by 1
  - Every 4 levels adds 1 additional hive
  - Level completion when all hives and all snipes are destroyed
- **Level Start Screen**: Animated clearing screen shows "LEVEL n - x HIVES with y SNIPES" at start of each level
- **Player Respawn**: All players reset to random positions at the start of each new level
- **Level State Management**: Proper level tracking and progression in both single-player and multiplayer modes

### Game Over Screen (Latest)
- **Animated Banner**: "GAME OVER" banner animates in from the left (white block text on blue background)
- **Banner Spacing**: Space between "GAME" and "OVER" words for better readability
- **Player Scores Display**: Shows all players sorted by score (descending) with "-< SCORES >-" header
- **Visual Hierarchy**: Top player displayed in cyan, all other players in yellow
- **Key Handling**: ENTER key returns to intro screen (other keys ignored)
- **Code Organization**: Moved game over screen to separate `GameOverScreen` class for better separation of concerns
- **Multiplayer Support**: Game over triggers when ALL players lose all lives, showing all player scores

### Multiplayer Implementation
- **gRPC Networking**: Implemented full multiplayer support using gRPC protocol (replaced MQTT)
  - **Server**: Dedicated gRPC server manages game rooms and message routing
  - **Client**: `GrpcGameClient` handles connection, game creation/joining, and bidirectional streaming
  - **Protocol Buffers**: All game messages defined in `.proto` files for efficient binary serialization
  - **Bidirectional Streaming**: Real-time game messages flow through persistent HTTP/2 connections
- **Game Discovery**: Host can create games with 6-character game IDs, clients can join by ID
- **Real-time Synchronization**: Player positions, bullets, hives, and snipes synchronized across all clients
- **Host-Client Architecture**: Host is authoritative for game state (hives, snipes), all players can move and shoot
- **Player Rendering**: Remote players displayed in yellow, local player in white/blue
- **Position Synchronization**: Fixed initial position sync issues, proper world coordinate system
- **Respawn Synchronization**: Player respawn positions properly synchronized across network
- **Initials Synchronization**: Player initials correctly displayed for all players
- **Network Message System**: Comprehensive protocol buffer messages for all game events (positions, bullets, game state)
- **Single Player Mode**: When starting multiplayer with 1 player, game starts immediately without network (local play only)

### Intro Screen and Menu System
- **Intro Screen**: Added animated NSNIPES banner that scrolls in from the left over 2 seconds
- **Menu System**: Implemented full menu with navigation, selection, and keyboard shortcuts
- **Multiplayer Menu Options**: Added "Start Multiplayer" and "Join Multiplayer" options
- **Waiting Screen**: Multiplayer waiting screen showing player count, game ID, and join notifications
- **Initials System**: Players can set and save their 2-character initials (persisted to nsnipes.json)
- **Clearing Effects**: Animated clearing effect when starting game, respawning, or starting new level, with messages
- **Game Reset**: Full game state reset when starting a new game after game over
- **Code Refactoring**: Moved all intro screen code to separate `IntroScreen` class, game over to `GameOverScreen` class for better organization

### Player Movement Improvements
- **Continuous Movement**: Player movement now supports smooth continuous movement while keys are held
- **Key State Tracking**: Improved keyboard handling for more natural direction changes
- **Movement Responsiveness**: Player can change direction immediately when pressing new movement keys
- **Instant Direction Changes**: Reduced key release detection delay from 150ms to 60ms for faster response
- **Immediate Movement Processing**: New movement keys trigger immediate movement processing (not just on timer), eliminating stutter when changing directions
- **Smooth Transitions**: When switching from one direction to another (e.g., holding Left, then pressing Up), movement responds instantly without pause

### Combat and Scoring System
- **Bullet-Snipe Collision**: Implemented collision detection between bullets and snipes (both directions)
  - Bullets can hit snipes at their position or arrow position
  - Snipes can move into bullet positions
  - On collision: snipe is killed, bullet is removed, player gains 25 points
  - Both snipe '@' character and arrow are properly cleared when killed
- **Bullet-Hive Collision**: Implemented hive damage system
  - Bullets stop and are removed when hitting a hive
  - Hives track hit count (3 hits required to destroy)
  - Hive flash rate decreases by 1/3 each time it's hit (minimum 10ms)
  - When destroyed: hive is removed from screen, all unreleased snipes are killed, player gains 500 points + 25 per unreleased snipe
- **Scoring System**: Fully functional scoring with points awarded for:
  - Killing snipes: 25 points each
  - Destroying hives: 500 points base + 25 points per unreleased snipe
- **Status Bar Updates**: Displays Level and Score in addition to hives, snipes, and lives

### Visual and Performance Improvements
- **Refined Snipe Clearing Algorithm**: Implemented sophisticated position tracking system
  - Tracks all previous snipe positions (both '@' and arrow)
  - Only clears positions that are no longer occupied by any snipe
  - Prevents artifacts when multiple snipes move in close proximity
  - Handles cases where snipes don't move but direction changes
  - Previous positions are updated after drawing to ensure accuracy
- **Artifact Elimination**: Fixed remaining visual artifacts from snipe movement
  - Both '@' characters and arrows are now properly cleared
  - Works correctly even with many snipes spawning from hives
  - Handles edge cases like snipes colliding and bouncing back

### Core Game Systems
- **Hive System**: Implemented hives that spawn snipes, with visual representation (glowing cyan/green boxes)
- **Snipe System**: Implemented intelligent snipes with two types ('A' and 'B'), movement AI, and collision detection
- **Bullet System**: Implemented player shooting with 8-directional firing, wall bouncing, and lifetime management
- **Status Bar**: Two-row status display showing game statistics (hives, snipes, lives, level, score)

### Player Mechanics
- **Player Lives**: Player starts with 5 lives
- **Player Respawn**: Player respawns at random valid position when hit by a snipe
- **Collision Detection**: Improved player-wall collision to properly handle 2x3 player sprite
- **Player Initials**: Customizable 2-character initials displayed on player sprite

### Snipe AI and Behavior
- **Heat Radius System**: Snipes are attracted to player based on distance (closer = more attracted)
- **Direction Persistence**: Snipes maintain direction unless hitting walls, colliding with other snipes, or player gets close
- **Snipe-to-Snipe Collision**: Snipes bounce off each other when they collide
- **Random Spawning**: Snipes spawn from hives in random directions
- **Wall Avoidance**: Snipes randomly choose new direction when hitting walls
- **Snipe Display**: Uses '@' symbol (Type 'A' = magenta, Type 'B' = green)

### Visual Improvements
- **Full-Screen Display**: Removed border, map fills entire console
- **Snipe Colors**: Type 'A' = magenta, Type 'B' = green
- **Bullet Appearance**: Flashing red '*' characters
- **Hive Animation**: Smooth color transitions (cyan/green) every 75ms, with individual flash rates
- **Artifact Fixes**: Fixed '@' and arrow artifacts left behind by snipe movement
- **Clearing Effects**: Smooth animated transitions when starting game or respawning

### Performance Optimizations
- **Separate Timers**: Hives and snipes have their own update timers for better performance
- **Viewport Culling**: Only visible objects are drawn
- **Efficient Redrawing**: Sophisticated position tracking ensures only necessary positions are cleared
- **Smart Clearing**: Uses HashSet-based position tracking to avoid clearing positions still occupied by other snipes
- **Caching**: Map viewport and status bar values are cached to reduce redundant calculations

### Technical Improvements
- **Map Wrapping**: Proper handling of coordinate wrapping for all game entities
- **Collision Detection**: Comprehensive collision detection for player, bullets, snipes, and hives
- **Game State Management**: Centralized game state tracking with scoring
- **Code Organization**: Separated intro screen logic into `IntroScreen` class
- **Configuration Management**: Game configuration (initials) persisted to JSON file

## What Works

### Core Gameplay
✅ Player movement (8 directions with smooth continuous movement)  
✅ Wall collision detection (prevents player from walking through walls)  
✅ Bullet firing and movement (8 directions)  
✅ Bullet wall bouncing (horizontal/vertical wall detection)  
✅ Bullet-snipe collision (both directions)  
✅ Bullet-hive collision and damage (3 hits to destroy)  
✅ Player-snipe collision and life loss  
✅ Player respawn on death (random valid position)  
✅ **Level completion detection** (when all hives and snipes destroyed)  
✅ **Level progression** (automatic advancement to next level with increased difficulty)  
✅ **Level start screen** (animated clearing with level info)  
✅ Game over detection and screen  

### Game Entities
✅ Hive spawning and display (glowing cyan/green animation)  
✅ Hive destruction (3 hits required) - properly cleared from screen  
✅ Hive flash rate decreases when hit  
✅ Snipe spawning from hives (random directions)  
✅ Snipe movement and AI (heat radius attraction system)  
✅ Snipe-to-snipe collision and bouncing  
✅ Snipe wall collision and direction change  
✅ Clean visual rendering - no artifacts from snipe movement  

### UI and Menus
✅ Intro screen with animated banner  
✅ Menu system with navigation  
✅ Initials input and persistence (saved to nsnipes.json)  
✅ Clearing effect animations (game start, respawn, level start)  
✅ Status bar display (hives, snipes, lives, level, score)  
✅ **Game over screen** (animated "GAME OVER" banner, player scores with "-< SCORES >-" header, ENTER to return)  
✅ Multiplayer waiting screen with player count and join notifications  

### Game Systems
✅ Map scrolling and wrapping (horizontal and vertical)  
✅ Game state tracking (level, score, counts)  
✅ **Level system with automatic progression** (level completion detection, level advancement, level-based hive/snipe counts)  
✅ **Level start screen** (animated clearing with level info: "LEVEL n - x HIVES with y SNIPES")  
✅ Scoring system (25 points per snipe, 500 + 25 per unreleased snipe for hives)  
✅ Game reset functionality (fully resets when starting new game after game over)  
✅ **Game over screen** (animated banner, player scores display, ENTER to return)  
✅ Player initials customization  
✅ Configuration persistence (initials saved between sessions)  

### Technical Features
✅ Performance optimizations (separate timers, viewport culling, caching)  
✅ Efficient rendering (HashSet-based position tracking for snipes)  
✅ Smooth animations (player eyes, hive colors, clearing effects)  
✅ Code organization (IntroScreen class separated from Game class)  
✅ gRPC networking infrastructure (GrpcGameClient, GameSession classes, gRPC server)  
✅ Network message serialization (Protocol buffer messages for all game events)  
✅ World coordinate system (all positions in map space, viewport conversion local)  

## Multiplayer Features

### ✅ Implemented

**Game Discovery and Joining**
- Host can start a multiplayer game (1-5 players)
- **Single Player Mode**: If host selects 1 player, game starts immediately without network (local play only, no network overhead)
- **Multiplayer Mode**: If host selects 2-5 players, uses gRPC networking with 60-second join window
- 6-character alphanumeric game ID for easy sharing (only used for 2+ player games)
- Real-time player count updates ("X of Y players joined")
- Player join notifications ("[Initials] joined!")
- Game automatically starts after join window expires or max players reached

**Network Architecture**
- **gRPC-based networking** using dedicated server (default: `localhost:5000`)
- **Server Management**: gRPC server manages game rooms, player connections, and message routing
- **Bidirectional Streaming**: Real-time game messages flow through persistent HTTP/2 connections
- **Protocol Buffers**: Efficient binary serialization for all game messages
- Host-client architecture (host is authoritative for game state)
- Real-time position synchronization (20ms update rate)
- Bullet synchronization across all players
- Game state synchronization (hives, snipes, player positions)

**Player Synchronization**
- All players see each other's movement in real-time
- Remote players displayed in yellow (local player in white/blue)
- Player initials synchronized across all clients
- Player respawn positions synchronized
- Player-to-player collision detection (players can't overlap)

**Game State Synchronization**
- Hive positions synchronized (all players see same hives)
- Snipe positions synchronized (host controls snipe movement, clients receive updates)
- Bullet positions synchronized (all players can shoot, host validates collisions)
- Game state snapshots on game start (ensures all players start with same state)

**Technical Implementation**
- **gRPC Server**: Dedicated server (`NSnipes.GrpcServer`) manages all multiplayer connections
- **Protocol Buffers**: Type-safe, efficient binary serialization for all game messages
- **Bidirectional Streaming**: Single persistent connection per player for all game messages
- **HTTP/2**: Modern protocol with multiplexing and header compression
- World coordinate system (all positions in map space, converted to viewport locally)
- Proper viewport position tracking for artifact-free rendering
- Network latency handling (latest position updates, not every intermediate step)
- Sequence numbers for ordered position updates
- Fire-and-forget messaging for low-latency position updates

### ⚠️ Known Issues / Limitations

- **Bullet Synchronization**: Bullets in multiplayer are not fully synchronized - needs refinement
- **Terminal.Gui Library Update**: Need to update to latest develop branch
  - May require significant rework as direct terminal driver access has been deprecated
  - Will need to figure out alternative approach for low-level terminal operations
- **Global ESC Key Handling**: Need to sort out global [ESC] key behavior across all screens
- **Level Progression**: Level progression in multiplayer is host-only (clients receive level updates but don't trigger progression)
- **Full Game State Sync**: Full game state synchronization (scores, lives) still being refined
- **Testing**: Extensive testing needed for multiplayer stability and edge cases

## Not Yet Implemented

❌ High score system  
❌ Option to restart multiplayer game with same players  
❌ Level progression synchronization in multiplayer (currently host-only)  
❌ Power-ups or special abilities  
❌ Different bullet types  
❌ Boss hives or special enemies  
❌ Sound effects  
❌ Pause functionality  

## Project Dependencies

This project is built with the following dependencies:
- https://github.com/gui-cs/Terminal.Gui (v2.0.0-prealpha.1895)
- **Grpc.Net.Client** (v2.62.0) - gRPC client for .NET
- **Grpc.AspNetCore** (v2.62.0) - gRPC server for ASP.NET Core
- **Google.Protobuf** (v3.25.3) - Protocol buffer runtime
- **Grpc.Tools** (v2.62.0) - Protocol buffer compiler tools

## Map Generation

I used the following https://stackoverflow.com/questions/56918471/how-can-i-increase-corridor-width-in-a-maze Python code to generate the maze at an appropriate scale.  I captured the output produced by the following Python code, and used a Text editor to change lines, make the maze wrap around and break through some walls to simplify.

```
import random


def make_maze(w = 16, h = 8, scale=0):

    h0, h1, h2, h3 = "+--", "+  ", "|  ", "   "
    h0 += scale * '----'
    h1 += scale * '    '
    h2 += scale * '    '
    h3 += scale * '    '
    vis = [[0] * w + [1] for _ in range(h)] + [[1] * (w + 1)]
    ver = [[h2] * w + ['|'] for _ in range(h)] + [[]]
    hor = [[h0] * w + ['+'] for _ in range(h + 1)]

    def walk(x, y):
        vis[y][x] = 1

        d = [(x - 1, y), (x, y + 1), (x + 1, y), (x, y - 1)]
        random.shuffle(d)
        for (xx, yy) in d:
            if vis[yy][xx]: continue
            if xx == x: hor[max(y, yy)][x] = h1
            if yy == y: ver[y][max(x, xx)] = h3
            walk(xx, yy)

    walk(random.randrange(w), random.randrange(h))

    s = ""
    for (a, b) in zip(hor, ver):
        s += ''.join(a + ['\n'] + b + ['\n'])
        for _ in range(scale):
            s += ''.join(b + ['\n'])
    return s



print(make_maze(scale=0))
print('\n\n')
print(make_maze(scale=1))
print('\n\n')
print(make_maze(scale=2))
print('\n\n')
print(make_maze(scale=3))
print('\n\n')
```
Full Credit for this amazing scaleable Maze generator goes to https://stackoverflow.com/users/2875563/reblochon-masque

## Resources

So resources that I took a look at:
https://en.wikipedia.org/wiki/Snipes_(video_game)

https://www.youtube.com/watch?v=IXsJhoW0C78
https://www.youtube.com/watch?v=1iGKsuZlIIo
https://www.youtube.com/watch?v=85IcFHTsVQs

https://www.networkworld.com/article/830595/infrastructure-management-novell-and-the-computer-game-that-changed-networking.html

https://medium.com/venture-evolved/snipes-the-game-that-gave-birth-to-lans-e9dc169873e4

https://playclassic.games/games/arcade-dos-games-online/play-snipes-online/play/

https://www.giantbomb.com/snipes/3030-12025/

https://github.com/Davidebyzero/Snipes
