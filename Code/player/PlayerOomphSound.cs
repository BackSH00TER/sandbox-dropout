using Sandbox;

public sealed class PlayerOomphSound : Component, PlayerController.IEvents
{
	[Property] public PlayerController PlayerController { get; set; }
	[Property] public SoundEvent Sound { get; set; }

	[Rpc.Broadcast]
	private void BroadcastSound()
	{
		GameObject.PlaySound( Sound );
	}

	void PlayerController.IEvents.OnLanded( float distance, Vector3 impactVelocity )
	{
		if ( distance > 100 ) BroadcastSound();
	}
}
