using System;
using Sandbox;

/// <summary>
/// Visual pre-game countdown drone. Holds the drone's tuning props and acts as a
/// scene singleton (<see cref="Current"/>) so the player camera and the world-space
/// hud panels can find it without serialized refs. The hud reads the countdown
/// value directly from <see cref="GameManager.CountdownTimer"/> on demand — same
/// pattern as <c>LobbyBoard</c> — so there's no per-frame work or [Sync] churn.
/// </summary>
public sealed class CountdownDrone : Component
{
    [Property] public GameObject Propeller { get; set; }

    [Property] public float PropellerSpinDegPerSec { get; set; } = 1152f;
    [Property] public float BounceAmplitude { get; set; } = 3f;
    [Property] public float BounceFrequency { get; set; } = 2.5f;
    [Property] public float LiftSpeed { get; set; } = 200f;
    [Property] public float DespawnDelay { get; set; } = 5f;

    public static CountdownDrone Current { get; private set; }

    /// <summary>
    /// Whole seconds remaining on the pre-game countdown. Returns 0 once the timer
    /// has hit zero ("GO!" state), or -1 if there's no live <see cref="GameManager"/>.
    /// </summary>
    public int DisplaySeconds
    {
        get
        {
            GameManager gm = GameManager.Current;
            if ( gm == null ) return -1;
            float remaining = (float)gm.CountdownTimer;
            if ( remaining <= 0f ) return 0;
            return Math.Max( 1, (int)Math.Ceiling( remaining ) );
        }
    }

    private Vector3 _restPosition;
    private TimeUntil _despawnAt;
    private bool _hasStartedLiftoff;

    protected override void OnEnabled()
    {
        Current = this;
        _restPosition = LocalPosition;
    }

    protected override void OnDisabled()
    {
        if ( Current == this )
            Current = null;
    }

    protected override void OnUpdate()
    {
        if ( !_hasStartedLiftoff && DisplaySeconds == 0 )
        {
            _hasStartedLiftoff = true;
            _despawnAt = DespawnDelay;
        }

        if ( _hasStartedLiftoff )
        {
            // Move upwards and despawn after the delay once the countdown hits zero
            UpdateLiftoff();
        }
        else
        {
            // Hover in place bouncing up and down while the countdown is active
            float bounce = MathF.Sin( Time.Now * BounceFrequency * MathF.Tau ) * BounceAmplitude;
            LocalPosition = _restPosition + Vector3.Up * bounce;
        }

        if ( Propeller != null )
        {
            Propeller.LocalRotation *= Rotation.FromYaw( PropellerSpinDegPerSec * Time.Delta );
        }
    }

    private void UpdateLiftoff()
    {
        float elapsed = DespawnDelay - (float)_despawnAt;
        LocalPosition = _restPosition + Vector3.Up * (LiftSpeed * elapsed);

        if ( _despawnAt <= 0f )
        {
            GameObject.Destroy();
        }
    }
}
