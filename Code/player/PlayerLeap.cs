using System;
using System.Numerics;
using System.Runtime.InteropServices.Swift;
using Sandbox;
using Sandbox.ui;

public sealed class PlayerLeap : Component, PlayerController.IEvents
{
	[Property] PlayerController TargetController { get; set; }
	[Property] GameObject TargetBody { get; set; }
	[Property] SkinnedModelRenderer TargetRenderer { get; set; }

	public bool IsLeaping = false;
	public float LeapCooldown = 3f;
	public float LeapCooldownTime = 0f;

	protected override void OnStart()
	{
		if ( !Network.IsOwner )
		{
			return;
		}
		AddUI();
	}

	protected override void OnUpdate()
	{
		if ( !Network.IsOwner )
		{
			return;
		}

		if ( IsLeaping )
		{
			return;
		}

		LeapCooldownTime -= Time.Delta;
		if ( LeapCooldownTime < 0.01 )
		{
			if ( Input.Pressed( "attack1" ) && TargetController.UseInputControls )
			{
				BeginLeap();
			}
		}
	}

	public void AddUI()
	{
		GameObject gameObject = new GameObject( "PlayerControlsUI" );
		gameObject.NetworkMode = NetworkMode.Never;
		gameObject.AddComponent<ScreenPanel>();
		var ui = gameObject.AddComponent<PlayerControlsUI>();
		ui.PlayerLeap = this;
	}

	[Rpc.Broadcast]
	public void BeginLeap()
	{
		// Set state
		IsLeaping = true;
		LeapCooldownTime = LeapCooldown;

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
		if ( IsLeaping )
		{
			FinishLeap();
		}

	}

	private void FinishLeap()
	{
		IsLeaping = false;

		TargetRenderer.Set( "special_movement_states", 0 );
		TargetController.UseInputControls = true;
	}

}