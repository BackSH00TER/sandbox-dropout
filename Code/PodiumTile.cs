/// <summary>
/// Lightweight stand-in for a regular <see cref="Tile"/> during the results phase. Sits on
/// the same GameObject that previously held a Tile component (with that Tile component
/// disabled), tints the model gold, and undoes any in-progress wobble / depression from
/// the now-disabled Tile so the podium looks clean.
///
/// Has no break logic, no sync state \u2014 it's added locally on each client via the
/// <c>BroadcastConvertToPodium</c> RPC in <see cref="GameManager"/>.
/// </summary>
public sealed class PodiumTile : Component
{
    [Property] public Color Tint { get; set; } = Color.Parse( "#FFD700" ) ?? Color.Yellow;

    protected override void OnStart()
    {
        foreach ( var renderer in GameObject.GetComponentsInChildren<ModelRenderer>() )
        {
            renderer.Tint = Tint;
        }

        // Tile drives wobble on the root and depression on a child model. Reset both so the
        // podium isn't stuck mid-animation when the host Tile component gets disabled.
        WorldRotation = WorldRotation.Angles().WithRoll( 0f ).WithPitch( 0f ).ToRotation();
        foreach ( var child in GameObject.Children )
        {
            child.LocalPosition = Vector3.Zero;
            child.LocalRotation = Rotation.Identity;
        }
    }
}
