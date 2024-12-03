using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using Unity.Burst;
using System.Numerics;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile]
public class ChunkManager
{
    ChunkData centerChunk;

    MonoBehaviour monoBehaviour;

    GameObject chunkPrefab;

    Queue<ChunkGenerationTask> chunkGenerationQueue = new Queue<ChunkGenerationTask>();
    Queue<Vector2Int> placementQueue = new Queue<Vector2Int>();

    HeightmapGenerator heightmapGenerator = new HeightmapGenerator();

    public ChunkManager(MonoBehaviour mB, GameObject cP)
    {
        centerChunk = new ChunkData();
        monoBehaviour = mB;
        chunkPrefab = cP;

        monoBehaviour.StartCoroutine(UpdateTerrain());
    }
    
    public void DestroyChunk(Vector2Int position)
    {
        FreeChunk(position);
    }

    public void GenerateNewChunk(Vector2Int chunkIndex, Vector2Int LOD)
    {
        chunkGenerationQueue.Enqueue(new ChunkGenerationTask { chunkIndex = chunkIndex, LOD = LOD });
    }

    public void GenerateChunkBatch(List<ChunkDataSimplified> chunks)
    {
        monoBehaviour.StartCoroutine(GenerateBatched(chunks));
    }

    IEnumerator GenerateBatched(List<ChunkDataSimplified> chunks)
    {
        chunkGenerationQueue.Clear();
        
        while (placementQueue.Count > 0)
        {
            yield return null;
        }

        for (int i = 0; i < chunks.Count; i ++)
        {
            chunkGenerationQueue.Enqueue(new ChunkGenerationTask { chunkIndex = chunks[i].position, LOD = new Vector2Int(SizeOfChunkAtLODLevel(chunks[i].LOD), SizeOfChunkAtLODLevel(chunks[i].LOD)) });
        }
    }

    IEnumerator UpdateTerrain()
    {
        while (true)
        {
            while (chunkGenerationQueue.Count > 0)
            {
                ChunkGenerationTask task = chunkGenerationQueue.Dequeue();

                ChunkData chunk = MovePointerToChunk(task.chunkIndex);

                chunk.mesh = null;

                if (chunk.LOD != task.LOD || chunk.mesh == null) //Only generate if the stored mesh doesn't match the requested mesh
                {
                    monoBehaviour.StartCoroutine(GenerateMesh(task.chunkIndex, task.LOD, chunk));
                }

                chunk.LOD = task.LOD;

                placementQueue.Enqueue(task.chunkIndex);

                yield return null;
            }

            while (placementQueue.Count > 0)
            {
                Vector2Int chunkIndex = placementQueue.Dequeue();

                ChunkData chunk = MovePointerToChunk(chunkIndex);

                monoBehaviour.StartCoroutine(PlaceMeshIntoWorld(chunkIndex, chunk));
            }

            yield return null;
        }
    }

    IEnumerator GenerateMesh(Vector2Int chunkIndex, Vector2Int LOD, ChunkData chunk)
    {    
        //Generate heightmap
        NativeArray<float> heightmap = new NativeArray<float>(255 * 255, Allocator.Persistent);

        chunk.heightmap = heightmap;

        JobHandle jobHandle = new JobHandle();

        foreach (bool i in heightmapGenerator.GenerateHeightmap(chunkIndex, LOD, chunk.heightmap, jobHandle)) //Wait for the heightmap to be generated
        {
            if (i)
            {
                break;
            }
            yield return null;
        }

        chunk.mesh = new MeshGenerator();
        chunk.mesh.mesh = new Mesh();

        //Build a mesh from the heightmap
        chunk.mesh = MeshManager.GenerateMeshFromHeightmap(chunk.heightmap, chunk.mesh, new Vector2Int(100 * LOD.x, 100 * LOD.y), LOD, 1000, monoBehaviour, jobHandle);
    }

    IEnumerator PlaceMeshIntoWorld(Vector2Int chunkIndex, ChunkData chunk)
    {
        while (chunk.mesh == null)
        {
            yield return null;
        }

        while (!(chunk.mesh.meshDataFinished && chunk.mesh.physicsDataFinished)) //Wait for the mesh to be generated
        {
            yield return null;
        }

        GameObject chunkObject = RequestFreeChunkObject();

        if (chunkObject == null)
        {
            UnityEngine.Debug.LogError("Chunk object requested from empty pool.");
            yield break;
        }

        if (chunk.LOD.x == 1 && chunk.LOD.y == 1)
        {
            MeshCollider collider = chunkObject.GetComponent<MeshCollider>();
            
            collider.enabled = true;
            collider.sharedMesh = chunk.mesh.mesh;
        }
        else
        {
            chunkObject.GetComponent<MeshCollider>().enabled = false;
        }

        chunkObject.GetComponent<MeshFilter>().mesh = chunk.mesh.mesh;
        chunkObject.transform.position = new UnityEngine.Vector3(chunkIndex.x * 100, 0, chunkIndex.y * 100);
        chunkObject.name = "Chunk " + chunkIndex.x + ", " + chunkIndex.y;
    }

    public Vector2Int WorldPositionToChunkIndex(UnityEngine.Vector3 worldPosition)
    {
        return new Vector2Int(Mathf.FloorToInt(worldPosition.x / 100), Mathf.FloorToInt(worldPosition.z / 100));
    }

    public UnityEngine.Vector3 ChunkIndexToWorldPosition(Vector2Int chunkIndex)
    {
        return new UnityEngine.Vector3(chunkIndex.x * 100, 0, chunkIndex.y * 100);
    }

    int SizeOfChunkAtLODLevel(int LODLevel)
    {
        LODLevel = LODLevel < 0 ? 0 : LODLevel;
        
        return (int)Mathf.Pow(3, LODLevel);
    }

    public ChunkData MovePointerToChunk(Vector2Int destination) //Quickly get a reference to the correct chunk, while also building up the data structure up to this position
    {
        ChunkData pointer = centerChunk;

        while (true)
        {
            if (destination.x > 0)
            {
                if (pointer.north == null)
                {
                    pointer.north = new ChunkData();
                    pointer.north.south = pointer;
                }
                
                pointer = pointer.north;
                destination = new Vector2Int(destination.x - 1, destination.y);
            }
            else if (destination.x < 0)
            {
                if (pointer.south == null)
                {
                    pointer.south = new ChunkData();
                    pointer.south.north = pointer;
                }
                
                pointer = pointer.south;
                destination = new Vector2Int(destination.x + 1, destination.y);
            }
            else if (destination.y > 0)
            {
                if (pointer.east == null)
                {
                    pointer.east = new ChunkData();
                    pointer.east.west = pointer;
                }
                
                pointer = pointer.east;
                destination = new Vector2Int(destination.x, destination.y - 1);
            }
            else if (destination.y < 0)
            {
                if (pointer.west == null)
                {
                    pointer.west = new ChunkData();
                    pointer.west.east = pointer;
                }
                
                pointer = pointer.west;
                destination = new Vector2Int(destination.x, destination.y + 1);
            }
            else
            {
                return pointer;
            }
        }
    }

    //Chunk object pooling
    PooledChunkObject[] chunkObjectPool;
    public void RequestNewObjectPool(int objectAmmount)
    {
        chunkObjectPool = new PooledChunkObject[objectAmmount];
        for (int i = 0; i < objectAmmount; i ++)
        {
            chunkObjectPool[i] = new PooledChunkObject();
            chunkObjectPool[i].chunkObject = GameObject.Instantiate(chunkPrefab);
            chunkObjectPool[i].chunkObject.SetActive(false);
        }

        UnityEngine.Debug.Log("Chunk object pool created with " + objectAmmount + " objects.");
    }

    GameObject RequestFreeChunkObject()
    {
        for (int i = 0; i < chunkObjectPool.Length; i ++)
        {
            if (chunkObjectPool[i].free)
            {
                chunkObjectPool[i].free = false;
                chunkObjectPool[i].chunkObject.SetActive(true);
                return chunkObjectPool[i].chunkObject;
            }
        }

        return null;
    }

    int GetFreeChunks()
    {
        int counter = 0;
        for (int i = 0; i < chunkObjectPool.Length; i ++)
        {
            if (chunkObjectPool[i].free)
            {
                counter ++;
            }
        }

        return counter;
    }

    bool FreeChunk(Vector2Int chunkIndex)
    {
        //Remove the chunk from the generation queue
        for (int i = 0; i < chunkGenerationQueue.Count; i ++)
        {
            ChunkGenerationTask task = chunkGenerationQueue.Dequeue();
            if (task.chunkIndex != chunkIndex)
            {
                chunkGenerationQueue.Enqueue(task);
            }
        }

        //Remove the chunk from the placement queue
        for (int i = 0; i < placementQueue.Count; i ++)
        {
            Vector2Int index = placementQueue.Dequeue();
            if (index != chunkIndex)
            {
                placementQueue.Enqueue(index);
            }
        }
        
        for (int i = 0; i < chunkObjectPool.Length; i ++)
        {
            if (WorldPositionToChunkIndex(chunkObjectPool[i].chunkObject.transform.position) == chunkIndex)
            {
                chunkObjectPool[i].free = true;

                return true;
            }
        }

        UnityEngine.Debug.LogWarning("Failed to free a chunk object.");

        return false;
    }
}

public class ChunkData
{
    public NativeArray<float> heightmap;
    public MeshGenerator mesh;

    public Vector2Int LOD;

    public ChunkData north;
    public ChunkData south;
    public ChunkData east;
    public ChunkData west;
}

class ChunkGenerationTask
{
    public Vector2Int chunkIndex;
    public Vector2Int LOD;
}

class PooledChunkObject
{
    public GameObject chunkObject;
    public bool free = true;
}