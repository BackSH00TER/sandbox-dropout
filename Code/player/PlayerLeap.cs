using System.Numerics;
using Sandbox;

public sealed class PlayerLeap : Component, PlayerController.IEvents
{
	[Property] PlayerController TargetController { get; set; }
	[Property] GameObject TargetBody { get; set; }
	[Property] SkinnedModelRenderer TargetRenderer { get; set; }
	[Property] float LeapCooldown { get; set; } = 3f;

	bool isLeaping = false;
	private TimeUntil leapCooldownTime = -1f;


	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;

		if ( TargetController.UseInputControls == false )
			return;

		if ( !TargetController.IsOnGround )
			return;

		if ( Input.Pressed( "attack1" ) && isLeaping == false && leapCooldownTime )
			BeginLeap();
	}

	[Rpc.Broadcast]
	public void BeginLeap()
	{
		// Set state
		isLeaping = true;
		leapCooldownTime = LeapCooldown;

		// Update properties
		TargetController.UseInputControls = false;
		TargetRenderer.Set( "special_movement_states", 2 );

		// Get movement variables
		var rb = TargetController.GetComponent<Rigidbody>();
		Vector3 upVelocity = Vector3.Up * 400f;
		Vector3 flatEyeAngle = new Vector3( TargetController.EyeAngles.Forward.x, TargetController.EyeAngles.Forward.y, 0 );
		Vector3 forwardVelocity = flatEyeAngle.Normal * 400f;
		Vector3 leapVelocity = upVelocity + forwardVelocity;

		// Apply movement
		TargetController.Jump( upVelocity );
		rb.Velocity = leapVelocity;

		Rotation newRotation = Rotation.LookAt( forwardVelocity );
		TargetBody.WorldRotation = newRotation;
	}

	void PlayerController.IEvents.OnLanded( float distance, Vector3 impactVelocity )
	{
		if ( isLeaping )
			FinishLeap();
	}

	private void FinishLeap()
	{
		isLeaping = false;

		TargetRenderer.Set( "special_movement_states", 0 );
		TargetController.UseInputControls = true;
	}

}