Shader "PaintSim/ParticleBillboard"
{
    Properties
    {
        _ParticleSize ("Particle Size", Float)    = 8.0
        _Softness     ("Edge Softness", Float)    = 4.0
        _Stretch      ("Velocity Stretch", Float) = 2.0
        _Glow         ("Inner Glow", Float)       = 0.5
        _RestDensity  ("Rest Density", Float)     = 1200.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #include "UnityCG.cginc"

            struct Particle
            {
                float3 Position;     float _pad0;
                float3 Velocity;     float _pad1;
                float3 Acceleration; float _pad2;
                float Density;
                float Pressure;
                float Alpha;
                float VelocityDivergence;
                float Mass;
                float SmoothingLength;
                float Radius;
                float Age;
                float4 Color;
                uint Phase;
                uint IsActive;
                uint ParticleIndex;
                uint _pad3;
            };

            StructuredBuffer<Particle> _ParticleBuffer;

            float _ParticleSize;
            float _Softness;
            float _Stretch;
            float _Glow;
            float _RestDensity;

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float2 uv    : TEXCOORD0;
            };

            v2f vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                v2f o;

                Particle p = _ParticleBuffer[instanceID];

                if (p.IsActive == 0)
                {
                    o.pos   = float4(2.0, 2.0, 2.0, 0.0);
                    o.color = float4(0, 0, 0, 0);
                    o.uv    = float2(0, 0);
                    return o;
                }

                float3 viewDir = normalize(_WorldSpaceCameraPos - p.Position);
                float3 up = float3(0, 1, 0);
                float3 right = normalize(cross(up, viewDir));
                up = normalize(cross(viewDir, right));

                // Velocity Stretch:كلما زادت السرعة ازداد قطر الجسيم 
                float speed         = length(p.Velocity);
                float stretchAmount = 1.0 + saturate(speed * _Stretch * 0.5);

                float3 velDir     = speed > 0.001
                                  ? normalize(p.Velocity)
                                  : up;
                float3 stretchDir = normalize(lerp(up, velDir, 0.6));

                float3 crossVec     = cross(stretchDir, viewDir);
                float3 stretchRight = length(crossVec) > 0.001
                                    ? normalize(crossVec)
                                    : right;
                stretchDir = normalize(cross(viewDir, stretchRight));

                float2 quadUV;
                if      (vertexID == 0) quadUV = float2(0, 0);
                else if (vertexID == 1) quadUV = float2(1, 0);
                else if (vertexID == 2) quadUV = float2(0, 1);
                else if (vertexID == 3) quadUV = float2(1, 0);
                else if (vertexID == 4) quadUV = float2(1, 1);
                else                    quadUV = float2(0, 1);

                float2 quadOffset = (quadUV - 0.5) * 2.0;

                float baseSize = p.Radius * 2.0 * _ParticleSize;

                float normalizedDensity = p.Density / max(_RestDensity, 0.001);
                float densityBoost      = 1.0 + smoothstep(1.0, 1.5, normalizedDensity) * 2.0;
                baseSize *= densityBoost;

                float2 finalOffset = quadOffset;
                finalOffset.y *= stretchAmount;

                float3 worldPos = p.Position
                    + stretchRight * finalOffset.x * baseSize
                    + stretchDir   * finalOffset.y * baseSize;

                o.pos   = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.color = p.Color;
                o.uv    = quadUV;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 center = i.uv - 0.5;
                float  dist   = length(center);

                if (dist > 0.5) discard;

                float alpha = exp(-dist * dist * _Softness );

                float glowMask = exp(-dist * dist * _Glow * 20.0) * 0.4;
                float specMask = pow(max(0, 1.0 - dist * 3.0), 10.0) * 0.3;

                float3 col  = i.color.rgb;
                col        += i.color.rgb * glowMask;
                col        += specMask;

                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, lum.xxx, 0.1);

                float finalAlpha = max(0.9, i.color.a * alpha);

                return float4(col, finalAlpha);
            }
            ENDCG
        }
    }
}