namespace NSnipes;

public class Bullet(double startX, double startY, double velocityX, double velocityY, string? bulletId = null, string? playerId = null, DateTime? createdAt = null)
{
    // Static counter for bullet ID generation (thread-safe)
    private static long _bulletCounter = 0;
    private static readonly object _bulletCounterLock = new object();
    
    /// <summary>
    /// Generates a unique bullet ID combining player ID, timestamp, counter, and GUID
    /// Format: bullet_{playerId}_{timestamp}_{counter}_{guid}
    /// </summary>
    private static string GenerateBulletId(string? playerId)
    {
        long counter;
        lock (_bulletCounterLock)
        {
            counter = ++_bulletCounter;
        }
        
        string playerPrefix = string.IsNullOrWhiteSpace(playerId) ? "local" : playerId;
        string timestamp = DateTime.UtcNow.Ticks.ToString();
        string guid = Guid.NewGuid().ToString("N")[..8]; // 8 hex characters
        
        return $"bullet_{playerPrefix}_{timestamp}_{counter}_{guid}";
    }
    
    public string BulletId { get; set; } = bulletId ?? GenerateBulletId(playerId); // Unique bullet ID for network sync
    public string PlayerId { get; set; } = playerId ?? ""; // Player who fired this bullet
    public double X { get; set; } = startX;  // Using double for smooth movement
    public double Y { get; set; } = startY;
    public double PreviousX { get; set; } = startX;  // Previous position for clearing
    public double PreviousY { get; set; } = startY;
    public double VelocityX { get; set; } = velocityX;
    public double VelocityY { get; set; } = velocityY;
    public DateTime CreatedAt { get; set; } = createdAt ?? DateTime.Now;
    public const double LifetimeSeconds = 2.0; // Bullets expire after 2 seconds

    public void Update()
    {
        // Store previous position before updating
        PreviousX = X;
        PreviousY = Y;
        
        // Update position
        X += VelocityX;
        Y += VelocityY;
    }

    public void BounceX()
    {
        VelocityX = -VelocityX;
    }

    public void BounceY()
    {
        VelocityY = -VelocityY;
    }
}
