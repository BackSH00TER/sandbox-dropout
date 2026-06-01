using Sandbox;

/// <summary>
/// Trigger volume placed below the platform. When a player falls into it, the match resets:
/// platform tiles are rebuilt, players respawn, and the countdown timer restarts.
/// </summary>
public sealed class KillBox : Component, Component.ITriggerListener
{
    public void OnTriggerEnter( Collider other )
    {
        if ( !other.Tags.Has( "player" ) ) return;

        // Only the host decides to restart so we don't trigger multiple restarts.
        if ( !Networking.IsHost ) return;

        GameState.Current?.RestartMatch();
    }
}
