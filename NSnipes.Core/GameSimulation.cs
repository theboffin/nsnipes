namespace NSnipes;

/// <summary>
/// Pure game simulation: map, entities, tick logic. No UI or I/O.
/// Used by single-player (NSnipes runs it locally) and multiplayer (server runs it).
/// </summary>
public class GameSimulation
{
    public const int MaxBullets = 10;
    public const int MaxHives = 15;
    public const int MaxSnipes = 100;
    public const double BulletSpeed = 1.0;
    public const int SnipeKillScore = 25;
    public const int PlayerKillScore = 1000;
    public const int HiveBaseScore = 500;
    public const int SnipePerHiveScore = 25;
    public const int HiveSpawnRandomizationMs = 1000;
    public const int MaxHeatRadius = 20;

    private readonly Map _map = new Map();
    private readonly GameState _gameState = new GameState();
    private readonly List<Player> _players = [];
    private readonly List<Hive> _hives = [];
    private readonly List<Snipe> _snipes = [];
    private readonly List<Bullet> _bullets = [];
    private DateTime _currentTickTime = DateTime.Now;
    private readonly Dictionary<string, DateTime> _lastFireTime = new Dictionary<string, DateTime>();
    private const int FireRateMs = 150;

    public string Status { get; private set; } = "waiting"; // "waiting" | "playing" | "ended"
    public IReadOnlyList<Player> Players => _players;
    public IReadOnlyList<Hive> Hives => _hives;
    public IReadOnlyList<Snipe> Snipes => _snipes;
    public IReadOnlyList<Bullet> Bullets => _bullets;
    public GameState State => _gameState;
    public Map Map => _map;

    public void StartGame(int level, IReadOnlyList<(string PlayerId, string Initials)> playerInfos)
    {
        Status = "playing";
        _gameState.Level = level;
        _gameState.Score = 0;
        _hives.Clear();
        _snipes.Clear();
        _bullets.Clear();
        _players.Clear();
        _lastFireTime.Clear();

        int hiveCount = _gameState.GetHiveCountForLevel(level);
        int snipesPerHive = _gameState.GetSnipesPerHiveForLevel(level);
        _gameState.TotalHives = hiveCount;
        _gameState.HivesUndestroyed = hiveCount;
        _gameState.TotalSnipes = hiveCount * snipesPerHive;
        _gameState.SnipesUndestroyed = _gameState.TotalSnipes;

        foreach (var (playerId, initials) in playerInfos)
        {
            var (x, y) = FindRandomValidPosition(_players, _hives);
            var p = new Player(x, y) { PlayerId = playerId, Initials = initials };
            p.Lives = Player.InitialLives;
            p.Score = Player.InitialScore;
            p.IsAlive = true;
            _players.Add(p);
        }

        for (int i = 0; i < hiveCount; i++)
        {
            var (x, y) = FindRandomValidHivePosition(_players, _hives);
            _hives.Add(new Hive(x, y, snipesPerHive, _currentTickTime));
        }
    }

    public void ApplyInput(string playerId, int moveDx, int moveDy, int fireDx, int fireDy)
    {
        Player? player = _players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null || !player.IsAlive || Status != "playing") return;

        int newX = _map.WrapX(player.X + moveDx);
        int newY = _map.WrapY(player.Y + moveDy);
        if (IsPositionValidForPlayer(newX, newY, playerId))
        {
            player.X = newX;
            player.Y = newY;
        }

        if ((fireDx != 0 || fireDy != 0) && _bullets.Count < MaxBullets)
        {
            if (_lastFireTime.TryGetValue(playerId, out var last) &&
                (_currentTickTime - last).TotalMilliseconds < FireRateMs)
                return;
            _lastFireTime[playerId] = _currentTickTime;

            double vx = fireDx * BulletSpeed;
            double vy = fireDy * BulletSpeed;
            double startX = player.X + 0.5;
            double startY = player.Y + 1.0;
            _bullets.Add(new Bullet(startX, startY, vx, vy, null, playerId, _currentTickTime));
        }
    }

    public void Tick(DateTime now)
    {
        _currentTickTime = now;
        if (Status != "playing") return;

        UpdateBullets();
        SpawnSnipes();
        UpdateSnipes();
        CheckLevelComplete();
        CheckGameOver();
    }

    private (int x, int y) FindRandomValidPosition(List<Player> excludePlayers, List<Hive> excludeHives)
    {
        const int MAX_ATTEMPTS = 1000;

        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            int x = Random.Shared.Next(0, _map.MapWidth);
            int y = Random.Shared.Next(0, _map.MapHeight);
            if (IsPositionValidForPlayer(x, y, null, excludePlayers, excludeHives))
                return (x, y);
        }

        for (int y = 0; y < _map.MapHeight; y++)
            for (int x = 0; x < _map.MapWidth; x++)
                if (IsPositionValidForPlayer(x, y, null, excludePlayers, excludeHives))
                    return (x, y);
        return (1, 1);
    }

    private bool IsPositionValidForPlayer(int x, int y, string? excludePlayerId,
        List<Player>? excludePlayers = null, List<Hive>? excludeHives = null)
    {
        int pw = Player.Width;
        int ph = Player.Height;
        // Position is already wrapped from ApplyInput; allow any wrapped (x,y) in [0, MapWidth) x [0, MapHeight)
        if (x < 0 || x >= _map.MapWidth || y < 0 || y >= _map.MapHeight)
            return false;

        // Check walkability for each cell the player occupies, using wrapped coordinates (map wraps)
        for (int dy = 0; dy < ph; dy++)
            for (int dx = 0; dx < pw; dx++)
                if (!_map.IsWalkable(_map.WrapX(x + dx), _map.WrapY(y + dy)))
                    return false;

        foreach (var p in excludePlayers ?? _players)
        {
            if (p.PlayerId == excludePlayerId) continue;
            // Overlap in wrapped space: any of our cells equals any of their cells
            for (int dy = 0; dy < ph; dy++)
                for (int dx = 0; dx < pw; dx++)
                {
                    int cx = _map.WrapX(x + dx), cy = _map.WrapY(y + dy);
                    for (int ody = 0; ody < ph; ody++)
                        for (int odx = 0; odx < pw; odx++)
                            if (_map.WrapX(p.X + odx) == cx && _map.WrapY(p.Y + ody) == cy)
                                return false;
                }
        }
        foreach (var h in excludeHives ?? _hives)
        {
            if (h.IsDestroyed) continue;
            for (int dy = 0; dy < ph; dy++)
                for (int dx = 0; dx < pw; dx++)
                {
                    int cx = _map.WrapX(x + dx), cy = _map.WrapY(y + dy);
                    for (int hy = 0; hy <= 1; hy++)
                        for (int hx = 0; hx <= 1; hx++)
                            if (_map.WrapX(h.X + hx) == cx && _map.WrapY(h.Y + hy) == cy)
                                return false;
                }
        }
        return true;
    }

    private (int x, int y) FindRandomValidHivePosition(List<Player> players, List<Hive> hives)
    {
        const int MAX_ATTEMPTS = 1000;
        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            int x = Random.Shared.Next(0, _map.MapWidth - 1);
            int y = Random.Shared.Next(0, _map.MapHeight - 1);
            if (IsHivePositionValid(x, y, players, hives)) return (x, y);
        }
        for (int y = 0; y < _map.MapHeight - 1; y++)
            for (int x = 0; x < _map.MapWidth - 1; x++)
                if (IsHivePositionValid(x, y, players, hives)) return (x, y);
        return (1, 1);
    }

    private bool IsHivePositionValid(int x, int y, List<Player> players, List<Hive> hives)
    {
        if (x < 0 || x + 1 >= _map.MapWidth || y < 0 || y + 1 >= _map.MapHeight) return false;
        for (int row = y; row <= y + 1; row++)
            for (int col = x; col <= x + 1; col++)
                if (!_map.IsWalkable(col, row)) return false;
        foreach (var p in players)
        {
            if (x >= p.X - 1 && x <= p.X + Player.Width && y >= p.Y - 1 && y <= p.Y + Player.Height)
                return false;
        }
        foreach (var h in hives)
        {
            if (x >= h.X - 1 && x <= h.X + 1 && y >= h.Y - 1 && y <= h.Y + 1)
                return false;
        }
        return true;
    }

    /// <summary>Enumerates map cells (wrapped) that the line segment from (x0,y0) to (x1,y1) passes through, to prevent bullet tunneling.</summary>
    private static IEnumerable<(int mapX, int mapY)> GetCellsAlongSegment(Map map, double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0;
        double dy = y1 - y0;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len <= 0) yield break;
        int steps = Math.Max(1, (int)Math.Ceiling(len));
        var seen = new HashSet<(int, int)>();
        for (int s = 0; s <= steps; s++)
        {
            double t = steps == 0 ? 1 : (double)s / steps;
            double x = x0 + t * dx;
            double y = y0 + t * dy;
            int cx = (int)Math.Floor(x);
            int cy = (int)Math.Floor(y);
            int wx = map.WrapX(cx);
            int wy = map.WrapY(cy);
            var key = (wx, wy);
            if (seen.Add(key))
                yield return key;
        }
    }

    private void UpdateBullets()
    {
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            var bullet = _bullets[i];
            if ((_currentTickTime - bullet.CreatedAt).TotalSeconds >= Bullet.LifetimeSeconds)
            {
                _bullets.RemoveAt(i);
                continue;
            }

            double prevX = bullet.X;
            double prevY = bullet.Y;
            bullet.Update();

            // Check every cell the bullet passes through (prevents tunneling); skip start cell so we only bounce when entering a new wall
            int startCellX = _map.WrapX((int)Math.Floor(prevX));
            int startCellY = _map.WrapY((int)Math.Floor(prevY));
            bool hitWall = false;
            char hitCellChar = ' ';
            foreach (var (mapX, mapY) in GetCellsAlongSegment(_map, prevX, prevY, bullet.X, bullet.Y))
            {
                if (mapX == startCellX && mapY == startCellY) continue;
                if (!_map.IsValidCoordinate(mapX, mapY) || _map.IsWalkable(mapX, mapY))
                    continue;
                hitWall = true;
                hitCellChar = _map.FullMap[mapY][mapX];
                break;
            }

            if (hitWall)
            {
                bool isHorizontalWall = hitCellChar == '═' || hitCellChar == '─' || hitCellChar == '╦' || hitCellChar == '╩' || hitCellChar == '╬';
                bool isVerticalWall = hitCellChar == '║' || hitCellChar == '│' || hitCellChar == '╣' || hitCellChar == '╠';
                if (isHorizontalWall) bullet.BounceY();
                else if (isVerticalWall) bullet.BounceX();
                else
                {
                    if (Math.Abs(bullet.VelocityX) > Math.Abs(bullet.VelocityY)) bullet.BounceX();
                    else if (Math.Abs(bullet.VelocityY) > Math.Abs(bullet.VelocityX)) bullet.BounceY();
                    else { bullet.BounceX(); bullet.BounceY(); }
                }
                bullet.X = prevX;
                bullet.Y = prevY;
                // Nudge bullet away from wall so next tick we don't immediately re-enter the same wall (avoids stuck/oscillation in corners)
                bullet.X += bullet.VelocityX * 0.2;
                bullet.Y += bullet.VelocityY * 0.2;
            }

            int bulletMapX = _map.WrapX((int)Math.Round(bullet.X));
            int bulletMapY = _map.WrapY((int)Math.Round(bullet.Y));

            bool removed = false;
            for (int j = _snipes.Count - 1; j >= 0 && !removed; j--)
            {
                var snipe = _snipes[j];
                if (!snipe.IsAlive) continue;
                int sx = _map.WrapX(snipe.X);
                int sy = _map.WrapY(snipe.Y);
                int ax = _map.WrapX(sx + (snipe.DirectionX < 0 ? -1 : 1));
                bool hit = (bulletMapX == sx && bulletMapY == sy) || (bulletMapX == ax && bulletMapY == sy);
                if (hit)
                {
                    snipe.IsAlive = false;
                    _snipes.RemoveAt(j);
                    _gameState.SnipesUndestroyed--;
                    _gameState.Score += SnipeKillScore;
                    var owner = _players.FirstOrDefault(p => p.PlayerId == bullet.PlayerId);
                    if (owner != null) owner.Score += SnipeKillScore;
                    _bullets.RemoveAt(i);
                    removed = true;
                }
            }

            if (!removed)
                foreach (var hive in _hives)
                {
                    if (hive.IsDestroyed) continue;
                    int hx = _map.WrapX(hive.X);
                    int hy = _map.WrapY(hive.Y);
                    int hx2 = _map.WrapX(hive.X + 1);
                    int hy2 = _map.WrapY(hive.Y + 1);
                    bool inHive = (bulletMapX == hx || bulletMapX == hx2) && (bulletMapY == hy || bulletMapY == hy2);
                    if (inHive)
                    {
                        hive.Hits++;
                        hive.FlashIntervalMs = Math.Max(10, (int)(hive.FlashIntervalMs * 2.0 / 3.0));
                        _bullets.RemoveAt(i);
                        removed = true;
                        if (hive.Hits >= Hive.HitsToDestroy)
                        {
                            hive.IsDestroyed = true;
                            _gameState.HivesUndestroyed--;
                            int unreleased = hive.SnipesRemaining;
                            int score = HiveBaseScore + unreleased * SnipePerHiveScore;
                            _gameState.Score += score;
                            _gameState.SnipesUndestroyed -= unreleased;
                            _gameState.TotalSnipes -= unreleased;
                            var owner = _players.FirstOrDefault(p => p.PlayerId == bullet.PlayerId);
                            if (owner != null) owner.Score += score;
                        }
                        break;
                    }
                }

            // Bullet hits another player (PvP): shot player dies, loses a life, respawns if lives left; shooter gets 1000; dead player's bullets vanish
            if (!removed)
            {
                foreach (var player in _players)
                {
                    if (player.PlayerId == bullet.PlayerId) continue; // can't shoot yourself
                    if (!player.IsAlive || player.Lives <= 0) continue;
                    int px = _map.WrapX(player.X);
                    int py = _map.WrapY(player.Y);
                    bool bulletInPlayer = false;
                    for (int dy = 0; dy < Player.Height && !bulletInPlayer; dy++)
                    for (int dx = 0; dx < Player.Width; dx++)
                    {
                        if (_map.WrapX(player.X + dx) == bulletMapX && _map.WrapY(player.Y + dy) == bulletMapY)
                        {
                            bulletInPlayer = true;
                            break;
                        }
                    }
                    if (!bulletInPlayer) continue;

                    // Shot player dies, loses a life
                    player.Lives--;
                    player.IsAlive = player.Lives > 0;
                    if (player.Lives > 0)
                    {
                        var (rx, ry) = FindRandomValidPosition(_players, _hives);
                        player.X = rx;
                        player.Y = ry;
                    }

                    // Shooter gets 1000 points
                    var shooter = _players.FirstOrDefault(p => p.PlayerId == bullet.PlayerId);
                    if (shooter != null) shooter.Score += PlayerKillScore;

                    // Remove the hitting bullet first (so index i is still valid)
                    _bullets.RemoveAt(i);
                    removed = true;

                    // All bullets owned by the shot player vanish
                    string? shotPlayerId = player.PlayerId;
                    for (int k = _bullets.Count - 1; k >= 0; k--)
                    {
                        if (_bullets[k].PlayerId == shotPlayerId)
                            _bullets.RemoveAt(k);
                    }
                    break;
                }
            }
        }
    }

    private void SpawnSnipes()
    {
        foreach (var hive in _hives)
        {
            if (!hive.CanSpawnSnipe()) continue;
            int elapsed = (int)(_currentTickTime - hive.LastSpawnTime).TotalMilliseconds;
            if (elapsed < Hive.SpawnIntervalMs + Random.Shared.Next(-HiveSpawnRandomizationMs, HiveSpawnRandomizationMs))
                continue;

            int sx = hive.X + 1;
            int sy = hive.Y + 1;
            var type = hive.GetNextSnipeType();
            var snipe = new Snipe(sx, sy, type);
            int[] dirs = [-1, 0, 1];
            snipe.DirectionX = dirs[Random.Shared.Next(3)];
            snipe.DirectionY = dirs[Random.Shared.Next(3)];
            if (snipe.DirectionX == 0 && snipe.DirectionY == 0)
                snipe.DirectionX = Random.Shared.Next(2) == 0 ? -1 : 1;
            _snipes.Add(snipe);
            hive.SpawnSnipe(_currentTickTime);
        }
    }

    private Player? GetNearestAlivePlayer(int snipeX, int snipeY)
    {
        Player? nearest = null;
        int bestDist = int.MaxValue;
        foreach (var p in _players)
        {
            if (!p.IsAlive || p.Lives <= 0) continue;
            int dx = p.X - snipeX;
            int dy = p.Y - snipeY;
            _map.WrapDeltaX(ref dx);
            _map.WrapDeltaY(ref dy);
            int d = Math.Abs(dx) + Math.Abs(dy);
            if (d < bestDist) { bestDist = d; nearest = p; }
        }
        return nearest;
    }

    private bool IsSnipePositionValid(int x, int y, int dirX, int dirY)
    {
        int wx = _map.WrapX(x);
        int wy = _map.WrapY(y);
        if (!_map.IsValidCoordinate(wx, wy) || !_map.IsWalkable(wx, wy)) return false;
        int arrowX = _map.WrapX(dirX < 0 ? x - 1 : x + 1);
        if (!_map.IsValidCoordinate(arrowX, wy) || !_map.IsWalkable(arrowX, wy)) return false;
        return true;
    }

    private void UpdateSnipes()
    {
        for (int i = _snipes.Count - 1; i >= 0; i--)
        {
            if (!_snipes[i].IsAlive)
            {
                _snipes.RemoveAt(i);
                _gameState.SnipesUndestroyed--;
                continue;
            }
        }

        var toMove = new List<int>();
        for (int i = 0; i < _snipes.Count; i++)
        {
            if (!_snipes[i].IsAlive) continue;
            if ((_currentTickTime - _snipes[i].LastMoveTime).TotalMilliseconds >= Snipe.MoveIntervalMs)
                toMove.Add(i);
        }

        foreach (int idx in toMove)
        {
            if (idx >= _snipes.Count) continue;
            var snipe = _snipes[idx];
            if (!snipe.IsAlive) continue;

            var target = GetNearestAlivePlayer(snipe.X, snipe.Y);
            int playerX = target?.X ?? snipe.X;
            int playerY = target?.Y ?? snipe.Y;

            int deltaX = playerX - snipe.X;
            int deltaY = playerY - snipe.Y;
            _map.WrapDeltaX(ref deltaX);
            _map.WrapDeltaY(ref deltaY);
            int dist = Math.Abs(deltaX) + Math.Abs(deltaY);
            double heatFactor = Math.Max(0, 1.0 - (dist / (double)MaxHeatRadius));

            int preferredDx = 0, preferredDy = 0;
            if (Math.Abs(deltaX) > Math.Abs(deltaY))
            {
                preferredDx = deltaX > 0 ? 1 : (deltaX < 0 ? -1 : 0);
                preferredDy = preferredDx == 0 && deltaY != 0 ? (deltaY > 0 ? 1 : -1) : 0;
            }
            else
            {
                preferredDy = deltaY > 0 ? 1 : (deltaY < 0 ? -1 : 0);
                preferredDx = preferredDy == 0 && deltaX != 0 ? (deltaX > 0 ? 1 : -1) : 0;
            }

            var possible = new List<(int dx, int dy)>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (IsSnipePositionValid(snipe.X + dx, snipe.Y + dy, dx, dy))
                        possible.Add((dx, dy));
                }

            if (possible.Count == 0) { snipe.LastMoveTime = _currentTickTime; continue; }

            bool curValid = possible.Contains((snipe.DirectionX, snipe.DirectionY));
            int ndx, ndy;
            if (curValid && heatFactor < 0.3)
            {
                if (Random.Shared.Next(100) < 20)
                    (ndx, ndy) = possible[Random.Shared.Next(possible.Count)];
                else
                    (ndx, ndy) = (snipe.DirectionX, snipe.DirectionY);
            }
            else if (heatFactor > 0.3 && possible.Contains((preferredDx, preferredDy)))
                (ndx, ndy) = (preferredDx, preferredDy);
            else
                (ndx, ndy) = possible[Random.Shared.Next(possible.Count)];

            snipe.PreviousX = snipe.X;
            snipe.PreviousY = snipe.Y;
            snipe.PreviousDirectionX = snipe.DirectionX;
            snipe.PreviousDirectionY = snipe.DirectionY;
            snipe.X = _map.WrapX(snipe.X + ndx);
            snipe.Y = _map.WrapY(snipe.Y + ndy);
            snipe.DirectionX = ndx;
            snipe.DirectionY = ndy;
            snipe.LastMoveTime = _currentTickTime;

            for (int j = 0; j < _snipes.Count; j++)
            {
                if (j == idx || !_snipes[j].IsAlive) continue;
                var other = _snipes[j];
                int ox = _map.WrapX(other.X);
                int oy = _map.WrapY(other.Y);
                int sx = _map.WrapX(snipe.X);
                int sy = _map.WrapY(snipe.Y);
                int oax = other.DirectionX < 0 ? _map.WrapX(ox - 1) : _map.WrapX(ox + 1);
                int sax = snipe.DirectionX < 0 ? _map.WrapX(sx - 1) : _map.WrapX(sx + 1);
                bool collide = (sx == ox && sy == oy) || (sx == oax && sy == oy) || (sax == ox && sy == oy) || (sax == oax && sy == oy);
                if (collide)
                {
                    snipe.DirectionX = -snipe.DirectionX;
                    snipe.DirectionY = -snipe.DirectionY;
                    other.DirectionX = -other.DirectionX;
                    other.DirectionY = -other.DirectionY;
                    snipe.X = snipe.PreviousX;
                    snipe.Y = snipe.PreviousY;
                    snipe.X = _map.WrapX(snipe.X);
                    snipe.Y = _map.WrapY(snipe.Y);
                    other.X = other.PreviousX;
                    other.Y = other.PreviousY;
                    other.X = _map.WrapX(other.X);
                    other.Y = _map.WrapY(other.Y);
                    break;
                }
            }

            for (int k = _bullets.Count - 1; k >= 0; k--)
            {
                var bullet = _bullets[k];
                int bx = _map.WrapX((int)Math.Round(bullet.X));
                int by = _map.WrapY((int)Math.Round(bullet.Y));
                int sx = _map.WrapX(snipe.X);
                int sy = _map.WrapY(snipe.Y);
                int sax = snipe.DirectionX < 0 ? _map.WrapX(sx - 1) : _map.WrapX(sx + 1);
                if ((bx == sx && by == sy) || (bx == sax && by == sy))
                {
                    snipe.IsAlive = false;
                    _bullets.RemoveAt(k);
                    _gameState.SnipesUndestroyed--;
                    _gameState.Score += SnipeKillScore;
                    var owner = _players.FirstOrDefault(p => p.PlayerId == bullet.PlayerId);
                    if (owner != null) owner.Score += SnipeKillScore;
                    break;
                }
            }

            foreach (var player in _players)
            {
                if (!player.IsAlive || player.Lives <= 0) continue;
                int px = _map.WrapX(player.X);
                int py = _map.WrapY(player.Y);
                int sx = _map.WrapX(snipe.X);
                int sy = _map.WrapY(snipe.Y);
                if (sx >= px && sx <= px + (Player.Width - 1) && sy >= py && sy <= py + (Player.Height - 1))
                {
                    snipe.IsAlive = false;
                    player.Lives--;
                    player.IsAlive = player.Lives > 0;
                    if (player.Lives > 0)
                    {
                        var (rx, ry) = FindRandomValidPosition(_players, _hives);
                        player.X = rx;
                        player.Y = ry;
                    }
                    break;
                }
            }
        }
    }

    private void CheckLevelComplete()
    {
        if (!_gameState.IsLevelComplete()) return;
        _gameState.Level++;
        int hiveCount = _gameState.GetHiveCountForLevel(_gameState.Level);
        int snipesPerHive = _gameState.GetSnipesPerHiveForLevel(_gameState.Level);
        _gameState.TotalHives = hiveCount;
        _gameState.HivesUndestroyed = hiveCount;
        _gameState.TotalSnipes = hiveCount * snipesPerHive;
        _gameState.SnipesUndestroyed = _gameState.TotalSnipes;

        _hives.Clear();
        _snipes.Clear();
        _bullets.Clear();

        foreach (var p in _players)
        {
            var (x, y) = FindRandomValidPosition(_players, _hives);
            p.X = x;
            p.Y = y;
        }
        for (int i = 0; i < hiveCount; i++)
        {
            var (x, y) = FindRandomValidHivePosition(_players, _hives);
            _hives.Add(new Hive(x, y, snipesPerHive, _currentTickTime));
        }
    }

    private void CheckGameOver()
    {
        bool allDead = _players.All(p => p.Lives <= 0 || !p.IsAlive);
        if (allDead)
            Status = "ended";
    }
}
