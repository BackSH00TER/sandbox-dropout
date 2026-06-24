using Sandbox;

public sealed class PlayerImpactSound : Component, PlayerController.IEvents
{
	[Property] public PlayerController PlayerController { get; set; }
	[Property] public SoundEvent Sound { get; set; }
	[Property] public float MinFallDistance { get; set; } = 100f;

	[Rpc.Broadcast]
	private void BroadcastSound()
	{
		GameObject.PlaySound( Sound );
	}

	void PlayerController.IEvents.OnLanded( float distance, Vector3 impactVelocity )
	{
		if ( distance > MinFallDistance ) BroadcastSound();
	}
}
