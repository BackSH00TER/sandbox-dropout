using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

public sealed class GameManager : Component
{
	[Property] TileManager TileManager { get; set; }
	[Property] PlayerManager PlayerManager { get; set; }
	[Property] public SceneFile SceneToLoadFinish { get; set; }

	[Property, Group( "Debug" )] public bool Debug_DisableGridPhysics { get; set; } = false;

	public TimeUntil CountdownTimer = 3f;
	public bool CountdownActive { get; private set; } = false;
	public bool GameInProgress { get; private set; } = false;

	public static GameManager Current { get; private set; }

	private int _alivePlayerCount = 0;
	private readonly HashSet<Guid> _eliminatedIds = new();

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
				if ( !Debug_DisableGridPhysics ) TileManager.ActivateGrid();
				PlayerManager.EnablePlayersInput();
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

	public bool IsPlayerEliminated( GameObject playerGameObject )
	{
		return playerGameObject != null && _eliminatedIds.Contains( playerGameObject.Id );
	}

	public void PlayerEliminated( PlayerController player )
	{
		if ( !Networking.IsHost ) return;
		if ( player == null || !player.IsValid() ) return;
		if ( _eliminatedIds.Contains( player.GameObject.Id ) ) return;

		_alivePlayerCount--;
		var name = player.Network?.Owner?.DisplayName ?? player.GameObject.Name;
		Log.Info( $"Player '{name}' eliminated. Players remaining: {_alivePlayerCount}" );

		BroadcastPlayerEliminated( player.GameObject );

		if ( _alivePlayerCount <= 1 )
		{
			var winner = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( pc => pc.IsValid() && !_eliminatedIds.Contains( pc.GameObject.Id ) );
			var winnerName = winner?.Network?.Owner?.DisplayName ?? "Unknown";
			Log.Info( $"{winnerName} won!" );
			FinishGame();
		}
	}

	// Fanned out from the host to every client when a player is eliminated. Records the
	// player's id locally (so IsPlayerEliminated agrees on every machine) and activates
	// the player's Spectator component, which hides the body, disables controls, and —
	// for the owning client only — switches the camera into spectate mode.
	[Rpc.Broadcast]
	private void BroadcastPlayerEliminated( GameObject playerGameObject )
	{
		if ( playerGameObject == null ) return;
		_eliminatedIds.Add( playerGameObject.Id );

		var spectator = playerGameObject.GetComponent<Spectator>();
		spectator?.Activate();
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
