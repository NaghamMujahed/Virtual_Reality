using UnityEngine;

namespace PaintBucketSim.Systems.Canvas
{
    internal static class MpmCanvasUvRemap
    {
        private static readonly int ID_FlipU = Shader.PropertyToID("_FlipU");
        private static readonly int ID_FlipV = Shader.PropertyToID("_FlipV");
        private static readonly int ID_SwapUV = Shader.PropertyToID("_SwapUV");
        private static readonly int ID_Rotate90 = Shader.PropertyToID("_Rotate90");
        private static readonly int ID_InvertTextureY = Shader.PropertyToID("_InvertTextureY");

        public static void Apply(ComputeShader shader, MpmCanvasPaintSurface surface)
        {
            if (shader == null)
                return;

            shader.SetInt(ID_FlipU, IsEnabled(surface?.FlipU));
            shader.SetInt(ID_FlipV, IsEnabled(surface?.FlipV));
            shader.SetInt(ID_SwapUV, IsEnabled(surface?.SwapUV));
            shader.SetInt(ID_Rotate90, IsEnabled(surface?.Rotate90));
            shader.SetInt(ID_InvertTextureY, IsEnabled(surface?.InvertTextureY));
        }

        public static void Apply(MaterialPropertyBlock propertyBlock, MpmCanvasPaintSurface surface)
        {
            if (propertyBlock == null)
                return;

            propertyBlock.SetInt(ID_FlipU, IsEnabled(surface?.FlipU));
            propertyBlock.SetInt(ID_FlipV, IsEnabled(surface?.FlipV));
            propertyBlock.SetInt(ID_SwapUV, IsEnabled(surface?.SwapUV));
            propertyBlock.SetInt(ID_Rotate90, IsEnabled(surface?.Rotate90));
            propertyBlock.SetInt(ID_InvertTextureY, IsEnabled(surface?.InvertTextureY));
        }

        private static int IsEnabled(bool? value)
        {
            return value == true ? 1 : 0;
        }
    }
}
