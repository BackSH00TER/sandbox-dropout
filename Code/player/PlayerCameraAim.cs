using System.Threading.Tasks;
using Sandbox;

/// <summary>
/// On spawn, points the local player's camera at the countdown drone so players
/// can see the pre-game countdown immediately. Runs only on the owning client —
/// PlayerController.EyeAngles is owner-authoritative.
/// </summary>
public sealed class PlayerCameraAim : Component
{
    [Property] public PlayerController PlayerController { get; set; }

    // Drone spawn message can race the player spawn on remote clients; poll briefly.
    [Property] public float MaxWaitSeconds { get; set; } = 1f;

    protected override void OnStart()
    {
        _ = AimAtDroneAsync();
    }

    private async Task AimAtDroneAsync()
    {
        if ( GameObject.Network.Owner != Connection.Local ) return;

        PlayerController pc = PlayerController ?? GetComponent<PlayerController>();
        if ( pc == null ) return;

        TimeUntil giveUp = MaxWaitSeconds;
        while ( CountdownDrone.Current == null && (float)giveUp > 0f )
        {
            await Task.DelayRealtimeSeconds( 0.05f );
            if ( !this.IsValid() ) return;
        }

        CountdownDrone drone = CountdownDrone.Current;
        if ( drone == null ) return;

        Vector3 targetPosition = drone.WorldPosition + Vector3.Down * 150f;
        Vector3 dir = (targetPosition - pc.WorldPosition).Normal;
        if ( dir.LengthSquared < 0.0001f ) return;

        pc.EyeAngles = Rotation.LookAt( dir, Vector3.Up ).Angles();
    }
}
