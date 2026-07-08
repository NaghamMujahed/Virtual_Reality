using Unity.Mathematics;

namespace PaintBucketSim.Utilities.SpatialHash
{
    public static class SpatialHashUtility
    {
        public static int3 PositionToCell(float3 position, float cellSize)
        {
            float inv = 1.0f / math.max(cellSize, 1e-8f);
            return (int3)math.floor(position * inv);
        }

        public static int HashCell(int3 cell)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 73856093 ^ cell.x;
                hash = hash * 19349663 ^ cell.y;
                hash = hash * 83492791 ^ cell.z;
                return hash;
            }
        }
    }
}