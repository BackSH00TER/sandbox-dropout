public sealed class GameManager : Component
{
	[Property] TileManager TileManager { get; set; }
	[Property] PlayerManager PlayerManager { get; set; }
	[Property] public SceneFile SceneToLoadFinish { get; set; }
	[Property] public GameObject ConfettiPrefab { get; set; }

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

	// Confetti bursts queued during results. Populated locally on every client when the
	// host's BroadcastBeginConfetti RPC arrives, then ticked locally to spawn prefab clones.
	private readonly List<(TimeUntil at, Vector3 pos, Vector3 dir)> _confettiBurstSchedule = new();

	// Per-client: drives the winner's celebratory hops during results. Only the client that
	// owns the winner GameObject actually animates the transform; everyone else watches via
	// network transform sync.
	private const float WinnerHopHeight = 40f;
	private const float WinnerHopPeriod = 0.55f;  // seconds per up-and-back-down cycle
	private float _winnerHopStartTime;
	private Vector3 _winnerHopGroundPosition;
	private bool _hasCapturedHopGround;
	private bool _wasShowingResults;

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
		// Per-client: drive the winner's hops regardless of host status.
		TickWinnerHop();
		// Per-client: drive the confetti schedule locally (populated by BroadcastBeginConfetti).
		TickConfettiBursts();

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

		// Confetti: fan out via RPC so every client populates its own local burst schedule.
		// [Sync] doesn't propagate reliably on this scene-singleton GameManager (verified), but
		// [Rpc.Broadcast] bodies do run on clients (same mechanism BroadcastResultsBegin uses).
		if ( winnerGameObject.IsValid() && podiumGameObject.IsValid() )
		{
			var winnerForward = winnerGameObject.WorldRotation.Forward.WithZ( 0f );
			if ( winnerForward.LengthSquared > 0.001f )
			{
				BroadcastBeginConfetti( podiumGameObject.WorldPosition, winnerForward.Normal );
			}
		}

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

	// Runs on every client. Resets the hop timer when results begin, then on the
	// owning client only, applies an upward impulse to the winner at a fixed interval.
	// Runs on every client. Resets state when results begin, then on the owning client
	// drives the winner's transform up-and-down with an abs(sin) wave for a continuous hop.
	private void TickWinnerHop()
	{
		if ( !IsShowingResults || !Winner.IsValid() )
		{
			_wasShowingResults = false;
			_hasCapturedHopGround = false;
			return;
		}

		if ( !_wasShowingResults )
		{
			_wasShowingResults = true;
			_winnerHopStartTime = Time.Now;
		}

		if ( !Winner.Network.IsOwner ) return;

		// Capture the ground position once on the owner client, after the freeze + teleport
		// have resolved, so the hop returns to the same spot every cycle.
		if ( !_hasCapturedHopGround )
		{
			_hasCapturedHopGround = true;
			_winnerHopGroundPosition = Winner.WorldPosition;
		}

		float elapsed = Time.Now - _winnerHopStartTime;
		float phase = (elapsed / WinnerHopPeriod) * (float)System.Math.PI;
		float z = System.Math.Abs( (float)System.Math.Sin( phase ) ) * WinnerHopHeight;
		Winner.WorldPosition = _winnerHopGroundPosition + Vector3.Up * z;
	}

	// Fanned out from the host so every client (including host) populates its own local
	// confetti schedule from the same pattern. Plain RPC — [Sync] doesn't propagate on this
	// scene-singleton GameManager (verified). Mirror of the BroadcastResultsBegin pattern.
	[Rpc.Broadcast]
	private void BroadcastBeginConfetti( Vector3 podiumPos, Vector3 winnerForward )
	{
		_confettiBurstSchedule.Clear();

		if ( winnerForward.LengthSquared < 0.001f ) return;
		winnerForward = winnerForward.Normal;

		// (delay seconds, yaw offset from "directly behind" in degrees, radius, height)
		var pattern = new (float delay, float yaw, float radius, float height)[]
		{
			( 0.00f,    0f, 70f, 20f ),  // dead behind
			( 0.60f,  -55f, 80f, 25f ),  // behind-left
			( 1.20f,   55f, 80f, 25f ),  // behind-right
			( 2.00f,    0f, 60f, 35f ),  // behind, higher
			( 2.80f,  -90f, 95f, 15f ),  // hard left flank
			( 2.80f,   90f, 95f, 15f ),  // hard right flank
			( 3.80f,    0f, 70f, 20f ),  // final center pop
		};

		foreach ( var (delay, yaw, radius, height) in pattern )
		{
			var offsetDir = Rotation.FromYaw( yaw ) * (-winnerForward);
			var pos = podiumPos + offsetDir * radius + Vector3.Up * height;
			_confettiBurstSchedule.Add( (delay, pos, winnerForward) );
		}
	}

	private void TickConfettiBursts()
	{
		if ( _confettiBurstSchedule.Count == 0 ) return;

		for ( int i = _confettiBurstSchedule.Count - 1; i >= 0; i-- )
		{
			var entry = _confettiBurstSchedule[i];
			if ( entry.at <= 0f )
			{
				SpawnConfettiLocally( entry.pos, entry.dir );
				_confettiBurstSchedule.RemoveAt( i );
			}
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

		// Disable the controller and freeze rigidbody motion so the winner-hop tick can drive
		// the transform directly without the move modes or physics overriding our position.
		pc.Enabled = false;
		var rb = winnerGameObject.GetComponent<Rigidbody>();
		if ( rb != null ) rb.MotionEnabled = false;
	}

	// Only the owning client actually moves the transform — it owns the player's authority.
	[Rpc.Broadcast]
	private void BroadcastTeleportWinner( GameObject winnerGameObject, Vector3 position )
	{
		if ( !winnerGameObject.IsValid() ) return;
		if ( !winnerGameObject.Network.IsOwner ) return;
		winnerGameObject.WorldPosition = position;
	}

	private static readonly Color[] ConfettiColors =
	{
		Color.Parse( "#FF5757" ) ?? Color.Red,
		Color.Parse( "#FFD93D" ) ?? Color.Yellow,
		Color.Parse( "#6BCB77" ) ?? Color.Green,
		Color.Parse( "#4D96FF" ) ?? Color.Blue,
		Color.Parse( "#FF6FFF" ) ?? Color.Magenta,
		Color.White,
	};

	private void SpawnConfettiLocally( Vector3 spawnPos, Vector3 launchDirection )
	{
		if ( !ConfettiPrefab.IsValid() ) return;

		var dir = launchDirection.LengthSquared > 0.001f ? launchDirection.Normal : Vector3.Forward;
		var rotation = Rotation.LookAt( dir );

		// One clone per color so each burst contains a mix of colored particles. The clone’s
		// ParticleSphereEmitter is single-shot (Loop=false, DestroyOnEnd=true) so each cleans itself up.
		foreach ( var color in ConfettiColors )
		{
			var clone = ConfettiPrefab.Clone( new CloneConfig
			{
				StartEnabled = true,
				Transform = new Transform( spawnPos, rotation )
			} );

			var effect = clone.GetComponent<ParticleEffect>();
			if ( effect != null ) effect.Tint = color;
		}
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
