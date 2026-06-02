using System;
using System.Linq;
using Sandbox;

/// <summary>
/// Lives in the lobby scene. Host watches every <see cref="PlayerReadyState"/>
/// in the scene; once all connected players are ready (and we have at least
/// <see cref="MinPlayers"/>), it runs a short launch countdown and then loads
/// the gameplay scene for everyone. Walking out of the ready zone before the
/// countdown ends cancels the launch.
/// </summary>
public sealed class LobbyManager : Component
{
	/// <summary>Gameplay scene to load once everyone is ready.</summary>
	[Property] public SceneFile GameScene { get; set; }

	/// <summary>Minimum number of connected players required before the launch countdown can start.</summary>
	[Property] public int MinPlayers { get; set; } = 1;

	/// <summary>Seconds to wait, with everyone still ready, before actually loading the game scene.</summary>
	[Property] public float LaunchSeconds { get; set; } = 3f;

	/// <summary>Seconds left on the launch countdown. -1 means no countdown is running. Synced so clients can show it.</summary>
	[Sync] public float LaunchSecondsRemaining { get; private set; } = -1f;

	public bool IsLaunching => LaunchSecondsRemaining >= 0f;

	/// <summary>Scene-wide singleton so the lobby UI can find us without a hard reference.</summary>
	public static LobbyManager Current { get; private set; }

	private TimeUntil _launchAt;
	private bool _isCountdownActive;
	private bool _hasLaunched;

	protected override void OnEnabled()
	{
		Current = this;
	}

	protected override void OnDisabled()
	{
		if ( Current == this )
			Current = null;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( _hasLaunched ) return;

		var states = Scene.GetAllComponents<PlayerReadyState>().ToList();
		bool areAllPlayersReady = states.Count >= MinPlayers && states.All( s => s.IsReady );

		if ( areAllPlayersReady )
		{
			if ( !_isCountdownActive )
			{
				_isCountdownActive = true;
				_launchAt = LaunchSeconds;
			}

			LaunchSecondsRemaining = MathF.Max( 0f, (float)_launchAt );

			if ( (float)_launchAt <= 0f )
			{
				_hasLaunched = true;
				LaunchSecondsRemaining = -1f;
				LoadGameScene();
			}
		}
		else if ( _isCountdownActive )
		{
			// Someone walked out (or disconnected) — cancel.
			_isCountdownActive = false;
			LaunchSecondsRemaining = -1f;
		}
	}

	private void LoadGameScene()
	{
		if ( GameScene is null )
		{
			Log.Warning( "LobbyManager: GameScene is not assigned, can't launch." );
			return;
		}

		BroadcastLoadGameScene();
	}

	[Rpc.Broadcast]
	private void BroadcastLoadGameScene()
	{
		Game.ActiveScene.LoadFromFile( GameScene.ResourcePath );
	}
}
