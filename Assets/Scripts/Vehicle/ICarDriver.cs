/// <summary>
/// Whatever is deciding what a car should do this frame — a keyboard, an AI, later a replay.
/// </summary>
/// <remarks>
/// <see cref="CarController"/> talks to this and never to a concrete type, so the AI drives a
/// traffic car through exactly the same physics the player drives theirs through. That is worth
/// more than it sounds: traffic that moves by its own rules always ends up feeling like it is on
/// rails next to a car that does not, and every handling fix would have to be made twice.
/// </remarks>
public interface ICarDriver
{
    /// <summary>-1 (full reverse / brake) to 1 (full throttle).</summary>
    float Throttle { get; }

    /// <summary>-1 (left) to 1 (right).</summary>
    float Steer { get; }

    /// <summary>True while the handbrake is held.</summary>
    bool Handbrake { get; }
}
