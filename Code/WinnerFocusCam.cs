using System;

/// <summary>
/// Cinematic camera that takes over during the post-game results phase. Lives on the
/// main camera GameObject alongside <see cref="SpectatorMode"/>. Activates on every
/// client whenever <see cref="GameManager.IsShowingResults"/> is true and a Winner is set.
///
/// Behavior: starts in front of the winner (looking at their face), sways gently
/// side-to-side, and dollies in over the results duration for a hero-shot.
/// </summary>
public sealed class WinnerFocusCam : Component
{
    // Dolly distance from winner in units; lerps Start -> End across the results window.
    private const float DistanceStart = 140f;
    private const float DistanceEnd = 110f;

    // Height is relative to the lookAt point (winner chest level): 0 = straight-on,
    // negative = camera below winner looking up. Lerps Start -> End across the results window.
    private const float HeightStart = 10f;
    private const float HeightEnd = -15f;

    // A simple camera sweep left/right of the winner.
    private const float SwayAmplitude = 15f;   // degrees off-center each direction
    private const float SwayPeriod = 8f;       // seconds for one full left-right-back cycle

    private float _baseYaw;
    private float _startTime;
    private bool _hasCapturedYaw;

    protected override void OnUpdate()
    {
        var gameManager = GameManager.Current;
        var winner = (gameManager != null && gameManager.IsShowingResults) ? gameManager.Winner : null;

        if ( !winner.IsValid() )
        {
            _hasCapturedYaw = false;
            return;
        }

        var camera = Scene.Camera;
        if ( !camera.IsValid() ) return;

        if ( !_hasCapturedYaw )
        {
            _hasCapturedYaw = true;
            // PlayerController is disabled on freeze — pass true to GetComponent so we can
            // still find it. Renderer is the SkinnedModelRenderer whose GameObject rotation
            // is the visual facing.
            var pc = winner.GetComponent<PlayerController>( true );
            var rendererRotation = (pc?.Renderer.IsValid() == true) ? pc.Renderer.WorldRotation : winner.WorldRotation;
            _baseYaw = rendererRotation.Yaw() + 180f;
            _startTime = Time.Now;
        }

        float elapsed = Time.Now - _startTime;
        float swayPhase = (elapsed / SwayPeriod) * MathF.PI * 2f;
        float yaw = _baseYaw + MathF.Sin( swayPhase ) * SwayAmplitude;

        // Dolly-in: lerp distance + height across the results window using the synced timer.
        var remaining = (float)gameManager.ResultsTimer;
        var t = Math.Clamp( 1f - (remaining / GameManager.ResultsDuration), 0f, 1f );
        var distance = MathX.Lerp( DistanceStart, DistanceEnd, t );
        var height = MathX.Lerp( HeightStart, HeightEnd, t );

        var rotation = new Angles( 0f, yaw, 0f ).ToRotation();
        var lookAt = winner.WorldPosition + Vector3.Up * 60f;
        var camPos = lookAt + rotation.Forward * -distance + Vector3.Up * height;

        camera.WorldPosition = camPos;
        camera.WorldRotation = Rotation.LookAt( (lookAt - camPos).Normal );
    }
}
