namespace NSnipes;

public class Bullet(double startX, double startY, double velocityX, double velocityY, string? bulletId = null, string? playerId = null, DateTime? createdAt = null)
{
    private static long _bulletCounter = 0;
    private static readonly object _bulletCounterLock = new object();

    private static string GenerateBulletId(string? playerId)
    {
        long counter;
        lock (_bulletCounterLock) { counter = ++_bulletCounter; }
        string playerPrefix = string.IsNullOrWhiteSpace(playerId) ? "local" : playerId;
        return $"bullet_{playerPrefix}_{DateTime.UtcNow.Ticks}_{counter}_{Guid.NewGuid().ToString("N")[..8]}";
    }

    public string BulletId { get; set; } = bulletId ?? GenerateBulletId(playerId);
    public string PlayerId { get; set; } = playerId ?? "";
    public double X { get; set; } = startX;
    public double Y { get; set; } = startY;
    public double PreviousX { get; set; } = startX;
    public double PreviousY { get; set; } = startY;
    public double VelocityX { get; set; } = velocityX;
    public double VelocityY { get; set; } = velocityY;
    public DateTime CreatedAt { get; set; } = createdAt ?? DateTime.Now;
    public const double LifetimeSeconds = 2.0;

    public void Update()
    {
        PreviousX = X;
        PreviousY = Y;
        X += VelocityX;
        Y += VelocityY;
    }

    public void BounceX() => VelocityX = -VelocityX;
    public void BounceY() => VelocityY = -VelocityY;
}
