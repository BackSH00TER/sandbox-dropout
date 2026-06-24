using Sandbox;

public sealed class PlayerImpactSound : Component, PlayerController.IEvents
{
	[Property] public PlayerController PlayerController { get; set; }
	[Property] public SoundEvent Sound { get; set; }

	// Fallback for scenes with no TileManager (e.g. lobby).
	private readonly float DefaultMinFallDistance = TileManager.Current?.LayerSpacing * 1.2f ?? 400f;
	[Rpc.Broadcast]
	private void BroadcastSound()
	{
		GameObject.PlaySound( Sound );
	}

	void PlayerController.IEvents.OnLanded( float distance, Vector3 impactVelocity )
	{
		float minFallDistance = DefaultMinFallDistance;
		if ( distance > minFallDistance ) BroadcastSound();
	}
}
