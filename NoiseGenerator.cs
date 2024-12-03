using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;

[BurstCompile]
public class HeightmapGenerator
{
    public IEnumerable<bool> GenerateHeightmap(Vector2Int chunkIndex, Vector2Int LOD, NativeArray<float> heightmap, JobHandle jobHandle)
    {
        GenerateHeightmapJob job = new GenerateHeightmapJob
        {
            heightmap = heightmap,
            chunkIndex = chunkIndex,
            LOD = LOD
        };

        jobHandle = job.Schedule();

        while (!jobHandle.IsCompleted)
        {
            yield return false;
        }

        jobHandle.Complete();

        yield return true;
    }
}

[BurstCompile]
struct GenerateHeightmapJob : IJob
{
    public NativeArray<float> heightmap;
    public Vector2Int chunkIndex;
    public Vector2Int LOD;

    public void Execute()
    {
        for (int x = 0; x < 255; x++)
        {
            for (int y = 0; y < 255; y++)
            {
                float sampleInChunkX = ((float)x / 254) * 100 * LOD.x;
                float sampleInChunkY = ((float)y / 254) * 100 * LOD.y;

                float sampleInWorldX = sampleInChunkX + chunkIndex.x * 100;
                float sampleInWorldY = sampleInChunkY + chunkIndex.y * 100;

                float resolutionAdjustment = 5000;

                heightmap[x * 255 + y] = SampleNoise(new Vector2(sampleInWorldX / resolutionAdjustment, sampleInWorldY / resolutionAdjustment));
            }
        }
    }

    float SampleNoise(Vector2 position)
    {
        float brownianHeight = FractalBrownian(position);

        return Mathf.Sqrt(brownianHeight);
    }

    public float Simplex(Vector2 position) //Perlin as placeholder
    {
        return Mathf.PerlinNoise(position.x, position.y);
    }

    public float FractalBrownian(Vector2 position)
    {
        float height = 0;
        
        int fractalBrownianLayers = 10;

        float detailMultiplicator = 1;
        float impactMultiplicator = 1;

        float impactModifier = 2;
        float detailModifier = 2;

        float stepSize = 0.01f;
        float erosionStrength = 0.1f;
        float erosionMultiplier = 0;

        float normalizer = 0;

        for (int i = 0; i < fractalBrownianLayers; i++)
        {
            // Use Simplex (currently Perlin) for erosion calculation
            float xDerivative = (Simplex(position + new Vector2(stepSize, 0) * detailMultiplicator) - Simplex(position - new Vector2(stepSize, 0) * detailMultiplicator)) / (2 * stepSize);
            float yDerivative = (Simplex(position + new Vector2(0, stepSize) * detailMultiplicator) - Simplex(position - new Vector2(0, stepSize) * detailMultiplicator)) / (2 * stepSize);
            float gradient = Mathf.Sqrt(xDerivative * xDerivative + yDerivative * yDerivative);
            erosionMultiplier += 1 / (1 + gradient * erosionStrength);
            
            // Apply noise and erosion for this octave
            height += Simplex(position * detailMultiplicator) * impactMultiplicator;

            normalizer += impactMultiplicator;

            detailMultiplicator *= detailModifier;
            impactMultiplicator /= impactModifier;
        }

        //height /= maxHeight;

        // Apply average erosion effect
        height *= erosionMultiplier / normalizer;

        return height;
    }

    public float FractalBrownian(float x, float y)
    {
        return FractalBrownian(new Vector2(x, y));
    }
}