public sealed class GameManager : Component
{
	[Property] TileManager TileManager { get; set; }
	[Property] PlayerManager PlayerManager { get; set; }
	[Property] public SceneFile SceneToLoadFinish { get; set; }

	[Property, Group( "Debug" )] public bool Debug_DisableGridPhysics { get; set; } = false;

	public TimeUntil CountdownTimer = 3f;
	public bool CountdownActive { get; private set; } = false;
	public bool GameInProgress { get; private set; } = false;

	// Results phase: shown after a winner is decided, before the scene swaps back to the lobby.
	public const float ResultsDuration = 7f;
	// Drop the arena over the first chunk of the results window, leaving the tail for the
	// podium to stand alone before the scene swaps.
	public const float DisintegrationDuration = 5f;
	[Sync] public bool IsShowingResults { get; private set; } = false;
	[Sync] public GameObject Winner { get; private set; }
	[Sync] public TimeUntil ResultsTimer { get; private set; }

	// Host-only guard so we only kick off the scene change once.
	private bool _hasFinishedResults = false;

	// Host-only: tiles queued to drop during results, sorted outward from the podium.
	private readonly List<(Tile tile, TimeUntil at)> _disintegrationSchedule = new();

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

		if ( CountdownActive )
		{
			if ( CountdownTimer <= 0f )
			{
				GameInProgress = true;
				CountdownActive = false;
				if ( !Debug_DisableGridPhysics ) TileManager.ActivateGrid();
				PlayerManager.EnablePlayersInput();
			}
		}

		if ( IsShowingResults )
		{
			TickDisintegration();

			if ( !_hasFinishedResults && ResultsTimer <= 0f )
			{
				_hasFinishedResults = true;
				FinishGame();
			}
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

		var name = player.Network?.Owner?.DisplayName ?? player.GameObject.Name;

		// Destroy hasn't propagated yet, so filter out the eliminated player explicitly.
		var remaining = Scene.GetAllComponents<PlayerController>()
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
			BeginResults( remaining.FirstOrDefault() );
		}
	}

	/// <summary>
	/// Host-only. Enter the post-game results phase: declare the winner, freeze the game,
	/// set up the podium tile, and start the timer that eventually swaps back to the lobby scene.
	/// </summary>
	private void BeginResults( PlayerController winner )
	{
		if ( !Networking.IsHost ) return;
		if ( IsShowingResults ) return;

		GameInProgress = false;

		var winnerGameObject = winner.IsValid() ? winner.GameObject : null;

		GameObject podiumGameObject = null;
		if ( winnerGameObject.IsValid() )
		{
			podiumGameObject = SetupPodium( winner );
			BroadcastFreezeWinner( winnerGameObject );

			// Center the winner on the podium tile so a stray last step can't carry them off the edge.
			// Done after the freeze so there's no input window between snapping and locking inputs.
			if ( podiumGameObject.IsValid() )
				BroadcastTeleportWinner( winnerGameObject, podiumGameObject.WorldPosition + Vector3.Up * 8f );
		}

		ScheduleDisintegration( podiumGameObject );

		BroadcastResultsBegin( winnerGameObject );

		var winnerName = winner?.Network?.Owner?.DisplayName ?? "Unknown";
		Log.Info( $"{winnerName} won! Showing results for {ResultsDuration}s." );
	}

	// Find the tile the winner is standing on and convert it into a golden podium. If they
	// were mid-air, spawn a fresh podium tile above the arena center and teleport them onto it.
	// Returns the podium tile's prefab root so callers can exclude it from the disintegration wave.
	private GameObject SetupPodium( PlayerController winner )
	{
		var winnerPos = winner.WorldPosition;
		// Start well above the player so we're clearly outside their body capsule, trace down
		// past their feet. Ignore both the player root and the separate ColliderObject — the
		// "Colliders" child isn't tagged "player", so WithoutTags alone won't exclude it.
		var trace = Scene.Trace.Ray( winnerPos + Vector3.Up * 200f, winnerPos + Vector3.Down * 60f )
			.WithoutTags( "player" )
			.IgnoreGameObjectHierarchy( winner.GameObject );
		if ( winner.ColliderObject.IsValid() )
			trace = trace.IgnoreGameObjectHierarchy( winner.ColliderObject );
		var result = trace.Run();

		GameObject podiumGameObject = null;
		if ( result.Hit && result.GameObject.IsValid() )
		{
			// result.GameObject is the TileModelCollider; one level up is the tile prefab root,
			// which contains the Tile component on a sibling child. Don't use .Root — that walks
			// all the way up to the scene-level TileManager and would tint the whole arena.
			var tileRoot = result.GameObject.Parent;
			var existingTile = tileRoot?.GetComponentInChildren<Tile>();
			if ( existingTile != null )
			{
				podiumGameObject = tileRoot;
			}
		}

		if ( podiumGameObject == null )
		{
			// Mid-air winner: spawn a podium tile above the arena center. The winner is teleported
			// onto it by BeginResults, not here, so the snap happens after the freeze.
			podiumGameObject = SpawnPodiumGameObject();
			if ( podiumGameObject == null )
			{
				Log.Warning( "[Results] Could not produce a podium tile for mid-air winner." );
				return null;
			}
		}

		BroadcastConvertToPodium( podiumGameObject );
		return podiumGameObject;
	}

	// Host-only. Build an outward-from-podium drop schedule for every non-podium tile, spread
	// across DisintegrationDuration. Tile.BreakTile is host-authoritative and flips a [Sync]
	// _falling flag, so clients animate the cascade for free.
	private void ScheduleDisintegration( GameObject podiumGameObject )
	{
		_disintegrationSchedule.Clear();
		if ( TileManager == null ) return;

		var podiumPos = podiumGameObject.IsValid()
			? podiumGameObject.WorldPosition
			: TileManager.WorldPosition;

		var candidates = new List<(Tile tile, float distance)>();
		foreach ( var tile in TileManager.GameObject.GetComponentsInChildren<Tile>() )
		{
			if ( !tile.IsValid() ) continue;
			// Tile lives on a child of the prefab root; comparing parents skips the podium tile.
			if ( podiumGameObject.IsValid() && tile.GameObject.Parent == podiumGameObject ) continue;

			// Use horizontal distance only so every layer ripples outward together,
			// instead of cascading top-layer-first to bottom-layer-last.
			float horizontalDistance = (tile.WorldPosition - podiumPos).WithZ( 0f ).Length;
			candidates.Add( (tile, horizontalDistance) );
		}

		candidates.Sort( ( a, b ) => a.distance.CompareTo( b.distance ) );

		for ( int i = 0; i < candidates.Count; i++ )
		{
			float t = candidates.Count > 1 ? (float)i / (candidates.Count - 1) : 0f;
			TimeUntil dropAt = t * DisintegrationDuration;
			_disintegrationSchedule.Add( (candidates[i].tile, dropAt) );
		}
	}

	private void TickDisintegration()
	{
		if ( _disintegrationSchedule.Count == 0 ) return;

		for ( int i = _disintegrationSchedule.Count - 1; i >= 0; i-- )
		{
			var entry = _disintegrationSchedule[i];
			if ( !entry.tile.IsValid() )
			{
				_disintegrationSchedule.RemoveAt( i );
				continue;
			}
			if ( entry.at <= 0f )
			{
				entry.tile.BreakTile();
				_disintegrationSchedule.RemoveAt( i );
			}
		}
	}

	private GameObject SpawnPodiumGameObject()
	{
		if ( TileManager == null || !TileManager.TilePrefab.IsValid() ) return null;

		// Raise the podium above the top layer so it doesn't z-fight with the existing center tile.
		var spawnPos = TileManager.WorldPosition + Vector3.Up * 96f;
		var tileGameObject = TileManager.TilePrefab.Clone( new CloneConfig
		{
			Parent = TileManager.GameObject,
			StartEnabled = true,
			Transform = new Transform( spawnPos )
		} );
		tileGameObject.Name = "Tile_Podium";
		tileGameObject.NetworkSpawn();
		return tileGameObject;
	}

	// Fanned out so every client locally disables the regular Tile behavior on this GameObject
	// and swaps in a PodiumTile component (which tints it gold + resets any in-progress wobble).
	[Rpc.Broadcast]
	private void BroadcastConvertToPodium( GameObject tileGameObject )
	{
		if ( !tileGameObject.IsValid() ) return;

		// Tile lives on a child of the prefab root, so search downward.
		var tile = tileGameObject.GetComponentInChildren<Tile>();
		if ( tile != null )
		{
			tile.SetTriggerEnabled( false );
			tile.Enabled = false;
		}

		if ( tileGameObject.GetComponent<PodiumTile>() == null )
		{
			tileGameObject.AddComponent<PodiumTile>();
		}
	}

	// Fanned out so every client sets the winner's local PlayerController flags. Matches the
	// pattern used by PlayerManager.EnablePlayersInput.
	[Rpc.Broadcast]
	private void BroadcastFreezeWinner( GameObject winnerGameObject )
	{
		if ( !winnerGameObject.IsValid() ) return;
		var pc = winnerGameObject.GetComponent<PlayerController>();
		if ( pc == null ) return;
		pc.UseInputControls = false;
		pc.UseCameraControls = false;
		// Clear any held input — otherwise the last WishVelocity (e.g. W still pressed)
		// keeps driving the controller forward after input is disabled.
		pc.WishVelocity = Vector3.Zero;
	}

	// Only the owning client actually moves the transform — it owns the player's authority.
	[Rpc.Broadcast]
	private void BroadcastTeleportWinner( GameObject winnerGameObject, Vector3 position )
	{
		if ( !winnerGameObject.IsValid() ) return;
		if ( !winnerGameObject.Network.IsOwner ) return;
		winnerGameObject.WorldPosition = position;
	}

	// Fanned out from the host so every client (including host) sets the same local results
	// state and starts its own timer. Plain RPC instead of [Sync] because [Sync] props on this
	// scene-level component don't propagate to non-host clients in this project's setup.
	[Rpc.Broadcast]
	private void BroadcastResultsBegin( GameObject winnerGameObject )
	{
		IsShowingResults = true;
		Winner = winnerGameObject;
		ResultsTimer = ResultsDuration;
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

	private void FinishGame()
	{
		if ( !Networking.IsHost ) return;

		GameInProgress = false;

		var loadOptions = new SceneLoadOptions();
		loadOptions.SetScene( SceneToLoadFinish );
		if ( !Game.ChangeScene( loadOptions ) )
		{
			Log.Error( $"Failed to load scene '{SceneToLoadFinish}'." );
		}
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
