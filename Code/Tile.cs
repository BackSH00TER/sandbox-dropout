using System;
using Sandbox;

public sealed class Tile : Component, Component.ITriggerListener
{
	/// <summary>Seconds between a player stepping on the tile and it breaking away.</summary>
	[Property] public float BreakDelay { get; set; } = 1.0f;

	/// <summary>Seconds after breaking before the tile GameObject is destroyed.</summary>
	[Property] public float FallDuration { get; set; } = 2.0f;

	/// <summary>Maximum wobble roll angle (degrees) just before the tile breaks.</summary>
	[Property] public float WobbleAngle { get; set; } = 5.0f;

	/// <summary>How fast the wobble oscillates (radians/sec fed into Sin).</summary>
	[Property] public float WobbleSpeed { get; set; } = 25.0f;

	/// <summary>Mass applied to the rigidbody when the tile breaks, so gravity has something to act on.</summary>
	[Property] public float FallMass { get; set; } = 100f;

	/// <summary>How fast the white-flash pulses as the tile is about to break (Hz-ish).</summary>
	[Property] public float FlashSpeed { get; set; } = 12f;

	/// <summary>How far down the visual model dips while a player is standing on it (local units).</summary>
	[Property] public float DepressDepth { get; set; } = 3f;

	/// <summary>How quickly the model lerps toward its depressed/rest position.</summary>
	[Property] public float DepressSpeed { get; set; } = 25f;

	/// <summary>Non-trigger collider that the player stands on. Switched to a trigger on break so the player falls through.</summary>
	[Property] public Collider SolidCollider { get; set; }

	/// <summary>Rigidbody created at break-time on the SolidCollider's GameObject so the tile can fall. Kept off the prefab so it doesn't fight the parent transform during spawn.</summary>
	private Rigidbody Rigidbody;

	/// <summary>Trigger collider that detects the player stepping onto the tile.</summary>
	[Property] public Collider TriggerCollider { get; set; }

	/// <summary>Model renderer used for the white-flash effect and depression bob. Should be on a child GameObject so we can move it without moving the collider.</summary>
	[Property] public ModelRenderer Model { get; set; }

	/// <summary>The tile prefab root GameObject — the thing to destroy when the tile finishes falling. Wire this up in the prefab inspector.</summary>
	[Property] public GameObject TileRoot { get; set; }

	/// <summary>True once a player has stepped on the tile and the break timer has started. Synced from host so all clients show the wobble/flash in lockstep.</summary>
	[Sync] private bool _triggered { get; set; } = false;

	/// <summary>True once BreakTile() has run and the tile is physically falling. Synced from host so every client applies the break-state collider/rigidbody changes at the same moment.</summary>
	[Sync] private bool _falling { get; set; } = false;

	/// <summary>Counts down from BreakDelay; when it hits 0 the tile breaks.</summary>
	private TimeUntil _breakAt;

	/// <summary>Counts down from FallDuration after breaking; when it hits 0 the tile is destroyed.</summary>
	private TimeUntil _destroyAt;

	/// <summary>The tile's original world rotation, used as the wobble pivot.</summary>
	private Rotation _restRotation;

	/// <summary>The Model GameObject's resting local position, used as the depression pivot.</summary>
	private Vector3 _modelRestPosition;

	/// <summary>Number of player colliders currently overlapping the trigger. Host-only — we only count on the host since it's the one making break decisions.</summary>
	private int _playersOnTile = 0;

	/// <summary>Tracks whether this client has already applied the break-state changes (IsTrigger/Enabled flips, rigidbody add) so we only do it once when _falling becomes true.</summary>
	private bool _appliedBreakLocally = false;

	/// <summary>The Model's base tint at startup, used as the "non-flashing" colour.</summary>
	private Color _baseTint = Color.White;

	/// <summary>Random phase offset (radians) so each tile wobbles out of sync with its neighbours.</summary>
	private float _wobblePhase;

	/// <summary>Per-tile multiplier on WobbleSpeed so tiles drift in and out of phase over time instead of all oscillating at exactly the same rate.</summary>
	private float _wobbleSpeedJitter = 1f;

	protected override void OnStart()
	{
		// Wobble is applied to TileRoot (the prefab root) because the Tile component lives on a
		// child GameObject — rotating `this.WorldRotation` would only spin the invisible trigger
		// collider and leave the model branch untouched.
		_restRotation = TileRoot.IsValid() ? TileRoot.WorldRotation : WorldRotation;

		// Randomize per-tile so the platform doesn't wobble in lockstep. Seed off the GameObject's
		// id so every client picks the same offsets for the same tile and the synced WorldRotation
		// doesn't fight the local wobble computation.
		var rng = new Random( HashCode.Combine( GameObject.Id ) );
		_wobblePhase = (float)rng.NextDouble() * MathF.PI * 2f;
		_wobbleSpeedJitter = 0.75f + (float)rng.NextDouble() * 0.5f; // 0.75x .. 1.25x

		if ( Model.IsValid() )
		{
			_modelRestPosition = Model.LocalPosition;
			_baseTint = Model.Tint;
		}
	}

	protected override void OnUpdate()
	{
		// === HOST-ONLY decision logic ===
		if ( Networking.IsHost )
		{
			// Start the break sequence only once the match is actually live, even if a player
			// has been standing on us during the countdown.
			if ( !_triggered && !_falling && _playersOnTile > 0 && GameState.IsPlaying )
			{
				_triggered = true;
				_breakAt = BreakDelay;
			}

			if ( _triggered && !_falling && _breakAt <= 0 )
			{
				BreakTile();
			}

			if ( _falling && _destroyAt <= 0 )
			{
				TileRoot?.Destroy();
			}
		}

		// === Per-client visual + physics state mirroring ===
		// When _falling syncs to true on a client, apply the same collider/rigidbody changes
		// locally so the player can fall through and the tile visibly falls.
		if ( _falling && !_appliedBreakLocally )
		{
			ApplyBreakStateLocally();
			_appliedBreakLocally = true;
		}

		if ( _triggered && !_falling )
		{
			float remaining = MathX.Clamp( (float)_breakAt / BreakDelay, 0f, 1f );
			float intensity = 1f - remaining;
			float angle = MathF.Sin( Time.Now * WobbleSpeed * _wobbleSpeedJitter + _wobblePhase ) * WobbleAngle * intensity;
			var wobbleTarget = TileRoot.IsValid() ? TileRoot : GameObject;
			wobbleTarget.WorldRotation = _restRotation * Rotation.FromRoll( angle );

			// White flash that pulses faster/brighter as the tile is about to break.
			if ( Model.IsValid() )
			{
				float pulse = (MathF.Sin( Time.Now * FlashSpeed ) + 1f) * 0.5f; // 0..1
				float flashAmount = pulse * intensity;
				Model.Tint = Color.Lerp( _baseTint, Color.White, flashAmount );
			}
		}

		// Depression bob — only the visual Model moves, the colliders stay put.
		if ( Model.IsValid() && !_falling )
		{
			var target = _playersOnTile > 0
				? _modelRestPosition + Vector3.Down * DepressDepth
				: _modelRestPosition;

			Model.LocalPosition = Vector3.Lerp( Model.LocalPosition, target, Time.Delta * DepressSpeed );
		}
	}

	public void OnTriggerEnter( Collider other )
	{
		// Only the host counts players — it's the only one making break decisions.
		if ( !Networking.IsHost ) return;
		if ( !other.Tags.Has( "player" ) ) return;

		_playersOnTile++;
	}

	public void OnTriggerExit( Collider other )
	{
		if ( !Networking.IsHost ) return;
		if ( !other.Tags.Has( "player" ) ) return;

		_playersOnTile = Math.Max( 0, _playersOnTile - 1 );
	}

	/// <summary>Host-only: mark this tile as broken. The synced _falling flag is what makes every client apply the visual/physics changes.</summary>
	public void BreakTile()
	{
		if ( !Networking.IsHost ) return;
		if ( _falling ) return;

		_falling = true;
		_destroyAt = FallDuration;
	}

	/// <summary>Runs on every client when _falling becomes true. Converts the solid collider to a trigger, disables the player-detect trigger, and adds a Rigidbody so the tile falls. The host's transform syncs to clients so all clients see the same falling motion.</summary>
	private void ApplyBreakStateLocally()
	{
		if ( SolidCollider.IsValid() )
			SolidCollider.IsTrigger = true;

		if ( TriggerCollider.IsValid() )
			TriggerCollider.Enabled = false;

		// Only the host's rigidbody actually simulates — its world transform syncs to clients
		// through the tile's network object. Adding a rigidbody on the client too would let
		// physics fight the synced transform.
		if ( Networking.IsHost && SolidCollider.IsValid() )
		{
			Rigidbody = SolidCollider.GameObject.AddComponent<Rigidbody>();
			Rigidbody.MassOverride = FallMass;
			Rigidbody.MotionEnabled = true;
		}
	}
}
