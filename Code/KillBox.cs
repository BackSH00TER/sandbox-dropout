public sealed class KillBox : Component, Component.ITriggerListener
{
    [Property] public GameManager GameManager { get; set; }

    public void OnTriggerEnter( Collider other )
    {
        if ( !Networking.IsHost ) return;
        if ( !other.GameObject.Tags.Has( "player" ) ) return;

        var player = other.GameObject.Root.GetComponent<PlayerController>();
        if ( player == null ) return;

        if ( GameManager.IsValid() )
        {
            GameManager.PlayerEliminated( player );
        }
    }
}
