namespace NSnipes;

public class Bullet(double startX, double startY, double velocityX, double velocityY, string? bulletId = null, string? playerId = null, DateTime? createdAt = null)
{
    public string BulletId { get; set; } = bulletId ?? $"bullet_{DateTime.UtcNow.Ticks}_{Guid.NewGuid().ToString()[..8]}"; // Unique bullet ID for network sync
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

