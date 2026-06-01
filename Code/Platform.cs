using Sandbox;
using System.Linq;

public sealed class Platform : Component
{
    /// <summary>The tile prefab to clone for each grid cell.</summary>
    [Property] public GameObject TilePrefab { get; set; }

    /// <summary>Number of tiles along the X axis.</summary>
    [Property] public int Width { get; set; } = 10;

    /// <summary>Number of tiles along the Y axis.</summary>
    [Property] public int Depth { get; set; } = 10;

    /// <summary>Number of stacked tile layers (floors) to build, each below the last.</summary>
    [Property] public int LayerCount { get; set; } = 3;

    /// <summary>Vertical gap (in units) between layers, added on top of the tile's own height.</summary>
    [Property] public float LayerSpacing { get; set; } = 256f;

    /// <summary>Extra space (in units) added between tiles on top of their measured size. 0 = tiles touch edge-to-edge.</summary>
    [Property] public float Padding { get; set; } = 0f;

    /// <summary>If true, spawn the grid centered on this GameObject. Otherwise it grows from the origin outward in +X/+Y.</summary>
    [Property] public bool Centered { get; set; } = true;

    /// <summary>If true, each layer gets a random color tint applied to all its tiles.</summary>
    [Property] public bool TintLayers { get; set; } = true;

    protected override void OnStart()
    {
        // Tiles are networked objects spawned by the host. Clients receive them via network sync
        // — we must NOT have every client spawn its own local grid, or each client ends up
        // simulating a separate copy of the platform.
        if ( !Networking.IsHost ) return;

        BuildGrid();
    }

    /// <summary>Tear down any existing tile layers and build a fresh grid. Safe to call at runtime to reset the map. Host-only.</summary>
    public void Rebuild()
    {
        if ( !Networking.IsHost ) return;

        // Destroy any previously-spawned layer containers so we don't pile new tiles on top of old ones.
        var existing = GameObject.Children.Where( c => c.IsValid() && c.Name.StartsWith( "Layer_" ) ).ToList();
        foreach ( var layer in existing )
        {
            layer.Destroy();
        }

        BuildGrid();
    }

    public void BuildGrid()
    {
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
            var layerGo = new GameObject( true, $"Layer_{layer}" );
            layerGo.SetParent( GameObject );
            layerGo.LocalPosition = new Vector3( 0f, 0f, -layer * cellZ );
            layerGo.NetworkSpawn();

            var layerTint = TintLayers ? RandomLayerColor() : Color.White;

            for ( int x = 0; x < Width; x++ )
            {
                for ( int y = 0; y < Depth; y++ )
                {
                    var localPos = offset + new Vector3( x * cellX, y * cellY, 0f );
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
            StartEnabled = true,
            Transform = new Transform( localPos )
        } );
        tile.Name = name;

        // Always NetworkSpawn so the tile exists on every client, not just the host.
        // The probe tile spawned during measurement is host-only and destroyed immediately,
        // so it doesn't need to be networked — but networking it is harmless either way.
        tile.NetworkSpawn();

        return tile;
    }

    /// <summary>Measure a tile's bounds using whatever renderers it has.</summary>
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

