using System.Collections.Generic;
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

    /// <summary>Optional explicit spawn points. If empty, falls back to any <see cref="SpawnPoint"/> in the scene, then this component's transform.</summary>
    [Property] public List<GameObject> SpawnPoints { get; set; }

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

    protected override void OnStart()
    {
        // Game→lobby case: every client locally loads the lobby scene via Scene.Load,
        // which doesn't re-fire OnActive for already-connected players. Host explicitly
        // drives the spawn here so every connection gets a player.
        if ( !Networking.IsHost ) return;
        if ( !Networking.IsActive ) return;

        foreach ( Connection client in Connection.All )
        {
            if ( HasPlayerFor( client ) ) continue;
            SpawnFor( client );
        }
    }

    public void OnActive( Connection channel )
    {
        // Guard against double-spawn — OnStart may have already covered this connection.
        if ( HasPlayerFor( channel ) ) return;
        SpawnFor( channel );
    }

    private bool HasPlayerFor( Connection client )
    {
        return Scene.GetAllComponents<PlayerController>()
            .Any( pc => pc.IsValid() && pc.Network.Active && pc.Network.OwnerId == client.Id );
    }

    private void SpawnFor( Connection client )
    {
        if ( !PlayerPrefab.IsValid() ) return;

        Transform spawnTransform = FindSpawnLocation().WithScale( 1f );
        GameObject player = PlayerPrefab.Clone( spawnTransform, name: $"Player - {client.Name}" );
        player.NetworkSpawn( client );
    }

    private Transform FindSpawnLocation()
    {
        if ( SpawnPoints != null && SpawnPoints.Count > 0 )
        {
            GameObject point = Game.Random.FromList( SpawnPoints );
            if ( point.IsValid() )
            {
                // Re-roll the jitter so each player in our OnStart loop gets a fresh position.
                point.GetComponent<LobbyPlayerSpawner>()?.RandomizePosition();
                return point.WorldTransform;
            }
        }

        SpawnPoint[] componentPoints = Scene.GetAllComponents<SpawnPoint>().ToArray();
        if ( componentPoints.Length > 0 )
        {
            SpawnPoint picked = Game.Random.FromArray( componentPoints );
            picked.GameObject.GetComponent<LobbyPlayerSpawner>()?.RandomizePosition();
            return picked.WorldTransform;
        }

        return WorldTransform;
    }
}
