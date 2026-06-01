using Sandbox;
using System.Linq;

// This a trigger listerner, needs to listen for when player enters the trigger and exits the trigger
//  On enter, need to set the player as ready, and on exit, need to set the player as not ready. This will be used for the lobby system, where players can ready up before the game starts.
// When player is marked as ready we want to update the state on the player to display text above their head that says "ready" and when they are not ready, we want to remove that text.

public sealed class LobbyReadyUp : Component, Component.ITriggerListener
{

	protected override void OnUpdate()
	{

	}

	public void OnTriggerEnter( Collider other )
	{
		var indicator = GetReadyStateIndicator( other );
		if ( indicator is null ) return;

		indicator.Tint = Color.Green;
		Log.Info( $"Player {other.GameObject.Root.Name} is ready" );
	}

	public void OnTriggerExit( Collider other )
	{
		var indicator = GetReadyStateIndicator( other );
		if ( indicator is null ) return;

		indicator.Tint = Color.Red;
		Log.Info( $"Player {other.GameObject.Root.Name} is not ready" );
	}

	private static ModelRenderer GetReadyStateIndicator( Collider other )
	{
		var readyStateIndicator = other.GameObject.Root.Children
			.FirstOrDefault( c => c.Name == "ReadyStateIndicator" );
		Log.Info( $"Looking for ReadyStateIndicator in {other.GameObject.Root.Name}, found: {readyStateIndicator != null}" );
		return readyStateIndicator?.GetComponent<ModelRenderer>();
	}
}
