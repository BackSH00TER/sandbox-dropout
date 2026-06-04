using Sandbox;

public sealed class TileManager : Component
{
	[Property] public GameObject TilePrefab { get; set; }
	[Property] public int Width { get; set; } = 10;
	[Property] public int Depth { get; set; } = 10;
	[Property] public int LayerCount { get; set; } = 3;
	[Property] public float LayerSpacing { get; set; } = 256f;
	[Property] public float Padding { get; set; } = 0f;
	[Property] public bool Centered { get; set; } = true;
	[Property] public bool TintLayers { get; set; } = true;
	public List<Vector3> AvailableSpawnLocations { get; private set; } = new();

	public void BuildGrid()
	{
		if ( !Networking.IsHost ) return;

		if ( !TilePrefab.IsValid() )
		{
			Log.Warning( $"Platform on {GameObject.Name} has no TilePrefab assigned." );
			return;
		}

		// Spawn the first tile so we can measure its real size, then use that as the cell spacing.
		var probe = SpawnTile( Vector3.Zero, "Tile_probe", parent: null );
		var size = GetTileSize( probe );
		probe.Destroy();

		float cellX = size.x + Padding;
		float cellY = size.y + Padding;
		float cellZ = size.z + LayerSpacing;

		var offset = Centered
			? new Vector3( -(Width - 1) * cellX * 0.5f, -(Depth - 1) * cellY * 0.5f, 0f )
			: Vector3.Zero;

		for ( int layer = 0; layer < LayerCount; layer++ )
		{
			// Each layer is parented under its own child GameObject for tidiness in the scene tree.
			// It must be NetworkSpawn'd so that when we parent network-spawned tiles under it,
			// clients can resolve the parent reference and the tiles actually appear.
			var layerTint = TintLayers ? RandomLayerColor() : Color.White;
			var layerGo = new GameObject( true, $"Layer_{layer}" );
			layerGo.SetParent( GameObject );
			layerGo.LocalPosition = new Vector3( 0f, 0f, -layer * cellZ );
			layerGo.NetworkSpawn();

			for ( int x = 0; x < Width; x++ )
			{
				for ( int y = 0; y < Depth; y++ )
				{
					var localPos = offset + new Vector3( x * cellX, y * cellY, 0f );
					AvailableSpawnLocations.Add( localPos );
					var tile = SpawnTile( localPos, $"Tile_{x}_{y}", parent: layerGo );

					if ( TintLayers )
					{
						foreach ( var renderer in tile.GetComponentsInChildren<ModelRenderer>() )
						{
							renderer.Tint = layerTint;
						}
					}
				}
			}
		}
	}

	public void ActivateGrid()
	{
		foreach ( var tile in GameObject.GetComponentsInChildren<Tile>() )
		{
			tile.SetTriggerEnabled( true );
		}
	}

	private static Color RandomLayerColor()
	{
		// Pick a vivid color by randomizing hue while keeping saturation/value high.
		float hue = Game.Random.Float( 0f, 360f );
		return new ColorHsv( hue, 0.6f, 1f ).ToColor();
	}

	private GameObject SpawnTile( Vector3 localPos, string name, GameObject parent )
	{
		var tile = TilePrefab.Clone( new CloneConfig
		{
			Parent = parent ?? GameObject,
			StartEnabled = false,
			Transform = new Transform( localPos )
		} );
		tile.Name = name;

		// Always NetworkSpawn so the tile exists on every client, not just the host.
		// The probe tile spawned during measurement is host-only and destroyed immediately,
		// so it doesn't need to be networked — but networking it is harmless either way.
		tile.NetworkSpawn();

		return tile;
	}

	private Vector3 GetTileSize( GameObject tile )
	{
		BBox? bounds = null;

		foreach ( var renderer in tile.GetComponentsInChildren<ModelRenderer>() )
		{
			var b = renderer.Bounds;
			bounds = bounds.HasValue ? bounds.Value.AddBBox( b ) : b;
		}

		if ( !bounds.HasValue )
		{
			Log.Warning( $"Platform: couldn't measure tile size, falling back to 64." );
			return new Vector3( 64f, 64f, 64f );
		}

		return bounds.Value.Size;
	}




}
