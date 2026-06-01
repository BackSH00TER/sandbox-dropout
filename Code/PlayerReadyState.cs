using Sandbox;

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
