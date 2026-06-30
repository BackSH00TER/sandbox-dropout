using Sandbox;

/// <summary>
/// Sits on a <see cref="Sandbox.SpawnPoint"/> GameObject and jitters its position
/// inside <see cref="SpawnAreaSize"/> on each player join, so <see cref="NetworkHelper"/>
/// reads a different world transform every time and players don't pile up on one spot.
/// </summary>
public sealed class LobbyPlayerSpawner : Component, Component.INetworkListener
{
    /// <summary>Full size of the random jitter box (XYZ) applied around the rest position.</summary>
    [Property] public Vector3 SpawnAreaSize { get; set; } = new Vector3( 150f, 150f, 0f );

    /// <summary>Draw the spawn jitter box in the editor so it can be sized to the room.</summary>
    [Property] public bool DrawSpawnGizmo { get; set; } = false;

    private Vector3 _origin;
    private bool _hasOrigin;

    protected override void OnEnabled()
    {
        if ( !_hasOrigin )
        {
            _origin = LocalPosition;
            _hasOrigin = true;
        }
        Log.Info( $"LobbyPlayerSpawner: origin={_origin}, spawnArea={SpawnAreaSize}" );
        RandomizePosition();
    }

    public void OnActive( Connection channel )
    {
        // Re-roll for the next join. Our OnActive may run before or after NetworkHelper's,
        // but since OnEnabled also randomized, the very first spawn is already off-center.
        Log.Info( $"LobbyPlayerSpawner: OnActive for {channel}, re-rolling spawn position." );
        RandomizePosition();
    }

    public void RandomizePosition()
    {

        Vector3 half = SpawnAreaSize * 0.5f;
        LocalPosition = _origin + new Vector3(
            Game.Random.Float( -half.x, half.x ),
            Game.Random.Float( -half.y, half.y ),
            Game.Random.Float( -half.z, half.z )
        );
    }

    protected override void DrawGizmos()
    {
        if ( !DrawSpawnGizmo ) return;
        if ( SpawnAreaSize.IsNearZeroLength ) return;

        // Anchor the gizmo to the rest position so it doesn't follow the GameObject during play.
        Vector3 anchor = _hasOrigin ? _origin - LocalPosition : Vector3.Zero;
        BBox box = BBox.FromPositionAndSize( anchor, SpawnAreaSize );

        Gizmo.Draw.Color = Gizmo.IsSelected ? Color.Cyan : Color.Cyan.WithAlpha( 0.4f );
        Gizmo.Draw.LineBBox( box );
    }
}
