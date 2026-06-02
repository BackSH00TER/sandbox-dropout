using System;
using Sandbox;

public sealed class Tile : Component, Component.ITriggerListener
{
	[Property] public float BreakDelay { get; set; } = 1.0f;
	[Property] public float FallDuration { get; set; } = 2.0f;
	[Property] public float WobbleAngle { get; set; } = 5.0f;
	[Property] public float WobbleSpeed { get; set; } = 25.0f;
	[Property] public float FallMass { get; set; } = 100f;
	[Property] public float FlashSpeed { get; set; } = 12f;
	[Property] public float DepressDepth { get; set; } = 3f;
	[Property] public float DepressSpeed { get; set; } = 25f;
	[Property] public Collider SolidCollider { get; set; }
	[Property] public Collider TriggerCollider { get; set; }
	[Property] public ModelRenderer Model { get; set; }
	[Property] public GameObject TileRoot { get; set; }

	[Sync] private bool _triggered { get; set; } = false;
	[Sync] private bool _falling { get; set; } = false;

	private TimeUntil _breakAt;
	private TimeUntil _destroyAt;
	private Rotation _restRotation;
	private Vector3 _modelRestPosition;
	private bool _appliedBreakLocally = false;
	private int _playersOnTile = 0;
	private Color _baseTint = Color.White;
	private float _wobblePhase;
	private float _wobbleSpeedJitter = 1f;
	private Rigidbody _rigidbody;

	protected override void OnStart()
	{
		_restRotation = TileRoot.IsValid() ? TileRoot.WorldRotation : WorldRotation;
		var rng = new Random( HashCode.Combine( GameObject.Id ) );
		_wobblePhase = (float)rng.NextDouble() * MathF.PI * 2f;
		_wobbleSpeedJitter = 0.75f + (float)rng.NextDouble() * 0.5f;

		if ( Model.IsValid() )
		{
			_modelRestPosition = Model.LocalPosition;
			_baseTint = Model.Tint;
		}

		if ( TriggerCollider.IsValid() )
		{
			TriggerCollider.Enabled = false;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( Networking.IsHost )
		{
			HostFixedUpdate();
		}

		ClientFixedUpdate();
	}

	private void HostFixedUpdate()
	{
		if ( _triggered && !_falling )
		{
			UpdateTriggeredAnimation();

			if ( _breakAt <= 0 )
			{
				BreakTile();
			}
		}

		if ( _falling && _destroyAt <= 0 )
		{
			TileRoot?.Destroy();
		}
	}

	private void ClientFixedUpdate()
	{
		if ( _triggered && !_falling )
		{
			UpdateTriggeredAnimation();
		}

		if ( Model.IsValid() && !_falling )
		{
			var target = _playersOnTile > 0 ? _modelRestPosition + Vector3.Down * DepressDepth : _modelRestPosition;
			Model.LocalPosition = Vector3.Lerp( Model.LocalPosition, target, Time.Delta * DepressSpeed );
		}

		if ( _falling && !_appliedBreakLocally )
		{
			ApplyBreakStateLocally();
		}
	}

	private void UpdateTriggeredAnimation()
	{
		float progress = MathX.Clamp( 1f - (float)_breakAt / BreakDelay, 0f, 1f );
		float angle = MathF.Sin( Time.Now * WobbleSpeed * _wobbleSpeedJitter + _wobblePhase ) * WobbleAngle * progress;
		var wobbleTarget = TileRoot.IsValid() ? TileRoot : GameObject;
		wobbleTarget.WorldRotation = _restRotation * Rotation.FromRoll( angle );

		if ( Model.IsValid() )
		{
			float pulse = (MathF.Sin( Time.Now * FlashSpeed ) + 1f) * 0.5f;
			Model.Tint = Color.Lerp( _baseTint, Color.White, pulse * progress );
		}
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( !other.Tags.Has( "player" ) ) return;

		_playersOnTile++;

		if ( !Networking.IsHost || _triggered || _falling ) return;

		_triggered = true;
		_breakAt = BreakDelay;
	}

	public void OnTriggerExit( Collider other )
	{
		if ( !other.Tags.Has( "player" ) ) return;

		_playersOnTile = Math.Max( 0, _playersOnTile - 1 );
	}

	public void SetTriggerEnabled( bool enabled )
	{
		if ( TriggerCollider.IsValid() )
		{
			TriggerCollider.Enabled = enabled;
		}
	}

	public void BreakTile()
	{
		if ( !Networking.IsHost || _falling ) return;

		_falling = true;
		_destroyAt = FallDuration;
		ApplyBreakStateLocally();
	}

	private void ApplyBreakStateLocally()
	{
		if ( _appliedBreakLocally ) return;
		_appliedBreakLocally = true;

		if ( SolidCollider.IsValid() )
			SolidCollider.IsTrigger = true;

		if ( TriggerCollider.IsValid() )
			TriggerCollider.Enabled = false;

		if ( SolidCollider.IsValid() )
		{
			_rigidbody = SolidCollider.GameObject.AddComponent<Rigidbody>();
			_rigidbody.MassOverride = FallMass;
			_rigidbody.MotionEnabled = true;
		}
	}
}
