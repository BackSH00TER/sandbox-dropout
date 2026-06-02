using Sandbox;

public sealed class KillBox : Component, Component.ITriggerListener
{
    [Property] public GameManager GameManager { get; set; }

    public void OnTriggerEnter( Collider other )
    {
        if ( !Networking.IsHost ) return;
        if ( !other.GameObject.Tags.Has( "player" ) ) return;

        if ( GameManager.IsValid() )
        {
            GameManager.PlayerEliminated();
        }
    }
}
