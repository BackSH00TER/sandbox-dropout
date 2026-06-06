using System;

/// <summary>
/// Scene-level spectator camera. Lives on the main camera GameObject. Two modes:
///   * Spectator — entered via <see cref="Activate"/> when the local player is eliminated.
///     Free orbit driven by mouse look, LMB/RMB cycles between remaining players.
///   * Winner focus — entered automatically on every client when
///     <see cref="GameManager.IsShowingResults"/> is true. No input: slow auto yaw drift
///     and a gentle dolly-in over the results duration. Overrides Spectator while active.
/// </summary>
public sealed class SpectatorMode : Component
{
    /// <summary>Scene-level singleton so other components (GameManager, HUD) can find us.</summary>
    public static SpectatorMode Current { get; private set; }

    /// <summary>True once the local client has entered spectate mode.</summary>
    public bool IsActive { get; private set; }

    /// <summary>True while the winner-focus cam is driving the camera (during the results phase).</summary>
    public bool IsFocusingWinner { get; private set; }

    /// <summary>Display name of the player currently being spectated, or null.</summary>
    public string SpectatingName => _spectatedPlayer.IsValid() ? _spectatedPlayer.Network?.Owner?.DisplayName ?? "Unknown" : null;

    private PlayerController _spectatedPlayer;
    private int _spectatedIndex;

    // Orbit angles around the spectated player, driven by mouse look.
    private Angles _orbitAngles = new( 20f, 0f, 0f );
    private const float OrbitDistance = 220f;
    private const float OrbitHeight = 90f;
    private const float MinPitch = -80f;
    private const float MaxPitch = 80f;

    // Winner-focus cam tuning. Camera starts directly in front of the winner (looking
    // at their face) and slowly sways side-to-side via a sine wave for a cinematic feel.
    // Distance + height lerp from "start" to "end" across the full results duration
    // for a subtle dolly-in. Height is relative to the lookAt point (winner chest level),
    // so 0 = straight-on, negative = camera below winner looking up.
    private const float WinnerPitch = 0f;
    private const float WinnerDistanceStart = 140f;
    private const float WinnerDistanceEnd = 110f;
    private const float WinnerHeightStart = 10f;
    private const float WinnerHeightEnd = -15f;
    private const float WinnerSwayAmplitude = 45f;  // degrees off-center each direction
    private const float WinnerSwayPeriod = 8f;      // seconds for one full left-right-back cycle

    private float _winnerBaseYaw;
    private float _winnerStartTime;
    private bool _hasCapturedWinnerYaw;

    protected override void OnEnabled()
    {
        Current = this;
    }

    protected override void OnDisabled()
    {
        if ( Current == this )
            Current = null;
    }

    /// <summary>
    /// Enter spectate mode on this client. Idempotent — extra calls are no-ops.
    /// </summary>
    public void Activate()
    {
        if ( IsActive ) return;
        IsActive = true;

        var targets = GetAvailableTargets();
        if ( targets.Count > 0 )
        {
            _spectatedIndex = 0;
            _spectatedPlayer = targets[0];
        }
    }

    protected override void OnUpdate()
    {
        // Winner-focus mode overrides everything else while the results phase is up.
        var gameManager = GameManager.Current;
        var winner = (gameManager != null && gameManager.IsShowingResults) ? gameManager.Winner : null;

        if ( winner.IsValid() )
        {
            IsFocusingWinner = true;
            UpdateWinnerFocus( winner );
            return;
        }
        IsFocusingWinner = false;
        _hasCapturedWinnerYaw = false;

        if ( !IsActive ) return;

        var targets = GetAvailableTargets();
        if ( targets.Count == 0 )
        {
            _spectatedPlayer = null;
            return;
        }

        // The player we were watching is gone: roll forward to the next one.
        if ( !_spectatedPlayer.IsValid() || !targets.Contains( _spectatedPlayer ) )
        {
            _spectatedIndex %= targets.Count;
            _spectatedPlayer = targets[_spectatedIndex];
        }

        if ( Input.Pressed( "Attack1" ) )
        {
            _spectatedIndex = (_spectatedIndex - 1 + targets.Count) % targets.Count;
            _spectatedPlayer = targets[_spectatedIndex];
        }
        else if ( Input.Pressed( "Attack2" ) )
        {
            _spectatedIndex = (_spectatedIndex + 1) % targets.Count;
            _spectatedPlayer = targets[_spectatedIndex];
        }

        UpdateCamera();
    }

    private List<PlayerController> GetAvailableTargets()
    {
        // All remaining players in the scene. Eliminated players are destroyed by
        // PlayerManager so they naturally drop out of this list.
        return Scene.GetAllComponents<PlayerController>()
            .Where( p => p.IsValid() )
            .ToList();
    }

    private void UpdateCamera()
    {
        if ( !_spectatedPlayer.IsValid() ) return;

        var camera = Scene.Camera;
        if ( !camera.IsValid() ) return;

        var look = Input.AnalogLook;
        _orbitAngles.yaw += look.yaw;
        _orbitAngles.pitch = Math.Clamp( _orbitAngles.pitch + look.pitch, MinPitch, MaxPitch );

        var lookAt = _spectatedPlayer.WorldPosition + Vector3.Up * 60f;
        var orbitRotation = _orbitAngles.ToRotation();
        // Sit OrbitDistance units behind the orbit's forward direction, then lift by OrbitHeight for a slight top-down angle.
        var camPos = lookAt + orbitRotation.Forward * -OrbitDistance + Vector3.Up * OrbitHeight;

        camera.WorldPosition = camPos;
        camera.WorldRotation = Rotation.LookAt( (lookAt - camPos).Normal );
    }

    private void UpdateWinnerFocus( GameObject winner )
    {
        var camera = Scene.Camera;
        if ( !camera.IsValid() ) return;

        // First frame in winner-focus: seed yaw from the winner's facing direction so the
        // camera starts directly in front of them, looking at their face. Also reset the
        // sway clock so the sine wave begins centered (sin(0) = 0).
        if ( !_hasCapturedWinnerYaw )
        {
            _hasCapturedWinnerYaw = true;
            // PlayerController is disabled on freeze — pass true to GetComponent so we can
            // still find it. Renderer is the SkinnedModelRenderer whose GameObject rotation
            // is the visual facing.
            var pc = winner.GetComponent<PlayerController>( true );
            var rendererRotation = (pc?.Renderer.IsValid() == true) ? pc.Renderer.WorldRotation : winner.WorldRotation;
            _winnerBaseYaw = rendererRotation.Yaw() + 180f;
            _winnerStartTime = Time.Now;
        }

        float elapsed = Time.Now - _winnerStartTime;
        float swayPhase = (elapsed / WinnerSwayPeriod) * MathF.PI * 2f;
        float yaw = _winnerBaseYaw + MathF.Sin( swayPhase ) * WinnerSwayAmplitude;

        // Dolly-in: lerp distance + height across the results window using the synced timer.
        var remaining = (float)GameManager.Current.ResultsTimer;
        var t = Math.Clamp( 1f - (remaining / GameManager.ResultsDuration), 0f, 1f );
        var distance = MathX.Lerp( WinnerDistanceStart, WinnerDistanceEnd, t );
        var height = MathX.Lerp( WinnerHeightStart, WinnerHeightEnd, t );

        var rotation = new Angles( WinnerPitch, yaw, 0f ).ToRotation();
        var lookAt = winner.WorldPosition + Vector3.Up * 60f;
        var camPos = lookAt + rotation.Forward * -distance + Vector3.Up * height;

        camera.WorldPosition = camPos;
        camera.WorldRotation = Rotation.LookAt( (lookAt - camPos).Normal );
    }
}
