Shader "FateScreenshotShaderCompatible" {

	Properties{
		_Alpha("Alpha", 2D) = "white" {}
		_Color ("Main Color", Color) = (1,1,1,1)
		_AddColor ("Add Color", Color) = (0,0,0,0)
		_MainTex ("Base (RGB) Alpha (A)", 2D) = "white" { }
		_Cutoff ("Base Alpha cutoff", Range(0, 0.9)) = 0.9
	}

	SubShader{
		Tags { "IGNOREPROJECTOR" = "true" "QUEUE" = "Transparent" "RenderType" = "TransparentCutout" }

		Pass //Opaque Character
		{
			Tags { "IGNOREPROJECTOR" = "true" "QUEUE" = "Transparent" "RenderType" = "TransparentCutout" }
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION0;
				float2 uv : TEXCOORD0;
				float4 color : COLOR0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : POSITION0;
				float4 color : COLOR0;
			};
				
			sampler2D _MainTex;
			sampler2D _Alpha;
			fixed4 _Color;
			fixed4 _AddColor;
			float4 u_xlat0;
			float4 u_xlat1;

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.color = v.color;
				o.uv = v.uv;
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				fixed4 col = tex2D(_MainTex, i.uv);
				fixed4 alph = tex2D(_Alpha, i.uv);
				col.a *= alph.r;
				clip(col.a - 1);
				col = fixed4(1,1,1,1);
				return col;
			}
			ENDCG
		}
		Pass //ADD COLORS
		{
			Cull Off // make double sided
			Blend SrcAlpha OneMinusSrcAlpha
			BlendOp Max
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
			fixed4 _ColorTint;
			float4 u_xlat0;

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
				col = fixed4(col.a,col.a,col.a,col.a);
				return col;
			}
			ENDCG
		}
	}
}