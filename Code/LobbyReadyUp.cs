using Sandbox;

// Trigger listener for the lobby ready-up zone. Flips the networked
// PlayerReadyState.IsReady on the player that entered/exited. The indicator
// color and lobby board both react to that bool via PlayerReadyState's change
// callback, so this component intentionally knows nothing about visuals.

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
