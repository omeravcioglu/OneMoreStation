// Grid-texture overlays drawn over the map: built-up tint and data views (mode 0), puffy clouds over
// unexplored districts (mode 1) and the town-limit haze with its dashed frame (mode 2).
// The texture is one texel per city cell and is sampled bilinearly for soft edges.
Shader "Metro/Overlay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _HatchColor ("Hatch / shade colour", Color) = (0,0,0,0.1)
        _HatchSpacing ("Hatch Spacing (px)", Float) = 10
        _Mode ("Mode (0 tint, 1 clouds, 2 frame)", Float) = 0
        _Opacity ("Opacity", Float) = 1
        _Rect ("Play area (xMin, yMin, xMax, yMax)", Vector) = (0,0,1,1)
        _EdgeColor ("Edge / outline colour", Color) = (0,0,0,0.6)
        _Soft ("Edge softness (world)", Float) = 0.6
        _Puff ("Cloud puff size (world)", Float) = 1.2
        _KindTex ("Town style per cell (point)", 2D) = "black" {}
        _Lot ("Town lot size (world)", Float) = 0.3
        _Cell ("City cell size (world)", Float) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
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

            sampler2D _MainTex;
            float4 _Color;
            float4 _HatchColor;
            float _HatchSpacing;
            float _Mode;
            float _Opacity;
            float4 _Rect;
            float4 _EdgeColor;
            float _Soft;
            float _Puff;
            sampler2D _KindTex;
            float _Lot;
            float _Cell;
            float _PixelWorldSize;   // global: world units per screen pixel
            float _PxScale;          // global: screen pixels per 1080p reference pixel

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screen : TEXCOORD1;
                float2 world : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.screen = ComputeScreenPos(o.pos);
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                return o;
            }

            float PxScale() { return _PxScale > 0.0 ? _PxScale : 1.0; }

            // Diagonal hatching, anchored to the screen; spacing in reference pixels.
            float Stripe(float4 screen, float spacing, float width)
            {
                float2 sp = (screen.xy / max(screen.w, 1e-5)) * _ScreenParams.xy;
                float s = spacing * PxScale();
                float h = abs(frac((sp.x + sp.y) / s) - 0.5) * s;   // pixels from the stripe's centre
                return 1.0 - smoothstep(width * 0.5 * PxScale() - 0.6, width * 0.5 * PxScale() + 0.6, h);
            }

            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // Puffy bumps: one jittered circle per grid cell; returns the strongest "cone" and its circle's
            // vertical position inside the circle (-1 bottom .. 1 top) for shading.
            float Puffs(float2 w, out float vy)
            {
                float2 cell = floor(w / _Puff);
                float best = 0.0;
                vy = 0.0;
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 c = cell + float2(x, y);
                        float2 h = Hash2(c);
                        float2 center = (c + 0.2 + 0.6 * h) * _Puff;
                        float r = _Puff * (0.55 + 0.35 * frac(h.x * 7.3 + h.y * 3.1));
                        float2 d = w - center;
                        float v = 1.0 - length(d) / r;
                        if (v > best)
                        {
                            best = v;
                            vy = d.y / r;
                        }
                    }
                }
                return best;
            }

            // ---------------------------------------------------------------- storybook town (mode 3)

            float SdBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            float SdTriangle(float2 p, float2 p0, float2 p1, float2 p2)
            {
                float2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
                float2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
                float2 pq0 = v0 - e0 * saturate(dot(v0, e0) / dot(e0, e0));
                float2 pq1 = v1 - e1 * saturate(dot(v1, e1) / dot(e1, e1));
                float2 pq2 = v2 - e2 * saturate(dot(v2, e2) / dot(e2, e2));
                float s = sign(e0.x * e2.y - e0.y * e2.x);
                float2 d = min(min(float2(dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                                   float2(dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                                   float2(dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
                return -sqrt(d.x) * sign(d.y);
            }

            // Paints a shape (signed distance sd, world units) with a fill and an outline of half-width ow.
            void Paint(inout float4 col, float sd, float3 fill, float3 edge, float ow, float aa)
            {
                float f = saturate(0.5 - sd / aa);
                col.rgb = lerp(col.rgb, fill, f);
                col.a = max(col.a, f);
                if (ow <= 0.0) return;
                float e = saturate(0.5 - (abs(sd) - ow) / aa);
                col.rgb = lerp(col.rgb, edge, e);
                col.a = max(col.a, e);
            }

            float3 Pick4(float t, float3 a, float3 b, float3 c, float3 d)
            {
                return t < 0.25 ? a : (t < 0.5 ? b : (t < 0.75 ? c : d));
            }

            float3 Hex(float r, float g, float b) { return float3(r, g, b) / 255.0; }

            float4 Town(float2 w, float density, float style)
            {
                float pw = max(_PixelWorldSize, 1e-6);
                float aa = pw;
                float ow = 0.65 * PxScale() * pw;
                float lot = _Lot;
                float2 g = w / lot;
                float2 cell = floor(g);
                float2 h = Hash2(cell);
                float2 h2 = Hash2(cell + 17.31);
                float2 lp = frac(g) - 0.5;                   // -0.5..0.5 inside the lot
                float2 o = (h2 - 0.5) * 0.24;
                float4 col = float4(0, 0, 0, 0);
                // Low-frequency "greenness": some patches of town are leafy, with fewer houses and more trees.
                float2 gc = floor(w / 1.7);
                float2 gf = smoothstep(0.0, 1.0, frac(w / 1.7));
                float green = lerp(lerp(Hash2(gc).x, Hash2(gc + float2(1, 0)).x, gf.x),
                                   lerp(Hash2(gc + float2(0, 1)).x, Hash2(gc + float2(1, 1)).x, gf.x), gf.y);

                if (style > 0.5 && style < 1.5)
                {
                    // Downtown: pastel towers with window grids.
                    if (h.x < smoothstep(0.08, 0.45, density))
                    {
                        float2 hs = float2(0.2 + 0.1 * h.y, 0.27 + 0.16 * frac(h.y * 5.3));
                        float2 p = lp - o * 0.6 - float2(0.0, hs.y - 0.42);
                        float3 wall = Pick4(frac(h.y * 3.7), Hex(215, 233, 251), Hex(244, 223, 248), Hex(255, 241, 196), Hex(221, 244, 234));
                        Paint(col, SdBox(p, hs, 0.02) * lot, wall, Hex(156, 147, 184), ow, aa);
                        float2 wg = (p + hs) / (hs * 2.0);
                        float2 wc = frac(wg * float2(3.0, 5.0)) - 0.5;
                        float win = SdBox(wc, float2(0.22, 0.2), 0.05) * lot * hs.x * 0.66;
                        if (abs(p.x) < hs.x - 0.03 && abs(p.y) < hs.y - 0.03) Paint(col, win, Hex(183, 205, 235), Hex(183, 205, 235), 0.0, aa);
                    }
                }
                else if (style > 1.5 && style < 2.5)
                {
                    // Industry: grey sheds with sawtooth roofs and a smoking chimney.
                    if (h.x < smoothstep(0.06, 0.4, density))
                    {
                        float2 p = lp - o;
                        float3 wall = Hex(228, 226, 234);
                        float3 ink = Hex(156, 147, 184);
                        Paint(col, SdBox(p - float2(0.22, 0.12), float2(0.05, 0.17), 0.01) * lot, Hex(201, 196, 214), ink, ow, aa);
                        float teeth = 1e5;
                        for (int k = 0; k < 3; k++)
                        {
                            float x0 = -0.34 + k * 0.226;
                            teeth = min(teeth, SdTriangle(p, float2(x0, 0.05), float2(x0 + 0.226, 0.05), float2(x0 + 0.226, 0.2)));
                        }
                        Paint(col, teeth * lot, Hex(201, 196, 214), ink, ow, aa);
                        Paint(col, SdBox(p - float2(0.0, -0.1), float2(0.34, 0.15), 0.015) * lot, wall, ink, ow, aa);
                        Paint(col, (length(p - float2(0.27, 0.36 + 0.03 * h.y)) - 0.06) * lot, float3(1, 1, 1), ink, ow, aa);
                    }
                }
                else
                {
                    float houses = smoothstep(0.05, 0.55, density) * lerp(0.95, 0.45, smoothstep(0.55, 0.85, green));
                    if (h.x < houses)
                    {
                        // A little house: pastel walls, a candy roof, a door and a window.
                        float2 p = lp - o;
                        float3 wall = Pick4(h.y, Hex(255, 249, 236), Hex(255, 230, 210), Hex(243, 230, 255), Hex(233, 248, 225));
                        float3 roof = Pick4(frac(h.y * 7.1), Hex(244, 135, 106), Hex(141, 189, 240), Hex(190, 147, 224), Hex(244, 184, 78));
                        float3 ink = Hex(201, 169, 124);
                        float sc = 0.78 + 0.34 * frac(h.x * 13.7);
                        float2 q = p / sc;
                        float k2 = lot * sc;
                        Paint(col, SdBox(q - float2(0.0, -0.12), float2(0.25, 0.15), 0.015) * k2, wall, ink, ow, aa);
                        Paint(col, SdTriangle(q, float2(-0.33, 0.02), float2(0.33, 0.02), float2(0.0, 0.29)) * k2, roof, ink, ow, aa);
                        Paint(col, SdBox(q - float2(0.09, -0.19), float2(0.045, 0.08), 0.01) * k2, roof * 0.85, ink, ow * 0.7, aa);
                        Paint(col, SdBox(q - float2(-0.11, -0.1), float2(0.05, 0.045), 0.01) * k2, Hex(207, 231, 255), ink, ow * 0.7, aa);
                        if (h2.x > 0.72 && density > 0.3)
                        {
                            // A smaller neighbour squeezed in beside it.
                            float2 q2 = (p - float2(o.x > 0.0 ? -0.33 : 0.33, -0.12)) / 0.62;
                            float k3 = lot * 0.62;
                            Paint(col, SdBox(q2 - float2(0.0, -0.12), float2(0.25, 0.15), 0.015) * k3, wall, ink, ow, aa);
                            Paint(col, SdTriangle(q2, float2(-0.33, 0.02), float2(0.33, 0.02), float2(0.0, 0.29)) * k3, roof * 0.92, ink, ow, aa);
                        }
                    }
                    else if (h.x < houses + 0.14 + 0.2 * (1.0 - houses) && density > 0.02)
                    {
                        // A round tree with a soft shadow.
                        float2 p = lp - o * 1.5;
                        float r = 0.14 + 0.07 * h.y;
                        float3 crown = h.y < 0.5 ? Hex(127, 207, 92) : Hex(159, 219, 124);
                        Paint(col, (length(p - float2(0.05, -0.06)) - r) * lot, Hex(111, 174, 79), Hex(111, 174, 79), 0.0, aa);
                        Paint(col, (length(p) - r) * lot, crown, Hex(78, 138, 54), ow, aa);
                    }
                    else if (density < 0.1 && h.y > 0.45)
                    {
                        // Countryside: striped fields.
                        float2 fp = lp - o * 0.3;
                        float sd = SdBox(fp, float2(0.44, 0.44), 0.06) * lot;
                        float stripes = step(0.5, frac((h.x < 0.5 ? fp.x : fp.y) * 7.0));
                        float3 field = lerp(Hex(242, 224, 138), Hex(216, 235, 160), stripes);
                        float a = saturate(0.5 - sd / aa) * 0.6;
                        col.rgb = lerp(col.rgb, field, a);
                        col.a = max(col.a, a);
                    }
                }

                // Zoomed out, the town becomes a soft tint; zoomed in, the drawings fade in.
                float lotPx = lot / pw;
                float detail = smoothstep(6.0, 12.0, lotPx);
                float3 tint = (style > 0.5 && style < 1.5) ? Hex(226, 222, 242) : ((style > 1.5 && style < 2.5) ? Hex(222, 219, 230) : Hex(241, 205, 176));
                float tintA = smoothstep(0.02, 0.45, density) * 0.75;
                float4 far = float4(tint, tintA);
                float4 near = col;
                near.rgb = lerp(tint, col.rgb, col.a);
                near.a = max(col.a, tintA * 0.45);
                return lerp(far, near, detail);
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 c;
                if (_Mode > 2.5)
                {
                    float density = tex2D(_MainTex, i.uv).a;
                    float style = floor(tex2D(_KindTex, i.uv).r * 255.0 / 40.0 + 0.5);
                    c = Town(i.world, density, style);
                    c.a *= _Opacity;
                    return c;
                }
                if (_Mode < 0.5)
                {
                    c = tex2D(_MainTex, i.uv) * _Color;
                }
                else if (_Mode < 1.5)
                {
                    // Clouds: the fog coverage, made bumpy by puffs, with an ink outline and soft blue-grey
                    // shading under each puff. As a district opens, its coverage fades and the clouds shrink away.
                    // Coverage, blurred over a couple of cells so the puffs (not the grid) shape the edge.
                    float2 du = float2(_Puff * 0.55, 0.0) * (fwidth(i.uv.x) / max(fwidth(i.world.x), 1e-6));
                    float2 dv = float2(0.0, _Puff * 0.55) * (fwidth(i.uv.y) / max(fwidth(i.world.y), 1e-6));
                    float raw = tex2D(_MainTex, i.uv).a * 0.4
                        + (tex2D(_MainTex, i.uv + du).a + tex2D(_MainTex, i.uv - du).a + tex2D(_MainTex, i.uv + dv).a + tex2D(_MainTex, i.uv - dv).a) * 0.15;
                    float cov = smoothstep(0.02, 0.98, raw);
                    float vy;
                    float puff = Puffs(i.world, vy);
                    // Roughly how far (in world units) we are inside the unexplored area, plus the puffs' bumps.
                    float f = (raw - 0.5) * _Cell * 2.5 + (puff - 0.42) * _Puff * 0.75 * smoothstep(0.02, 0.3, raw);
                    float px = max(fwidth(f), 1e-5);
                    float d = f / px;                         // signed distance to the cloud edge, in pixels
                    float inside = saturate(d + 0.5);
                    float outline = 1.75 * PxScale();
                    float ink = 1.0 - smoothstep(outline - 0.6, outline + 0.6, abs(d - outline));
                    float shade = smoothstep(-0.05, -0.65, vy) * saturate(d / (6.0 * PxScale()));
                    c.rgb = lerp(_Color.rgb, _HatchColor.rgb, shade * _HatchColor.a);
                    c.a = inside * _Color.a;
                    float4 e = float4(_EdgeColor.rgb, 1.0);
                    c = lerp(c, e, ink * _EdgeColor.a);
                }
                else
                {
                    // Outside the play area: a pale haze with hatching; at the limit a dashed ink frame on a
                    // white under-stroke, with softly rounded corners.
                    float rad = 0.6;
                    float2 halfSize = (_Rect.zw - _Rect.xy) * 0.5;
                    float2 q = abs(i.world - (_Rect.xy + _Rect.zw) * 0.5) - (halfSize - rad);
                    float outside = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - rad;
                    float a = smoothstep(0.0, _Soft, outside);
                    float hatch = Stripe(i.screen, _HatchSpacing, 3.5) * _HatchColor.a;
                    c.rgb = lerp(_Color.rgb, _HatchColor.rgb, hatch);
                    c.a = a * lerp(_Color.a, 1.0, hatch * 0.6);
                    float pw = max(_PixelWorldSize, 1e-6);
                    float dpx = outside / pw;                  // distance from the limit, in screen pixels
                    float ps = PxScale();
                    float under = 1.0 - smoothstep(6.0 * ps - 0.7, 6.0 * ps + 0.7, abs(dpx));
                    float inkLine = 1.0 - smoothstep(2.25 * ps - 0.7, 2.25 * ps + 0.7, abs(dpx));
                    float along = (i.world.x + i.world.y) / (pw * ps);
                    float dash = step(frac(along / 34.0), 22.0 / 34.0);
                    c = lerp(c, float4(1, 1, 1, 1), under * 0.92);
                    c = lerp(c, float4(_EdgeColor.rgb, 1.0), inkLine * dash * _EdgeColor.a);
                }
                c.a *= _Opacity;
                return c;
            }
            ENDCG
        }
    }
}
