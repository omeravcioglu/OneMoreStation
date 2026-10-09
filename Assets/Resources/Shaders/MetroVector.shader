// Anti-aliased vector shapes for the schematic map.
// Every vertex carries its "centerline" position plus an offset expressed in half-width units.
// The half-width itself is computed per vertex from a world-space size that is clamped to a
// pixel range, so metro lines keep a readable thickness at every zoom level.
Shader "Metro/Vector"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float _PixelWorldSize;   // global: world units per screen pixel
            float _PxScale;          // global: screen pixels per 1080p reference pixel
            float4 _Tint;

            struct appdata
            {
                float4 vertex  : POSITION;
                float4 color   : COLOR;
                float2 sdf     : TEXCOORD0;   // normalised shape coordinates, edge at length == 1
                float4 offs    : TEXCOORD1;   // xy: fixed offset, zw: offset grown by the AA margin (half-width units)
                float4 wparams : TEXCOORD2;   // x: base half-width (world), y: min px, z: max px, w: shape radius in half-widths
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float2 sdf   : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float px = max(_PixelWorldSize, 1e-6);
                float ps = _PxScale > 0.0 ? _PxScale : 1.0;
                float hwPx = clamp(v.wparams.x / px, v.wparams.y * ps, v.wparams.z * ps);
                float hw = hwPx * px;
                float rPx = max(hwPx * max(v.wparams.w, 0.0), 1e-3);
                float g = (rPx + 1.0) / rPx;            // grow by one pixel for the AA ramp
                float3 p = v.vertex.xyz;
                p.xy += (v.offs.xy + v.offs.zw * g) * hw;
                o.pos = UnityObjectToClipPos(float4(p, 1.0));
                float4 c = v.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb = GammaToLinearSpace(c.rgb);
                #endif
                o.color = c * _Tint;
                o.sdf = v.sdf * g;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float d = length(i.sdf);
                float w = max(fwidth(d), 1e-5);
                float a = saturate((1.0 - d) / w + 0.5);
                float4 c = i.color;
                c.a *= a;
                return c;
            }
            ENDCG
        }
    }
}
