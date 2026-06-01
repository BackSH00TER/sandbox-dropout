using Sandbox;

public sealed class KillZone : Component, Component.ITriggerListener
{
	[Property] public SceneFile SceneToLoad { get; set; }

	public void OnTriggerEnter( GameObject other )
	{
		if ( !Networking.IsHost )
			return;

		if ( !other.Tags.Has( "player" ) )
			return;

		Log.Info( $"KillZone on {GameObject.Name} triggered by {other.Name}, loading scene '{SceneToLoad}'." );
		var loadOptions = new SceneLoadOptions();
		loadOptions.SetScene( SceneToLoad );
		if ( !Game.ChangeScene( loadOptions ) )
		{
			Log.Error( $"KillZone on {GameObject.Name} failed to load scene '{SceneToLoad}'." );
		}
	}
}
