using System.Linq;
using System.Threading.Tasks;
using Sandbox;

/// <summary>
/// Drop-in replacement for <see cref="Sandbox.NetworkHelper"/> in the lobby scene.
/// Handles three spawn cases:
/// <list type="bullet">
/// <item>Initial host startup — creates the lobby and (via <see cref="OnActive"/>) spawns the host.</item>
/// <item>A client joining a live lobby — spawns their player via <see cref="OnActive"/>.</item>
/// <item>Game→lobby scene reload — host-only <see cref="OnStart"/> pass iterates <see cref="Connection.All"/>
/// and spawns a player for any connection that doesn't already have one. Needed because the
/// per-client <c>Scene.Load</c> swap used by <c>VictoryManager.BroadcastLoadLobbyScene</c>
/// doesn't re-fire <see cref="Component.INetworkListener.OnActive"/> for existing connections.</item>
/// </list>
/// </summary>
public sealed class LobbyNetworkSpawner : Component, Component.INetworkListener
{
    /// <summary>Create a lobby on first load if one isn't already active.</summary>
    [Property] public bool StartServer { get; set; } = true;

    /// <summary>Prefab cloned for each connected player.</summary>
    [Property] public GameObject PlayerPrefab { get; set; }

    /// <summary>Size of the box (centered on each <see cref="SpawnPoint"/>) that players are randomly spawned in.</summary>
    [Property] public Vector3 SpawnAreaSize { get; set; } = new Vector3( 150f, 150f, 0f );

    /// <summary>Draw the spawn jitter box around each <see cref="SpawnPoint"/> in the editor.</summary>
    [Property] public bool ShouldDrawSpawnGizmo { get; set; } = false;

    // Async, runs before OnStart. On the host's very first lobby load Networking isn't
    // active yet — we create the lobby here. Game→lobby reloads skip this branch because
    // Networking is already active by then.
    protected override async Task OnLoad()
    {
        if ( Scene.IsEditor ) return;

        if ( StartServer && !Networking.IsActive )
        {
            LoadingScreen.Title = "Creating Lobby";
            await Task.DelayRealtimeSeconds( 0.1f );
            Networking.CreateLobby( new() );
        }
    }

    // Fires once when this scene becomes active for the local client. We only do work on
    // the host: walk every existing Connection and spawn a player for any that don't have
    // one. This is the game→lobby path — those connections were already joined before the
    // scene swap, so the engine won't fire OnActive for them again (see OnActive below).
    protected override void OnStart()
    {
        if ( !Networking.IsHost ) return;
        if ( !Networking.IsActive ) return;

        foreach ( Connection client in Connection.All )
        {
            if ( HasPlayerControllerFor( client ) ) continue;
            Log.Info( $"LobbyNetworkSpawner: OnStart for {client}, spawning player." );
            SpawnFor( client );
        }
    }

    // Engine-driven join hook, fired on the host when a Connection becomes active in this
    // scene. Triggers for: the local connection on initial bootup, and any remote client
    // joining a live lobby. Does NOT re-fire for connections that were already joined when
    // the scene reloads locally — that case is handled by OnStart.
    public void OnActive( Connection channel )
    {
        // Guard against double-spawn — OnStart may have already covered this connection.
        if ( HasPlayerControllerFor( channel ) ) return;
        Log.Info( $"LobbyNetworkSpawner: OnActive for {channel}, spawning player." );
        SpawnFor( channel );
    }

    // True if a networked PlayerController owned by this connection already exists in the
    // scene. Used by both OnStart and OnActive to avoid spawning a second player for the
    // same connection when both paths cover it.
    private bool HasPlayerControllerFor( Connection client )
    {
        return Scene.GetAllComponents<PlayerController>()
            .Any( pc => pc.IsValid() && pc.Network.Active && pc.Network.OwnerId == client.Id );
    }

    // Spawn a player for the given connection. The host owns this logic, and the engine
    // will automatically replicate the new player to the client.
    private void SpawnFor( Connection client )
    {
        if ( !PlayerPrefab.IsValid() ) return;

        Transform spawnTransform = FindSpawnLocation().WithScale( 1f );
        GameObject player = PlayerPrefab.Clone( spawnTransform, name: $"Player - {client.Name}" );
        player.NetworkSpawn( client );
    }

    // Pick a random spawn point and jitter it inside SpawnAreaSize.
    private Transform FindSpawnLocation()
    {
        SpawnPoint[] points = Scene.GetAllComponents<SpawnPoint>().ToArray();
        if ( points.Length == 0 ) return WorldTransform;

        SpawnPoint anchor = Game.Random.FromArray( points );
        Transform anchorTransform = anchor.WorldTransform;

        Vector3 half = SpawnAreaSize * 0.5f;
        Vector3 offset = new Vector3(
            Game.Random.Float( -half.x, half.x ),
            Game.Random.Float( -half.y, half.y ),
            Game.Random.Float( -half.z, half.z )
        );
        return anchorTransform.WithPosition( anchorTransform.Position + offset );
    }

    // Debug element that draws a gizmo around the spawn area to visualize the area.
    protected override void DrawGizmos()
    {
        if ( !ShouldDrawSpawnGizmo ) return;
        if ( SpawnAreaSize.IsNearZeroLength ) return;

        SpawnPoint[] points = Scene.GetAllComponents<SpawnPoint>().ToArray();
        if ( points.Length == 0 ) return;

        Gizmo.Draw.Color = Gizmo.IsSelected ? Color.Cyan : Color.Cyan.WithAlpha( 0.4f );
        BBox box = BBox.FromPositionAndSize( Vector3.Zero, SpawnAreaSize );

        foreach ( SpawnPoint point in points )
        {
            using ( Gizmo.Scope( "lobby-spawn-area", point.WorldTransform ) )
            {
                Gizmo.Draw.LineBBox( box );
            }
        }
    }
}
