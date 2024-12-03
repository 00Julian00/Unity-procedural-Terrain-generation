using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Rendering;
using Unity.Mathematics;
using System.Diagnostics;
using UnityEditor;
using Unity.Burst;
using Unity.VisualScripting;

//TODO: Recalculate normals in a job
//TODO: Calculate UVs in a job
//TODO: Dispose of all Native arrays when the generation process is interrupted

[BurstCompile]
public class MeshManager
{
    public static MeshGenerator GenerateMeshFromHeightmap(NativeArray<float> heightmap, MeshGenerator meshGenerator, Vector2Int size, Vector2Int LOD, float height, MonoBehaviour monoBehaviour, JobHandle heightmapHandle)
    {   
        monoBehaviour.StartCoroutine(RunJobs(meshGenerator, heightmap, size, LOD, height, heightmapHandle));

        return meshGenerator;
    }

    static IEnumerator RunJobs(MeshGenerator meshGenerator, NativeArray<float> heightmap, Vector2Int size, Vector2Int LOD, float height, JobHandle heightmapHandle)
    {
        //wait for the heightmap to be generated

        //Set up mesh
        meshGenerator.mesh.MarkDynamic();
        meshGenerator.physicsDataFinished = false;

        // Allocate mesh data
        Mesh.MeshDataArray meshDataArray = Mesh.AllocateWritableMeshData(1);
        Mesh.MeshData meshData = meshDataArray[0];

        int vertexCount = 255 * 255;
        int triangleCount = (255 - 1) * (255 - 1) * 6;

        // Set up vertex buffer
        meshData.SetVertexBufferParams(vertexCount,
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));

        // Set up index buffer
        meshData.SetIndexBufferParams(triangleCount, IndexFormat.UInt32);

        NativeArray<float3> boundsMinMax = new NativeArray<float3>(2, Allocator.Persistent);
        
        //Run job
        GenerateMeshJob meshJob = new GenerateMeshJob
        {
            heightmap = heightmap,
            size = size,
            height = height,
            meshData = meshData,
            boundsMinMax = boundsMinMax
        };
        JobHandle jobHandle = meshJob.Schedule(heightmapHandle);

        while (!jobHandle.IsCompleted)
        {
            yield return null;
        }

        jobHandle.Complete();

        //Apply data to mesh
        Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, meshGenerator.mesh);
        meshGenerator.mesh.RecalculateNormals();

        //Recalculate UVs
        Vector2[] uvs = new Vector2[meshGenerator.mesh.vertices.Length];

        for (int x = 0; x < 255; x++)
        {
            for (int z = 0; z < 255; z++)
            {
                uvs[x * 255 + z] = new Vector2(((float)x / 254) * LOD.x, ((float)z / 254) * LOD.y);
            }
        }

        meshGenerator.mesh.uv = uvs;

        //Update bounds
        float3 min = boundsMinMax[0];
        float3 max = boundsMinMax[1];
        Vector3 center = (max + min) * 0.5f;
        Vector3 boundsSize = max - min;
        meshGenerator.mesh.bounds = new Bounds(center, boundsSize);

        meshGenerator.meshDataFinished = true;

        boundsMinMax.Dispose();

        //meshGenerator.mesh.Optimize(); //Hurts performance during generation

        //Bake physics
        if (LOD.x == 1 && LOD.y == 1) //Only bake physics for the highest LOD
        {
            BakePhysics bakePhysics = new BakePhysics
            {
                meshID = meshGenerator.mesh.GetInstanceID()
            };
            jobHandle = bakePhysics.Schedule();

            while (!jobHandle.IsCompleted)
            {
                yield return null;
            }

            jobHandle.Complete();
        }

        meshGenerator.physicsDataFinished = true;
    }
}

[BurstCompile]
public struct GenerateMeshJob : IJob
{
    public NativeArray<float> heightmap;
    public Vector2 size;
    public float height;
    public Mesh.MeshData meshData;
    public NativeArray<float3> boundsMinMax;
    public void Execute()
    {
        // Get vertex and index data
        NativeArray<float3> positions = meshData.GetVertexData<float3>();
        NativeArray<uint> triangles = meshData.GetIndexData<uint>();

        // Initialize min and max for bounds calculation
        float3 min = new float3(float.MaxValue, float.MaxValue, float.MaxValue);
        float3 max = new float3(float.MinValue, float.MinValue, float.MinValue);

        for (int x = 0; x < 255; x++)
        {
            for (int z = 0; z < 255; z++)
            {
                float3 position = new float3(x * size.x / (255 - 1), heightmap[x * 255 + z] * height, z * size.y / (255 - 1));
                positions[x * 255 + z] = position;

                // Update min and max for bounds
                min = math.min(min, position);
                max = math.max(max, position);
            }
        }

        // Generate triangles
        int trisCounter = 0;
        for (int a = 0; a < 255 - 1; a++)
        {
            for (int b = 0; b < 255 - 1; b++)
            {
                triangles[trisCounter] = (uint)(a * 255 + b);
                triangles[trisCounter + 1] = (uint)(a * 255 + b + 1);
                triangles[trisCounter + 2] = (uint)(a * 255 + b + 255);

                triangles[trisCounter + 3] = (uint)(a * 255 + b + 1);
                triangles[trisCounter + 4] = (uint)(a * 255 + b + 1 + 255);
                triangles[trisCounter + 5] = (uint)(a * 255 + b + 255);

                trisCounter += 6;
            }
        }

        boundsMinMax[0] = min;
        boundsMinMax[1] = max;

        // Set up submesh
        meshData.subMeshCount = 1;
        meshData.SetSubMesh(0, new SubMeshDescriptor(0, triangles.Length, MeshTopology.Triangles));
    }
}

[BurstCompile]
public struct BakePhysics : IJob
{
    public int meshID;
    public void Execute()
    {
        Physics.BakeMesh(meshID, false);
    }
}

public class MeshGenerator
{
    public Mesh mesh;
    public bool physicsDataFinished;
    public bool meshDataFinished;
}
