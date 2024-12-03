using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Burst;

//TODO: Cancle the current generation if the player moves too fast

[BurstCompile]
public class TerrainGeneratorNew : MonoBehaviour
{
    ChunkManager chunkManager;

    [SerializeField] Transform player;

    [SerializeField] GameObject chunkPrefab;

    Vector2Int playerChunkPrevious;

    List<ChunkDataSimplified> lastGeneratedMeshes = new List<ChunkDataSimplified>();

    [SerializeField] int LevelOfDetail = 4;
    [SerializeField] float updateFrequency = 1;

    void Start()
    {
        chunkManager = new ChunkManager(this, chunkPrefab);

        chunkManager.RequestNewObjectPool(1 + 8 * (LevelOfDetail + 1));

        StartCoroutine(UpdateTerrain());
    }

    IEnumerator UpdateTerrain()
    {
        while (true)
        {
            GenerateTerrainAroundPlayer(LevelOfDetail);

            yield return new WaitForSeconds(updateFrequency);
        }
    }

    void GenerateTerrainAroundPlayer(int LOD)
    {
        Vector2Int playerChunk = chunkManager.WorldPositionToChunkIndex(player.position);

        if (playerChunk == playerChunkPrevious)
        {
            return;
        }

        foreach (ChunkDataSimplified mesh in lastGeneratedMeshes)
        {
            chunkManager.DestroyChunk(mesh.position);
        }

        List<LODChunkRing> rings = new List<LODChunkRing>();

        for (int i = 0; i <= LOD; i++)
        {
            rings.Add(GenerateLODChunkRing(i, playerChunk));
        }

        GenerateLODRingMesh(rings);

        playerChunkPrevious = playerChunk;
    }

    void GenerateLODRingMesh(List<LODChunkRing> rings)
    {
        List<ChunkDataSimplified> generatedMeshes = new List<ChunkDataSimplified>();

        //Center chunk
        generatedMeshes.Add(new ChunkDataSimplified { position = chunkManager.WorldPositionToChunkIndex(player.position), LOD = 0 });

        foreach (LODChunkRing ring in rings)
        { 
            //Diagonal chunks
            generatedMeshes.Add(new ChunkDataSimplified { position = ring.chunk00, LOD = ring.LOD });
            generatedMeshes.Add(new ChunkDataSimplified { position = ring.chunk02, LOD = ring.LOD });
            generatedMeshes.Add(new ChunkDataSimplified { position = ring.chunk20, LOD = ring.LOD });
            generatedMeshes.Add(new ChunkDataSimplified { position = ring.chunk22, LOD = ring.LOD });

            //Straight chunks
            generatedMeshes.Add(new ChunkDataSimplified { position = ring.chunk01, LOD = ring.LOD });
            generatedMeshes.Add(new ChunkDataSimplified { position = ring.chunk10, LOD = ring.LOD });
            generatedMeshes.Add(new ChunkDataSimplified { position = ring.chunk12, LOD = ring.LOD });
            generatedMeshes.Add(new ChunkDataSimplified { position = ring.chunk21, LOD = ring.LOD });
        }

        chunkManager.GenerateChunkBatch(generatedMeshes);

        lastGeneratedMeshes = generatedMeshes;
    }

    int SizeOfChunkAtLODLevel(int LODLevel)
    {
        LODLevel = LODLevel < 0 ? 0 : LODLevel;
        
        return (int)Mathf.Pow(3, LODLevel);
    }

    LODChunkRing GenerateLODChunkRing(int LODLevel, Vector2Int playerPos)
    {
        LODChunkRing ring = new LODChunkRing();

        ring.LOD = LODLevel;

        //Diagonal chunks
        ring.chunk00 = new Vector2Int(1, -1);
        ring.chunk02 = new Vector2Int(-1, -1);
        ring.chunk20 = new Vector2Int(1, 1);
        ring.chunk22 = new Vector2Int(-1, 1);

        for (int i = 0; i < LODLevel; i++)
        {
            ring.chunk00 *= 3;
            ring.chunk00 += new Vector2Int(-1, -1);

            ring.chunk02 *= 3;
            ring.chunk02 += new Vector2Int(-1, -1);

            ring.chunk20 *= 3;
            ring.chunk20 += new Vector2Int(-1, -1);

            ring.chunk22 *= 3;
            ring.chunk22 += new Vector2Int(-1, -1);
        }

        //Straight chunks
        ring.chunk01 = new Vector2Int(ring.chunk00.x, ring.chunk00.y + SizeOfChunkAtLODLevel(LODLevel));
        ring.chunk10 = new Vector2Int(ring.chunk00.x - SizeOfChunkAtLODLevel(LODLevel), ring.chunk00.y);
        ring.chunk12 = new Vector2Int(ring.chunk02.x, ring.chunk02.y + SizeOfChunkAtLODLevel(LODLevel));
        ring.chunk21 = new Vector2Int(ring.chunk22.x + SizeOfChunkAtLODLevel(LODLevel), ring.chunk22.y);

        ring.chunk00 += playerPos;
        ring.chunk01 += playerPos;
        ring.chunk02 += playerPos;
        ring.chunk10 += playerPos;
        ring.chunk12 += playerPos;
        ring.chunk20 += playerPos;
        ring.chunk21 += playerPos;
        ring.chunk22 += playerPos;

        return ring;
    }
}

struct LODChunkRing
{
    public int LOD;

    public Vector2Int chunk00;
    public Vector2Int chunk01;
    public Vector2Int chunk02;
    public Vector2Int chunk10;
    public Vector2Int chunk12;
    public Vector2Int chunk20;
    public Vector2Int chunk21;
    public Vector2Int chunk22;
}

public struct ChunkDataSimplified
{
    public Vector2Int position;
    public int LOD;
}