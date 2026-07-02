using Sandbox;

/// <summary>
/// Plays a "woo" sound from the local player when they actually fall down to the
/// layer below — not when they're just jumping or hopping around. Detection is
/// owner-only (PlayerController.Velocity is owner-authoritative), the trigger
/// fans out to every client via RPC so the sound is heard positionally.
/// </summary>
public sealed class PlayerFallSound : Component, PlayerController.IEvents
{
    [Property] public PlayerController PlayerController { get; set; }
    [Property] public SoundEvent FallSound { get; set; }

    /// <summary>
    /// How far the player must descend from their airborne peak before the fall
    /// sound triggers. Filters small jumps and step-downs.
    /// </summary>
    public float FallTriggerDistance { get; set; } = 100f;

    private float _airbornePeakZ;
    private bool _isTrackingFall;
    private bool _hasPlayedFallSound;

    protected override void OnUpdate()
    {
        if ( GameObject.Network.Owner != Connection.Local ) return;
        if ( PlayerController == null ) return;

        if ( PlayerController.IsOnGround )
        {
            // Fresh airtime next time they leave the ground.
            _isTrackingFall = false;
            _hasPlayedFallSound = false;
            return;
        }

        // Track the highest point of this airtime so we can compare current Z
        // against the peak — a jump goes up first, so the drop from peak only
        // exceeds FallTriggerDistance if they actually fell off something.
        float currentZ = PlayerController.WorldPosition.z;
        if ( !_isTrackingFall )
        {
            _isTrackingFall = true;
            _airbornePeakZ = currentZ;
        }
        else if ( currentZ > _airbornePeakZ )
        {
            _airbornePeakZ = currentZ;
        }

        if ( _hasPlayedFallSound ) return;
        // Skip the upward arc of a jump.
        if ( PlayerController.Velocity.z >= 0f ) return;
        if ( _airbornePeakZ - currentZ < FallTriggerDistance ) return;

        _hasPlayedFallSound = true;
        BroadcastFallSound();
    }

    [Rpc.Broadcast]
    private void BroadcastFallSound()
    {
        if ( FallSound == null ) return;
        // PlaySound from the the GameObject so the sound follows the player as they fall.
        GameObject.PlaySound( FallSound );
    }

    // Stub — future hard-land impact thump keyed off `distance`.
    void PlayerController.IEvents.OnLanded( float distance, Vector3 impactVelocity )
    {

    }
}
