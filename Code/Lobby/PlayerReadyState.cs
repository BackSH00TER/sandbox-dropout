using Sandbox;

/// <summary>
/// Per-player lobby ready state. Holds the networked <see cref="IsReady"/>
/// bool that <see cref="LobbyReadyUp"/> flips when the player enters/exits
/// the ready zone, and drives the player's overhead indicator color. 
/// Also exposed to the lobby board UI, which reads <see cref="DisplayName"/> 
/// and <see cref="IsReady"/> to render each connected player's ready state.
/// </summary>
public sealed class PlayerReadyState : Component
{
    [Property] public ModelRenderer Circle { get; set; }

    [Sync, Change( nameof( OnIsReadyChanged ) )]
    public bool IsReady { get; set; }

    public string DisplayName => Network.Owner?.DisplayName ?? "Unknown";

    protected override void OnStart()
    {
        ApplyTint( IsReady );
    }

    private void OnIsReadyChanged( bool oldValue, bool newValue )
    {
        ApplyTint( newValue );
    }

    private void ApplyTint( bool ready )
    {
        if ( Circle is null ) return;
        Circle.Tint = ready ? Color.Green : Color.Red;
    }
}
