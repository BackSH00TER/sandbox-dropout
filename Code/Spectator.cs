using System.Collections.Generic;
using System.Linq;
using Sandbox;

/// <summary>
/// Per-player spectator component. While active, the player this component is
/// attached to (the "self" player) is hidden and their controller is disabled.
/// On the owning client, the main camera is then taken over and follows another
/// player (the "spectated" player). Other systems decide when to activate it —
/// for example, <see cref="GameManager"/> activates it on elimination.
/// </summary>
public sealed class Spectator : Component
{
    /// <summary>True while the self player is in spectator mode.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Display name of the player this client is currently spectating, or null if not spectating.</summary>
    public string SpectatingName => _spectatedPlayer.IsValid() ? _spectatedPlayer.Network?.Owner?.DisplayName ?? "Unknown" : null;

    // The player this component is attached to — the one we hide/disable on activate.
    private PlayerController _selfPlayer;

    // The other player the camera is currently following (only meaningful on the owning client).
    private PlayerController _spectatedPlayer;
    private int _spectatedIndex;

    protected override void OnStart()
    {
        _selfPlayer = GameObject.GetComponent<PlayerController>();
    }

    /// <summary>
    /// Activates spectator mode for the self player. Invoked on every client (e.g.
    /// from an RPC broadcast) so the body/controller are turned off everywhere; on
    /// the owning client the main camera is taken over and starts following another
    /// player. Idempotent — calling it again while already active is a no-op.
    /// </summary>
    public void Activate()
    {
        if ( IsActive ) return;
        IsActive = true;

        HideSelfBody();
        DisableSelfControl();

        if ( Network.IsOwner )
        {
            EnterSpectateMode();
        }
    }

    protected override void OnUpdate()
    {
        if ( !IsActive ) return;
        if ( !Network.IsOwner ) return;

        // Refresh the list each frame in case other players become unavailable while we're spectating.
        var others = GetAvailableTargets();

        if ( others.Count == 0 )
        {
            // Nothing left to spectate (e.g. the round is wrapping up).
            _spectatedPlayer = null;
            return;
        }

        // The player we were watching is no longer available: move to the next one.
        if ( !_spectatedPlayer.IsValid() || !others.Contains( _spectatedPlayer ) )
        {
            _spectatedIndex %= others.Count;
            _spectatedPlayer = others[_spectatedIndex];
        }

        if ( Input.Pressed( "Attack1" ) )
        {
            _spectatedIndex = (_spectatedIndex - 1 + others.Count) % others.Count;
            _spectatedPlayer = others[_spectatedIndex];
        }
        else if ( Input.Pressed( "Attack2" ) )
        {
            _spectatedIndex = (_spectatedIndex + 1) % others.Count;
            _spectatedPlayer = others[_spectatedIndex];
        }

        UpdateSpectatorCamera();
    }

    private void EnterSpectateMode()
    {
        var others = GetAvailableTargets();
        if ( others.Count > 0 )
        {
            _spectatedIndex = 0;
            _spectatedPlayer = others[0];
        }
    }

    private List<PlayerController> GetAvailableTargets()
    {
        // Available = every other player (not self) who isn't themselves spectating.
        return Scene.GetAllComponents<PlayerController>()
            .Where( other => other.IsValid() && other.GameObject != GameObject )
            .Where( other =>
            {
                var otherSpectator = other.GetComponent<Spectator>();
                return otherSpectator == null || !otherSpectator.IsActive;
            } )
            .ToList();
    }

    private void UpdateSpectatorCamera()
    {
        if ( !_spectatedPlayer.IsValid() ) return;

        var camera = Scene.Camera;
        if ( !camera.IsValid() ) return;

        var lookAt = _spectatedPlayer.WorldPosition + Vector3.Up * 60f;
        var back = _spectatedPlayer.WorldRotation.Forward * -220f;
        var up = Vector3.Up * 90f;
        var camPos = lookAt + back + up;

        camera.WorldPosition = camPos;
        camera.WorldRotation = Rotation.LookAt( (lookAt - camPos).Normal );
    }

    private void HideSelfBody()
    {
        foreach ( var renderer in GameObject.GetComponentsInChildren<ModelRenderer>( includeDisabled: false ) )
        {
            renderer.Enabled = false;
        }
    }

    private void DisableSelfControl()
    {
        if ( _selfPlayer.IsValid() )
        {
            _selfPlayer.UseInputControls = false;
            _selfPlayer.UseCameraControls = false;
            _selfPlayer.UseLookControls = false;
            _selfPlayer.UseAnimatorControls = false;
        }

        // Stop the body from re-triggering anything as it falls.
        GameObject.Tags.Remove( "player" );
    }
}
