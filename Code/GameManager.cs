using Sandbox;

public sealed class GameManager : Component
{
	[Property] TileManager TileManager { get; set; }
	[Property] PlayerManager PlayerManager { get; set; }
	[Property] public SceneFile SceneToLoad { get; set; }
	public TimeUntil CountdownTimer = 3f;
	public bool CountdownActive { get; private set; } = false;
	public bool GameInProgress { get; private set; } = false;
	private int _alivePlayerCount = 0;

	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;

		TileManager.BuildGrid();
		PlayerManager.SpawnPlayers();
		_alivePlayerCount = Scene.GetAllComponents<PlayerController>().Count();
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
				TileManager.ActivateGrid();
				PlayerManager.EnablePlayersInput();
			}
		}
	}

	public void StartGame()
	{
		if ( !Networking.IsHost ) return;

		GameInProgress = true;
		CountdownActive = false;
		TileManager.ActivateGrid();
		PlayerManager.EnablePlayersInput();
	}

	public void PlayerEliminated()
	{
		if ( !Networking.IsHost ) return;

		_alivePlayerCount--;
		Log.Info( $"Player eliminated. Players remaining: {_alivePlayerCount}" );

		if ( _alivePlayerCount <= 1 )
		{
			FinishGame();
		}
	}

	private void FinishGame()
	{
		if ( !Networking.IsHost ) return;

		GameInProgress = false;

		var loadOptions = new SceneLoadOptions();
		loadOptions.SetScene( SceneToLoad );
		if ( !Game.ChangeScene( loadOptions ) )
		{
			Log.Error( $"Failed to load scene '{SceneToLoad}'." );
		}
	}

	/// <summary>
	/// When a client connects to the server.
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
