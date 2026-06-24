using Sandbox;

public sealed class PlayerLeap : Component, PlayerController.IEvents
{
	[Property] PlayerController TargetController { get; set; }
	[Property] SkinnedModelRenderer TargetRenderer { get; set; }

	bool isLeaping = false;
	private TimeUntil leapCooldown = -1f;

	protected override void OnFixedUpdate()
	{
		if ( TargetController.UseInputControls == false )
			return;

		if ( Input.Pressed( "attack1" ) && isLeaping == false && leapCooldown )
			BeginLeap();
	}

	private void BeginLeap()
	{
		// Set state
		isLeaping = true;
		leapCooldown = 3f;

		// Update properties
		TargetController.UseInputControls = false;
		TargetRenderer.Set( "special_movement_states", 2 );

		// Get movement variables
		var rb = TargetController.GetComponent<Rigidbody>();
		Vector3 upVelocity = Vector3.Up * 400f;
		Vector3 forwardVelocity = TargetController.EyeAngles.Forward * 400f;
		Vector3 leapVelocity = upVelocity + forwardVelocity;

		// Apply movement
		TargetController.Jump( upVelocity );
		rb.Velocity = leapVelocity;
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