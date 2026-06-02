using Sandbox;

/// <summary>
/// Trigger listener for the lobby ready-up zone. Flips the networked
/// <see cref="PlayerReadyState.IsReady"/> on the player that entered/exited.
/// </summary>
public sealed class LobbyReadyUp : Component, Component.ITriggerListener
{
	public void OnTriggerEnter( Collider other )
	{
		var state = other.GameObject.Root.GetComponent<PlayerReadyState>();
		if ( state is null ) return;

		state.IsReady = true;
	}

	public void OnTriggerExit( Collider other )
	{
		var state = other.GameObject.Root.GetComponent<PlayerReadyState>();
		if ( state is null ) return;

		state.IsReady = false;
	}
}
