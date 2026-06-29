using System;
using System.Linq;

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

	/// <summary>True on every client while the launch countdown is running.</summary>
	public bool IsLaunching { get; private set; }

	/// <summary>Seconds left on the launch countdown on this client. Only meaningful while <see cref="IsLaunching"/>.</summary>
	public float LaunchSecondsRemaining => MathF.Max( 0f, (float)_launchAt );

	/// <summary>Scene-wide singleton so the lobby UI can find us without a hard reference.</summary>
	public static LobbyManager Current { get; private set; }

	/// <summary>Seconds to wait, with everyone still ready, before actually loading the game scene.</summary>
	private float LaunchSeconds { get; set; } = 3f;

	/// <summary>Seconds the screen takes to fade to black at the tail of the launch countdown. Must be &lt;= <see cref="LaunchSeconds"/>.</summary>
	private const float LaunchFadeDuration = 2f;

	private TimeUntil _launchAt;
	private bool _hasLaunched;
	private bool _hasTriggeredLaunchFade;

	private float percentReadyRequired = 0.5f; // 50% of players

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
		// Per-client: kick off the screen fade-out once the countdown enters the fade window.
		if ( IsLaunching && !_hasTriggeredLaunchFade && (float)_launchAt <= LaunchFadeDuration )
		{
			_hasTriggeredLaunchFade = true;
			ScreenFade.FadeOut( LaunchFadeDuration );
		}

		// Host owns the state machine; everyone runs the launch-elapsed check locally
		// (broadcast-started, so all clients hit zero at roughly the same time).
		if ( IsLaunching && (float)_launchAt <= 0f )
		{
			if ( Networking.IsHost && !_hasLaunched )
			{
				_hasLaunched = true;
				LoadGameScene();
			}
			IsLaunching = false;
		}

		if ( !Networking.IsHost ) return;
		if ( _hasLaunched ) return;

		var playerReadyStates = Scene.GetAllComponents<PlayerReadyState>().ToList();
		int readyCount = playerReadyStates.Count( s => s.IsReady );
		// MinPlayers gate also avoids the 0/0 case after a scene swap where no
		// PlayerReadyState components exist yet and the majority check would pass.
		bool hasMajority = playerReadyStates.Count >= MinPlayers
			&& readyCount >= playerReadyStates.Count * percentReadyRequired;

		if ( hasMajority && !IsLaunching )
		{
			BroadcastCountdownStart( LaunchSeconds );
		}
		else if ( !hasMajority && IsLaunching )
		{
			BroadcastCountdownCancel();
		}
	}

	[Rpc.Broadcast]
	private void BroadcastCountdownStart( float seconds )
	{
		_launchAt = seconds;
		IsLaunching = true;
		_hasTriggeredLaunchFade = false;
	}

	[Rpc.Broadcast]
	private void BroadcastCountdownCancel()
	{
		IsLaunching = false;
		if ( _hasTriggeredLaunchFade )
		{
			_hasTriggeredLaunchFade = false;
			ScreenFade.FadeIn( LaunchFadeDuration );
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
