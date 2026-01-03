namespace NSnipes;

public class Bullet
{
    public string BulletId { get; set; } = ""; // Unique bullet ID for network sync
    public string PlayerId { get; set; } = ""; // Player who fired this bullet
    public double X { get; set; }  // Using double for smooth movement
    public double Y { get; set; }
    public double PreviousX { get; set; }  // Previous position for clearing
    public double PreviousY { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public DateTime CreatedAt { get; set; }
    public const double LifetimeSeconds = 2.0; // Bullets expire after 2 seconds

    public Bullet(double startX, double startY, double velocityX, double velocityY, string? bulletId = null, string? playerId = null)
    {
        BulletId = bulletId ?? $"bullet_{DateTime.UtcNow.Ticks}_{Guid.NewGuid().ToString().Substring(0, 8)}";
        PlayerId = playerId ?? "";
        X = startX;
        Y = startY;
        PreviousX = startX;
        PreviousY = startY;
        VelocityX = velocityX;
        VelocityY = velocityY;
        CreatedAt = DateTime.Now;
    }

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

