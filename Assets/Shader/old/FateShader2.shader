Shader "FateShader2" {

	Properties{
		[NoScaleOffset] _MainTex("Albedo (RGB)", 2D) = "white" {}
		_Alpha("Alpha", 2D) = "white" {}
		_Cutoff ("Base Alpha cutoff", Range(0, 1)) = 0.91
		_ColorTint ("Tint", Color) = (0,0,0,0)
	}

		// multiple render passes, in order to write to the zbuffer before drawing visible surfaces
		SubShader{
			Tags {
				"Queue" = "Transparent"
			}

			Pass {
				ZWrite On
				Cull Off // make double sided
				ColorMask 0 // don't draw any color
		AlphaToMask On

				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag

				#include "UnityCG.cginc"

				struct appdata
				{
					float4 vertex : POSITION;
					float2 uv : TEXCOORD0;
				};

				struct v2f
				{
					float2 uv : TEXCOORD0;
					float4 vertex : SV_POSITION;
				};

				sampler2D _MainTex;
				sampler2D _Alpha;
				float _Cutoff;

				v2f vert(appdata v)
				{
					v2f o;
					o.vertex = UnityObjectToClipPos(v.vertex);
					o.uv = v.uv; // just pass through with no scale/offset
					return o;
				}

				fixed4 frag(v2f i) : SV_Target
				{
					fixed4 col = tex2D(_MainTex, i.uv);
					fixed4 alph = tex2D(_Alpha, i.uv);
					col.a *= alph.r;
					clip(col.a - _Cutoff); // remove non-opaque pixels from writing to zbuffer
					return col;
				}
				ENDCG
			}

			Pass
			{
				ZWrite Off
				Cull Off // make double sided
				Blend SrcAlpha OneMinusSrcAlpha

				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag

				#include "UnityCG.cginc"

				struct appdata
				{
					float4 vertex : POSITION;
					float2 uv : TEXCOORD0;
				};

				struct v2f
				{
					float2 uv : TEXCOORD0;
					float4 vertex : SV_POSITION;
				};

				sampler2D _MainTex;
				fixed4 _ColorTint;

				v2f vert(appdata v)
				{
					v2f o;
					o.vertex = UnityObjectToClipPos(v.vertex);
					o.uv = v.uv; // just pass through with no scale/offset
					return o;
				}

				fixed4 frag(v2f i) : SV_Target
				{
					fixed4 col = tex2D(_MainTex, i.uv);
					return col;
				}
				ENDCG
			}
	}
}