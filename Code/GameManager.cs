using System;
using System.Linq;
using Sandbox;

/// <summary>
/// Owns the core game loop: building the arena, spawning players, running the
/// pre-game countdown, and detecting eliminations. Once only one player remains it
/// hands off to <see cref="VictoryManager"/> for the post-game victory sequence.
/// </summary>
public sealed class GameManager : Component
{
	[Property] TileManager TileManager { get; set; }
	[Property] PlayerManager PlayerManager { get; set; }
	[Property] VictoryManager VictoryManager { get; set; }
	[Property] public SoundEvent EliminatedSound { get; set; }

	[Property] public GameObject CountdownDronePrefab { get; set; }
	[Property] public Vector3 CountdownDroneSpawnOffset { get; set; } = new Vector3( 0f, 0f, 220f );

	[Property, Group( "Debug" )] public bool Debug_DisableGridPhysics { get; set; } = false;

	public TimeUntil CountdownTimer = 5f;
	public bool CountdownActive { get; private set; } = false;
	public bool GameInProgress { get; private set; } = false;

	public static GameManager Current { get; private set; }

	protected override void OnEnabled()
	{
		Current = this;
	}

	protected override void OnDisabled()
	{
		if ( Current == this )
			Current = null;
	}

	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;

		TileManager.BuildGrid();
		SpawnCountdownDrone();
		PlayerManager.SpawnPlayers();
		CountdownActive = true;
	}

	private void SpawnCountdownDrone()
	{
		if ( CountdownDronePrefab == null ) return;

		Vector3 spawnPos = TileManager.IsValid()
			? TileManager.WorldPosition + CountdownDroneSpawnOffset
			: WorldPosition + CountdownDroneSpawnOffset;

		GameObject drone = CountdownDronePrefab.Clone( new CloneConfig
		{
			Transform = new Transform( spawnPos ),
			Name = "CountdownDrone"
		} );
		drone.NetworkSpawn();
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;

		if ( CountdownActive && CountdownTimer <= 0f )
		{
			GameInProgress = true;
			CountdownActive = false;
			if ( !Debug_DisableGridPhysics ) TileManager.ActivateGrid();
			PlayerManager.EnablePlayersInput();
		}
	}

	public void StartGame()
	{
		if ( !Networking.IsHost ) return;

		GameInProgress = true;
		CountdownActive = false;
		if ( !Debug_DisableGridPhysics ) TileManager.ActivateGrid();
		PlayerManager.EnablePlayersInput();
	}

	public void PlayerEliminated( PlayerController player )
	{
		if ( !Networking.IsHost ) return;
		if ( player == null || !player.IsValid() ) return;

		string name = player.Network?.Owner?.DisplayName ?? player.GameObject.Name;

		// Destroy hasn't propagated yet, so filter out the eliminated player explicitly.
		List<PlayerController> remaining = Scene.GetAllComponents<PlayerController>()
			.Where( p => p.IsValid() && p != player )
			.ToList();

		Log.Info( $"Player '{name}' eliminated. Players remaining: {remaining.Count}" );

		// Notify clients first. We pass the owner's connection ID (a value) instead of
		// the player GameObject — the destroy packet can race the RPC and arrive first
		// on the owning client, which would null out a GameObject param and skip both
		// the sound and SpectatorMode activation.
		Guid ownerId = player.Network?.Owner?.Id ?? Guid.Empty;
		BroadcastPlayerEliminated( ownerId );

		PlayerManager.DestroyPlayer( player );

		if ( remaining.Count <= 1 )
		{
			GameInProgress = false;
			VictoryManager?.BeginVictory( remaining.FirstOrDefault() );
		}
	}

	// Fanned out from the host. The owning client of the eliminated player flips its
	// local SpectatorMode on and plays the elimination sound; everyone else just sees
	// the player's GameObject get destroyed. We compare connection IDs rather than
	// reading IsOwner off a GameObject ref so this still works if the destroy packet
	// arrives before this RPC.
	[Rpc.Broadcast]
	private void BroadcastPlayerEliminated( Guid ownerConnectionId )
	{
		if ( Connection.Local == null || Connection.Local.Id != ownerConnectionId ) return;

		if ( EliminatedSound != null )
		{
			// ListenLocal makes the sound play from the listener regardless of world
			// position — needed because the SoundEvent is 3D and Sound.Play with no
			// position plays at world origin, which is far from the player when they
			// fall into the killbox.
			var handle = Sound.Play( EliminatedSound );
			handle.Volume = 0.2f;
			handle.ListenLocal = true;
		}

		SpectatorMode.Current?.Activate();
	}

	/// <summary>
	/// TODO When a client connects to the server.
	/// </summary>
	/// <param name="channel"></param>
	public void OnActive( Connection channel )
	{
		if ( !Networking.IsHost ) return;

		if ( GameInProgress )
		{
			Log.Info( $"Game already in progress, not spawning player '{channel.Name}' into the game." );
			// Spawn them in spectate mode or something
			// PlayerManager.SpawnSpectator( channel );
			return;
		}
	}
}
