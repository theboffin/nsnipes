# MQTT Multiplayer Implementation Plan for NSnipes

## Overview

This document outlines the implementation plan for adding multiplayer functionality to NSnipes using MQTT. The game will support 1-5 players, with one player acting as the host (game authority) and others joining via a 6-character game ID.

## Architecture

### Authority Model

- **Host Player (Game Authority)**: 
  - Controls game state (level, score, lives)
  - Controls hives (spawning, destruction, flash rates)
  - Controls snipes (spawning, movement, AI decisions)
  - Publishes authoritative game state updates
  - Validates and processes player actions

- **All Players (Client Authority)**:
  - Each player is authoritative for their own position
  - Each player is authoritative for their own bullets
  - Players publish their own movements and bullet firings
  - Players receive and render other players' positions and bullets

### MQTT Broker

**Recommended**: HiveMQ Cloud (free tier) or Mosquitto public broker
- **Broker URL**: `broker.hivemq.com` (public, no auth) or `test.mosquitto.org`
- **Port**: 1883 (non-TLS) or 8883 (TLS)
- **QoS**: Use QoS 1 (at least once delivery) for all game-critical messages

## MQTT Topic Structure

### Game Discovery Topics

```
nsnipes/games/active          # List of active games waiting for players
nsnipes/games/{GAME_ID}/info  # Game information (player count, status)
```

### Game Session Topics

```
nsnipes/game/{GAME_ID}/join           # Join requests
nsnipes/game/{GAME_ID}/leave          # Leave notifications
nsnipes/game/{GAME_ID}/start          # Game start notification
nsnipes/game/{GAME_ID}/end            # Game end notification
nsnipes/game/{GAME_ID}/players/joined # Player join notifications
nsnipes/game/{GAME_ID}/players/count  # Player count updates during join wait
```

### Game State Topics (Host publishes)

```
nsnipes/game/{GAME_ID}/state      # Full game state snapshot (periodic)
nsnipes/game/{GAME_ID}/hives      # Hive updates (spawn, hit, destroy)
nsnipes/game/{GAME_ID}/snipes     # Snipe updates (spawn, move, die)
nsnipes/game/{GAME_ID}/gameover   # Game over with final scores
```

### Player Action Topics (All players publish)

```
nsnipes/game/{GAME_ID}/player/{PLAYER_ID}/position  # Player position updates
nsnipes/game/{GAME_ID}/player/{PLAYER_ID}/bullet    # Bullet fired/updated
nsnipes/game/{GAME_ID}/player/{PLAYER_ID}/action   # Other actions (respawn, etc.)
```

### Player State Topics (All players subscribe)

```
nsnipes/game/{GAME_ID}/players/+   # Subscribe to all player updates (wildcard)
```

## Message Formats (JSON)

### Game Discovery

**Topic**: `nsnipes/games/active`
**Publisher**: Host
**Message**:
```json
{
  "gameId": "ABC123",
  "hostPlayerId": "player1",
  "hostInitials": "BD",
  "maxPlayers": 5,
  "currentPlayers": 1,
  "status": "waiting",  // "waiting", "playing", "ended"
  "level": 1,
  "createdAt": "2024-01-15T10:30:00Z"
}
```

### Join Request

**Topic**: `nsnipes/game/{GAME_ID}/join`
**Publisher**: Joining player
**Message**:
```json
{
  "playerId": "player2",
  "initials": "AB",
  "timestamp": "2024-01-15T10:30:05Z"
}
```

### Join Response

**Topic**: `nsnipes/game/{GAME_ID}/join`
**Publisher**: Host
**Message** (sent as retained message):
```json
{
  "accepted": true,
  "playerId": "player2",
  "playerNumber": 2,
  "gameState": { /* full game state */ }
}
```

### Player Join Notification

**Topic**: `nsnipes/game/{GAME_ID}/players/joined`
**Publisher**: Host
**Message** (published when each player joins):
```json
{
  "playerId": "player2",
  "initials": "AB",
  "playerNumber": 2,
  "currentPlayers": 2,
  "maxPlayers": 5,
  "timestamp": "2024-01-15T10:30:05Z"
}
```

### Player Count Update

**Topic**: `nsnipes/game/{GAME_ID}/players/count`
**Publisher**: Host
**Message** (published periodically during join wait period):
```json
{
  "currentPlayers": 2,
  "maxPlayers": 5,
  "players": [
    {
      "playerId": "player1",
      "initials": "BD",
      "playerNumber": 1
    },
    {
      "playerId": "player2",
      "initials": "AB",
      "playerNumber": 2
    }
  ],
  "timeRemaining": 45,  // seconds remaining in join window
  "timestamp": "2024-01-15T10:30:10Z"
}
```

### Player Position Update

**Topic**: `nsnipes/game/{GAME_ID}/player/{PLAYER_ID}/position`
**Publisher**: Each player (their own position)
**Frequency**: Every 40ms (when player moves)
**Message**:
```json
{
  "playerId": "player1",
  "x": 150,
  "y": 200,
  "timestamp": "2024-01-15T10:30:10.123Z",
  "sequence": 12345  // Incremental sequence number for ordering
}
```

### Bullet Fired

**Topic**: `nsnipes/game/{GAME_ID}/player/{PLAYER_ID}/bullet`
**Publisher**: Player who fired
**Message**:
```json
{
  "bulletId": "bullet_1234567890",
  "playerId": "player1",
  "startX": 150.5,
  "startY": 200.0,
  "velocityX": 1.0,
  "velocityY": -1.0,
  "createdAt": "2024-01-15T10:30:10.500Z",
  "action": "fired"  // "fired", "updated", "expired", "hit"
}
```

### Bullet Update (for fast-moving bullets)

**Topic**: `nsnipes/game/{GAME_ID}/player/{PLAYER_ID}/bullet`
**Publisher**: Player who fired (or host if bullet hits something)
**Frequency**: Every 10ms (bullet update rate)
**Message**:
```json
{
  "bulletId": "bullet_1234567890",
  "playerId": "player1",
  "x": 151.5,
  "y": 199.0,
  "velocityX": 1.0,
  "velocityY": -1.0,
  "timestamp": "2024-01-15T10:30:10.510Z",
  "action": "updated"
}
```

### Bullet Hit/Expired

**Topic**: `nsnipes/game/{GAME_ID}/player/{PLAYER_ID}/bullet`
**Publisher**: Host (validates hits) or Player (expired bullets)
**Message**:
```json
{
  "bulletId": "bullet_1234567890",
  "playerId": "player1",
  "action": "hit",  // or "expired"
  "hitType": "snipe",  // "snipe", "hive", "player", "wall"
  "hitTargetId": "snipe_456",  // ID of hit target (if applicable)
  "timestamp": "2024-01-15T10:30:10.520Z"
}
```

### Game State Snapshot (Host publishes)

**Topic**: `nsnipes/game/{GAME_ID}/state`
**Publisher**: Host
**Frequency**: Every 200ms (5 times per second) - full state
**Message**:
```json
{
  "gameId": "ABC123",
  "level": 1,
  "status": "playing",
  "players": [
    {
      "playerId": "player1",
      "initials": "BD",
      "x": 150,
      "y": 200,
      "lives": 5,
      "score": 250,
      "isAlive": true
    },
    {
      "playerId": "player2",
      "initials": "AB",
      "x": 300,
      "y": 400,
      "lives": 5,
      "score": 0,
      "isAlive": true
    }
  ],
  "hives": [
    {
      "hiveId": "hive_1",
      "x": 100,
      "y": 150,
      "hits": 0,
      "isDestroyed": false,
      "snipesRemaining": 20,
      "flashIntervalMs": 75
    }
  ],
  "snipes": [
    {
      "snipeId": "snipe_1",
      "x": 120,
      "y": 160,
      "type": "A",
      "directionX": 1,
      "directionY": 0,
      "isAlive": true
    }
  ],
  "timestamp": "2024-01-15T10:30:10.200Z",
  "sequence": 100
}
```

### Snipe Updates (Host publishes)

**Topic**: `nsnipes/game/{GAME_ID}/snipes`
**Publisher**: Host
**Frequency**: Every 200ms (snipe movement rate)
**Message**:
```json
{
  "updates": [
    {
      "snipeId": "snipe_1",
      "action": "moved",  // "spawned", "moved", "died"
      "x": 121,
      "y": 160,
      "directionX": 1,
      "directionY": 0,
      "timestamp": "2024-01-15T10:30:10.200Z"
    },
    {
      "snipeId": "snipe_2",
      "action": "spawned",
      "x": 101,
      "y": 151,
      "type": "A",
      "directionX": 0,
      "directionY": 1,
      "timestamp": "2024-01-15T10:30:10.200Z"
    }
  ]
}
```

### Hive Updates (Host publishes)

**Topic**: `nsnipes/game/{GAME_ID}/hives`
**Publisher**: Host
**Frequency**: On change (spawn, hit, destroy)
**Message**:
```json
{
  "updates": [
    {
      "hiveId": "hive_1",
      "action": "hit",  // "spawned", "hit", "destroyed"
      "hits": 1,
      "flashIntervalMs": 50,
      "timestamp": "2024-01-15T10:30:10.500Z"
    }
  ]
}
```

### Game Start

**Topic**: `nsnipes/game/{GAME_ID}/start`
**Publisher**: Host
**Message**:
```json
{
  "gameId": "ABC123",
  "level": 1,
  "players": ["player1", "player2", "player3"],
  "startTime": "2024-01-15T10:30:15.000Z"
}
```

### Game Over

**Topic**: `nsnipes/game/{GAME_ID}/gameover`
**Publisher**: Host
**Message**:
```json
{
  "gameId": "ABC123",
  "finalScores": [
    {
      "playerId": "player1",
      "initials": "BD",
      "score": 1250,
      "rank": 1
    },
    {
      "playerId": "player2",
      "initials": "AB",
      "score": 500,
      "rank": 2
    }
  ],
  "endTime": "2024-01-15T10:35:00.000Z"
}
```

## Implementation Phases

### Phase 1: Game Discovery and Joining

**Files to Create/Modify**:
- `NSnipes/MqttGameClient.cs` - MQTT client wrapper
- `NSnipes/GameSession.cs` - Game session management
- `NSnipes/PlayerNetwork.cs` - Network player representation
- `NSnipes/IntroScreen.cs` - Add multiplayer menu options

**Functionality**:
1. **Start New Game**:
   - Prompt for number of players (1-5, default 1)
   - Generate 6-character game ID (alphanumeric)
   - Create MQTT connection as host
   - Publish game to `nsnipes/games/active`
   - Wait up to 60 seconds for players to join
   - **Host UI During Join Wait**:
     - Display game ID prominently (for sharing with other players)
     - Show countdown timer (60 seconds remaining)
     - Display "Waiting for players..." message
     - Show current player count: "X of Y players joined"
     - When a player joins, display notification: "[Player Initials] joined!" (e.g., "AB joined!")
     - Update player count in real-time as players join
     - List all joined players with their initials
   - Subscribe to `nsnipes/game/{GAME_ID}/players/joined` to receive join notifications
   - Publish player count updates to `nsnipes/game/{GAME_ID}/players/count` every second
   - Start game when max players reached or 60 seconds elapsed
   - **Initial Player Spawning**: When game starts, host spawns all players at random valid positions
     - Each player position must not overlap with any other player (check all 6 cells: 2x3)
     - Use same validation logic as respawn (see Phase 5)

2. **Join Existing Game**:
   - Prompt for 6-character game ID
   - Connect to MQTT broker
   - Subscribe to `nsnipes/games/{GAME_ID}/info`
   - Publish join request to `nsnipes/game/{GAME_ID}/join`
   - Wait for host acceptance
   - Receive initial game state
   - **Joining Player UI During Wait**:
     - Display "Waiting for game to start..." message
     - Show current player count: "X of Y players joined"
     - Subscribe to `nsnipes/game/{GAME_ID}/players/joined` to see join notifications
     - Subscribe to `nsnipes/game/{GAME_ID}/players/count` for periodic updates
     - When another player joins, display notification: "[Player Initials] joined!" (e.g., "CD joined!")
     - Show countdown timer if available from host updates
     - Display list of all joined players with their initials
   - Wait for game start notification

### Phase 2: Player Synchronization

**Files to Modify**:
- `NSnipes/Game.cs` - Add network player tracking
- `NSnipes/Player.cs` - Add network properties (playerId, isLocal, etc.)

**Functionality**:
1. Track remote players separately from local player
2. Publish local player position every 40ms (when moved)
3. Subscribe to all player position updates
4. Render remote players on screen (different color/indicator)
5. Handle player-to-player collision:
   - Check collision when processing movement
   - Block movement if another player occupies target position
   - No death on collision, just blocking

### Phase 3: Bullet Synchronization

**Files to Modify**:
- `NSnipes/Bullet.cs` - Add bulletId, playerId properties
- `NSnipes/Game.cs` - Track bullets from all players

**Functionality**:
1. **Bullet Firing**:
   - When local player fires, publish bullet creation
   - Include bulletId (timestamp-based), position, velocity
   - Track locally and remotely

2. **Bullet Updates**:
   - Host is authoritative for bullet collisions (hits snipes, hives, players)
   - Each player publishes their own bullet position updates (every 10ms)
   - All players subscribe to all bullet updates
   - Render all bullets (local and remote) on screen

3. **Bullet Collisions**:
   - Host detects collisions (bullet vs snipe, hive, player, wall)
   - Host publishes collision events
   - All players remove bullet and update game state
   - Host updates scores, lives, etc.

### Phase 4: Game State Synchronization

**Files to Modify**:
- `NSnipes/Game.cs` - Host/Client mode logic
- `NSnipes/GameState.cs` - Network serialization

**Functionality**:
1. **Host Responsibilities**:
   - Control hives (spawning, hits, destruction)
   - Control snipes (spawning, movement, AI)
   - Publish game state snapshots every 200ms
   - Validate and process all player actions
   - Detect game over conditions

2. **Client Responsibilities**:
   - Subscribe to game state updates
   - Apply authoritative state from host
   - Render all game entities (hives, snipes, players, bullets)
   - Publish own actions (movement, bullets)

3. **State Reconciliation**:
   - Clients apply host state for hives/snipes
   - Clients maintain own position/bullets (with host validation)
   - Handle conflicts (host is authoritative)

### Phase 5: Collision Detection Enhancements

**Files to Modify**:
- `NSnipes/Game.cs` - Add player-to-player collision
- `NSnipes/Game.cs` - Add bullet-to-player collision

**Functionality**:
1. **Player-to-Player Collision**:
   - Check all 6 cells (2x3) of player against all other players
   - Block movement if collision detected
   - No death, just prevent overlap
   - Works for both local and remote players

2. **Bullet-to-Player Collision**:
   - Host detects when bullet hits a player
   - Player loses a life
   - Player respawns at random position (see Player Spawn Validation below)
   - Host publishes collision event
   - All players update their displays

3. **Player Spawn Validation**:
   - When a player spawns (initial spawn or after losing a life):
     - Host finds a random valid position (not on walls, not on hives)
     - **Must check that spawn position does not overlap with any other player**
     - Check all 6 cells (2x3) of spawn position against all other players' positions
     - If overlap detected, try another random position (up to 100 attempts)
     - If no valid position found after attempts, use fallback systematic search
     - Host publishes spawn position to all players
   - Applies to both initial game start and respawn after death
   - Ensures players never spawn on top of each other

3. **Bullet-to-Bullet Collision** (Optional):
   - Detect when bullets collide
   - Both bullets destroyed
   - Host publishes collision event

## Technical Details

### Player ID Generation

```csharp
// Generate unique player ID
string playerId = $"player_{DateTime.UtcNow.Ticks}_{Guid.NewGuid().ToString().Substring(0, 8)}";
```

### Game ID Generation

```csharp
// Generate 6-character alphanumeric game ID
private string GenerateGameId()
{
    const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    var random = new Random();
    return new string(Enumerable.Repeat(chars, 6)
        .Select(s => s[random.Next(s.Length)]).ToArray());
}
```

### Message Ordering

- Use sequence numbers for position updates
- Use timestamps for all messages
- Host can reorder/reconcile out-of-order messages
- Clients should buffer and apply messages in order

### Network Latency Handling

1. **Client-Side Prediction**:
   - Players render their own actions immediately
   - Apply host corrections when received
   - Smooth interpolation for remote players

2. **Lag Compensation**:
   - Host uses timestamp-based collision detection
   - Account for network delay when validating hits
   - Buffer recent positions for hit validation

3. **Interpolation**:
   - Smooth movement of remote players between updates
   - Extrapolate position for missing updates
   - Snap to authoritative position when received

### Error Handling

1. **Connection Loss**:
   - Auto-reconnect to MQTT broker
   - Re-subscribe to all topics
   - Request game state from host

2. **Host Disconnection**:
   - Detect host absence (no state updates for 5 seconds)
   - Show "Host disconnected" message
   - Return to menu

3. **Message Validation**:
   - Validate all received messages
   - Ignore invalid/malformed messages
   - Log errors for debugging

## Dependencies

### NuGet Packages

```xml
<PackageReference Include="MQTTnet" Version="4.3.3.952" />
```

### MQTTnet Usage

```csharp
using MQTTnet;
using MQTTnet.Client;

var mqttFactory = new MqttFactory();
var mqttClient = mqttFactory.CreateMqttClient();

var options = new MqttClientOptionsBuilder()
    .WithTcpServer("broker.hivemq.com", 1883)
    .Build();

await mqttClient.ConnectAsync(options);
```

## Testing Strategy

1. **Local Testing**:
   - Run multiple instances on same machine
   - Use local MQTT broker (Mosquitto)
   - Test with 2-5 players

2. **Network Testing**:
   - Test across different networks
   - Measure latency and jitter
   - Test connection loss scenarios

3. **Stress Testing**:
   - Test with maximum players (5)
   - Test with many bullets active
   - Test with many snipes active

## Future Enhancements

1. **Private Games**:
   - Password protection
   - Friend-only games

2. **Spectator Mode**:
   - Allow observers to watch games
   - Read-only subscriptions

3. **Replay System**:
   - Record game state messages
   - Playback for review

4. **Leaderboards**:
   - Publish scores to leaderboard topic
   - Global high scores

## Implementation Checklist

- [ ] Phase 1: Game Discovery and Joining
  - [ ] MQTT client wrapper
  - [ ] Game ID generation
  - [ ] Start new game flow
  - [ ] Join existing game flow
  - [ ] 60-second join window
  - [ ] Player count management (1-5)
  - [ ] Host UI: Display game ID, countdown, player count, join notifications
  - [ ] Joining player UI: Display player count, join notifications, waiting message
  - [ ] Player join notification messages ("XX joined!")
  - [ ] Player count update messages (publish/subscribe)

- [ ] Phase 2: Player Synchronization
  - [ ] Network player representation
  - [ ] Position publishing (40ms)
  - [ ] Position subscription
  - [ ] Remote player rendering
  - [ ] Player-to-player collision
  - [ ] Initial player spawn validation (no overlap with other players)

- [ ] Phase 3: Bullet Synchronization
  - [ ] Bullet ID and ownership
  - [ ] Bullet firing publishing
  - [ ] Bullet update publishing (10ms)
  - [ ] Bullet subscription
  - [ ] Remote bullet rendering
  - [ ] Host bullet collision detection

- [ ] Phase 4: Game State Synchronization
  - [ ] Host/Client mode separation
  - [ ] Host game state publishing (200ms)
  - [ ] Client state subscription
  - [ ] Snipe synchronization
  - [ ] Hive synchronization
  - [ ] Score and lives synchronization

- [ ] Phase 5: Collision Detection
  - [ ] Player-to-player collision
  - [ ] Bullet-to-player collision
  - [ ] Player spawn validation (no overlap with other players on initial spawn or respawn)
  - [ ] Bullet-to-bullet collision (optional)

- [ ] Error Handling
  - [ ] Connection loss recovery
  - [ ] Host disconnection handling
  - [ ] Message validation

- [ ] Testing
  - [ ] Local multi-instance testing
  - [ ] Network testing
  - [ ] Stress testing

