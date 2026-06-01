using Sandbox;

public sealed class GameManager : Component
{
	[Property] TileManager TileManager { get; set; }
	[Property] PlayerManager PlayerManager { get; set; }
	public bool CountdownActive { get; private set; } = false;
	public bool GameInProgress { get; private set; } = false;
	public TimeUntil CountdownTimer = 2f;

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
				//TileManager.ActivateGrid();
				PlayerManager.EnablePlayersInput();
			}
		}
	}

	/// <summary>
	/// When a client connects to the server.
	/// </summary>
	/// <param name="channel"></param>
	public void OnActive( Connection channel )
	{
		if ( !Networking.IsHost ) return;

		Log.Info( $"Player '{channel.Name}' has joined the game" );

		if ( GameInProgress )
		{
			// Spawn them in spectate mode or something
			// PlayerManager.SpawnSpectator( channel );
			return;
		}
	}
}
