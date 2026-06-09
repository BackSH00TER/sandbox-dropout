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

	[Property, Group( "Debug" )] public bool Debug_DisableGridPhysics { get; set; } = false;

	public TimeUntil CountdownTimer = 3f;
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
		PlayerManager.SpawnPlayers();
		CountdownActive = true;
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

		// Notify clients first — the eliminated player's owning client needs its
		// GameObject to still exist when SpectatorMode activates so the local check
		// against Network.IsOwner resolves correctly.
		BroadcastPlayerEliminated( player.GameObject );

		PlayerManager.DestroyPlayer( player );

		if ( remaining.Count <= 1 )
		{
			GameInProgress = false;
			VictoryManager?.BeginVictory( remaining.FirstOrDefault() );
		}
	}

	// Fanned out from the host. The owning client of the eliminated player flips its
	// local SpectatorMode on; everyone else just sees the player's GameObject get destroyed.
	[Rpc.Broadcast]
	private void BroadcastPlayerEliminated( GameObject playerGameObject )
	{
		if ( playerGameObject == null ) return;
		if ( !playerGameObject.Network.IsOwner ) return;

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
