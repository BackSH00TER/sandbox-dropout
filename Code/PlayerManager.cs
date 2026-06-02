using Sandbox;

public sealed class PlayerManager : Component, Component.INetworkListener
{
	[Property] public GameObject PlayerPrefab { get; set; }
	[Property] public TileManager TileManager { get; set; }

	/// <summary>
	/// Spawns player characters at random tile positions. Called by GameManager when the game starts.
	/// </summary>
	public void SpawnPlayers()
	{
		if ( !Networking.IsHost ) return;

		var clientsToSpawn = Connection.All;
		var AvailableSpawnPositions = TileManager.AvailableSpawnLocations;
		var randomSeed = System.DateTime.Now.Millisecond;
		Sandbox.Game.SetRandomSeed( randomSeed );

		foreach ( var client in clientsToSpawn )
		{
			var selectedPosition = Sandbox.Game.Random.FromList( AvailableSpawnPositions );
			AvailableSpawnPositions.Remove( selectedPosition );
			var newTransform = new Transform( selectedPosition, Rotation.Identity, Vector3.One );
			var player = PlayerPrefab.Clone( newTransform, name: $"Player - {client.Name}" );
			player.GetComponent<PlayerController>().UseInputControls = false;
			player.NetworkSpawn( client );
		}
	}

	public void EnablePlayersInput()
	{
		EnablePlayersInputNetwork();
	}

	[Rpc.Broadcast]
	private void EnablePlayersInputNetwork()
	{
		foreach ( var pc in Scene.GetAllComponents<PlayerController>() )
		{
			pc.UseInputControls = true;
		}
	}
}
