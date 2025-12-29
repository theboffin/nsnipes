namespace NSnipes;

public class Bullet
{
    public double X { get; set; }  // Using double for smooth movement
    public double Y { get; set; }
    public double PreviousX { get; set; }  // Previous position for clearing
    public double PreviousY { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public DateTime CreatedAt { get; set; }
    public const double LifetimeSeconds = 2.0; // Bullets expire after 2 seconds

    public Bullet(double startX, double startY, double velocityX, double velocityY)
    {
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

