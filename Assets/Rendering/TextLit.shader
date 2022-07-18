// NOTE that this shader is largely copied from Sprite-Lit-Default.shader w/ elements of TMP_SDF-Mobile.shader via TextLitHelpers.hlsl

Shader "TextLit"
{
	Properties
	{
		// Sprite-Lit-Default
		_MainTex("Diffuse", 2D) = "white" {}
		_MaskTex("Mask", 2D) = "white" {}
		_NormalMap("Normal Map", 2D) = "bump" {}

		// TMP_SDF-Mobile
		[HDR] _FaceColor("Face Color", Color) = (1,1,1,1)
		_FaceDilate("Face Dilate", Range(-1,1)) = 0

		[HDR]_OutlineColor("Outline Color", Color) = (0,0,0,1)
		_OutlineWidth("Outline Thickness", Range(0,1)) = 0
		_OutlineSoftness("Outline Softness", Range(0,1)) = 0

		[HDR]_UnderlayColor("Border Color", Color) = (0,0,0,.5)
		_UnderlayOffsetX("Border OffsetX", Range(-1,1)) = 0
		_UnderlayOffsetY("Border OffsetY", Range(-1,1)) = 0
		_UnderlayDilate("Border Dilate", Range(-1,1)) = 0
		_UnderlaySoftness("Border Softness", Range(0,1)) = 0

		_WeightNormal("Weight Normal", float) = 0
		_WeightBold("Weight Bold", float) = .5

		//_ShaderFlags("Flags", float) = 0
		_ScaleRatioA("Scale RatioA", float) = 1
		//_ScaleRatioB("Scale RatioB", float) = 1
		_ScaleRatioC("Scale RatioC", float) = 1

		//_MainTex("Font Atlas", 2D) = "white" {}
		_TextureWidth("Texture Width", float) = 512
		_TextureHeight("Texture Height", float) = 512
		_GradientScale("Gradient Scale", float) = 5
		_ScaleX("Scale X", float) = 1
		_ScaleY("Scale Y", float) = 1
		_PerspectiveFilter("Perspective Correction", Range(0, 1)) = 0.875
		_Sharpness("Sharpness", Range(-1,1)) = 0

		_VertexOffsetX("Vertex OffsetX", float) = 0
		_VertexOffsetY("Vertex OffsetY", float) = 0

		_ClipRect("Clip Rect", vector) = (-32767, -32767, 32767, 32767)
		_MaskSoftnessX("Mask SoftnessX", float) = 0
		_MaskSoftnessY("Mask SoftnessY", float) = 0

		_StencilComp("Stencil Comparison", Float) = 8
		_Stencil("Stencil ID", Float) = 0
		_StencilOp("Stencil Operation", Float) = 0
		_StencilWriteMask("Stencil Write Mask", Float) = 255
		_StencilReadMask("Stencil Read Mask", Float) = 255

		_CullMode("Cull Mode", Float) = 0
		_ColorMask("Color Mask", Float) = 15
	}

	SubShader
	{
		Tags
		{
			// Sprite-Lit-Default
			"Queue" = "Transparent"
			"RenderType" = "Transparent"
			"RenderPipeline" = "UniversalPipeline"

			// TMP_SDF-Mobile
			"IgnoreProjector" = "True"
		}

		// TMP_SDF-Mobile
		Stencil
		{
			Ref[_Stencil]
			Comp[_StencilComp]
			Pass[_StencilOp]
			ReadMask[_StencilReadMask]
			WriteMask[_StencilWriteMask]
		}

		Cull [_CullMode]
		ZWrite Off
		Lighting Off
		Fog { Mode Off }
		ZTest[unity_GUIZTestMode]
		Blend One OneMinusSrcAlpha
		ColorMask[_ColorMask]

		// Sprite-Lit-Default
		//Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
		//Cull Off
		//ZWrite Off

		Pass
		{
			// Sprite-Lit-Default
			Tags { "LightMode" = "Universal2D" }

			HLSLPROGRAM
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "TextLitHelpers.hlsl"

			//#pragma enable_d3d11_debug_symbols // enable for RenderDoc debugging w/ source

			#pragma vertex CombinedShapeLightVertex
			#pragma fragment CombinedShapeLightFragment

			#pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
			#pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
			#pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
			#pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
			#pragma multi_compile _ DEBUG_DISPLAY

			struct Attributes
			{
				float4 positionOS   : POSITION; // upgraded from float3
				float4 color        : COLOR;
				float2  uv          : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID

				// TMP_SDF-Mobile
				float3	normal		: NORMAL;
				float4	texcoord1	: TEXCOORD1;	// Texture UV, alpha, reserved
			};

			struct Varyings
			{
				float4  positionCS  : SV_POSITION;
				half4   color       : COLOR;
				float2  uv          : TEXCOORD0;
				half2   lightingUV  : TEXCOORD1;
				#if defined(DEBUG_DISPLAY)
				float3  positionWS  : TEXCOORD2;
				#endif
				UNITY_VERTEX_OUTPUT_STEREO

				half4	param		: TEXCOORD3;	// Scale(x), BiasIn(y), BiasOut(z), Bias(w)
			};

			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

			TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);
			TEXTURE2D(_MaskTex);
			SAMPLER(sampler_MaskTex);
			half4 _MainTex_ST;

			#if USE_SHAPE_LIGHT_TYPE_0
			SHAPE_LIGHT(0)
			#endif

			#if USE_SHAPE_LIGHT_TYPE_1
			SHAPE_LIGHT(1)
			#endif

			#if USE_SHAPE_LIGHT_TYPE_2
			SHAPE_LIGHT(2)
			#endif

			#if USE_SHAPE_LIGHT_TYPE_3
			SHAPE_LIGHT(3)
			#endif

			// TMP_SDF-Mobile
			float4 _FaceColor;
			float _FaceDilate;
			float4 _OutlineColor;
			float _OutlineWidth;
			float _OutlineSoftness;
			float _UnderlayColor;
			float _UnderlayOffsetX;
			float _UnderlayOffsetY;
			float _UnderlayDilate;
			float _UnderlaySoftness;
			float _WeightNormal;
			float _WeightBold;
			float _ScaleRatioA;
			float _ScaleRatioC;
			float _TextureWidth;
			float _TextureHeight;
			float _GradientScale;
			float _ScaleX;
			float _ScaleY;
			float _PerspectiveFilter;
			float _Sharpness;
			float _VertexOffsetX;
			float _VertexOffsetY;
			float _ClipRect;
			float _MaskSoftnessX;
			float _MaskSoftnessY;

			// Sprite-Lit-Default
			Varyings CombinedShapeLightVertex(Attributes v)
			{
				Varyings o = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				o.positionCS = TransformObjectToHClip(v.positionOS);
				#if defined(DEBUG_DISPLAY)
				o.positionWS = TransformObjectToWorld(v.positionOS);
				#endif
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);

				//o.color = v.color;

				// TMP_SDF-Mobile
				vertex_t inputTMP;
				inputTMP.vertex = v.positionOS;
				inputTMP.normal = v.normal;
				inputTMP.color = v.color;
				inputTMP.texcoord0 = v.uv;
				inputTMP.texcoord1 = v.texcoord1.xy;
				pixel_t outputTMP = VertShader(inputTMP, _FaceColor, _FaceDilate, _OutlineColor, _OutlineWidth, _OutlineSoftness, _UnderlayOffsetX, _UnderlayOffsetY, _UnderlayDilate, _UnderlaySoftness, _WeightNormal, _WeightBold, _ScaleRatioA, _ScaleRatioC, _TextureWidth, _TextureHeight, _GradientScale, _ScaleX, _ScaleY, _PerspectiveFilter, _Sharpness, _VertexOffsetX, _VertexOffsetY, _ClipRect, _MaskSoftnessX, _MaskSoftnessY);

				// TODO: process more outputs from TMP?
				// = outputTMP.vertex;
				o.color = outputTMP.faceColor;
				// = outputTMP.outlineColor;
				// = outputTMP.texcoord0;
				o.param = outputTMP.param;
				// = outputTMP.mask;
				//#if (UNDERLAY_ON | UNDERLAY_INNER)
				// = outputTMP.texcoord1;
				// = outputTMP.underlayParam
				//#endif

				return o;
			}

			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

			half4 CombinedShapeLightFragment(Varyings i) : SV_Target
			{
				// TMP_SDF-Mobile
				// TODO: actually supply more robust TMP inputs?
				pixel_t inputTMP;
				inputTMP.vertex = i.positionCS;
				inputTMP.faceColor = i.color;
				inputTMP.outlineColor = i.color; // TODO: separate param?
				inputTMP.texcoord0 = float4(i.uv, 0.0, 0.0); // TODO: actual mask UV?
				inputTMP.param = i.param;
				inputTMP.mask = 0;//i.mask;
				#if (UNDERLAY_ON | UNDERLAY_INNER)
				inputTMP.texcoord1 = 0;//i.texcoord1;
				inputTMP.underlayParam = 0;//i.underlayParam;
				#endif
				float4 c = PixShader(inputTMP, _MainTex, _UnderlayColor, _ClipRect, sampler_MainTex);

				// Sprite-Lit-Default
				const half4 main = c; //i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
				const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv);
				SurfaceData2D surfaceData;
				InputData2D inputData;

				InitializeSurfaceData(main.rgb, main.a, mask, surfaceData);
				InitializeInputData(i.uv, i.lightingUV, inputData);

				return CombinedShapeLightShared(surfaceData, inputData);
			}
			ENDHLSL
		}

		Pass
		{
			// Sprite-Lit-Default
			Tags { "LightMode" = "NormalsRendering"}

			HLSLPROGRAM
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "TextLitHelpers.hlsl"

			//#pragma enable_d3d11_debug_symbols // enable for RenderDoc debugging w/ source

			#pragma vertex NormalsRenderingVertex
			#pragma fragment NormalsRenderingFragment

			// TMP_SDF-Mobile
			#pragma shader_feature __ OUTLINE_ON
			#pragma shader_feature __ UNDERLAY_ON UNDERLAY_INNER

			#pragma multi_compile __ UNITY_UI_CLIP_RECT
			#pragma multi_compile __ UNITY_UI_ALPHACLIP

			// Sprite-Lit-Default
			struct Attributes
			{
				float4 positionOS   : POSITION; // upgraded from float3
				float4 color        : COLOR;
				float2 uv           : TEXCOORD0;
				float4 tangent      : TANGENT;
				UNITY_VERTEX_INPUT_INSTANCE_ID

				// TMP_SDF-Mobile
				float3	normal			: NORMAL;
				float4	texcoord1		: TEXCOORD1;	// Texture UV, alpha, reserved
			};

			struct Varyings
			{
				float4  positionCS      : SV_POSITION;
				half4   color           : COLOR;
				float2  uv              : TEXCOORD0;
				half3   normalWS        : TEXCOORD1;
				half3   tangentWS       : TEXCOORD2;
				half3   bitangentWS     : TEXCOORD3;
				UNITY_VERTEX_OUTPUT_STEREO

				// TMP_SDF-Mobile
				// TODO: include more TMP inputs?
				//UNITY_VERTEX_INPUT_INSTANCE_ID
				//fixed4	faceColor		: COLOR;
				//fixed4	outlineColor	: COLOR1;
				//float4	texcoord0		: TEXCOORD0;// Texture UV, Mask UV
				half4	param				: TEXCOORD4;	// Scale(x), BiasIn(y), BiasOut(z), Bias(w)
				//half4	mask				: TEXCOORD2;	// Position in clip space(xy), Softness(zw)
				//#if (UNDERLAY_ON | UNDERLAY_INNER)
				//float4	texcoord1		: TEXCOORD3;// Texture UV, alpha, reserved
				//half2	underlayParam		: TEXCOORD4;	// Scale(x), Bias(y)
				//#endif
			};

			// Sprite-Lit-Default
			TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);
			TEXTURE2D(_NormalMap);
			SAMPLER(sampler_NormalMap);
			half4 _NormalMap_ST;

			// TMP_SDF-Mobile
			float4 _FaceColor;
			float _FaceDilate;
			float4 _OutlineColor;
			float _OutlineWidth;
			float _OutlineSoftness;
			float _UnderlayColor;
			float _UnderlayOffsetX;
			float _UnderlayOffsetY;
			float _UnderlayDilate;
			float _UnderlaySoftness;
			float _WeightNormal;
			float _WeightBold;
			float _ScaleRatioA;
			float _ScaleRatioC;
			float _TextureWidth;
			float _TextureHeight;
			float _GradientScale;
			float _ScaleX;
			float _ScaleY;
			float _PerspectiveFilter;
			float _Sharpness;
			float _VertexOffsetX;
			float _VertexOffsetY;
			float _ClipRect;
			float _MaskSoftnessX;
			float _MaskSoftnessY;

			// Sprite-Lit-Default
			Varyings NormalsRenderingVertex(Attributes attributes)
			{
				Varyings o = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(attributes);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				o.positionCS = TransformObjectToHClip(attributes.positionOS);
				o.uv = TRANSFORM_TEX(attributes.uv, _NormalMap);
				//o.color = attributes.color;
				o.normalWS = -GetViewForwardDir();
				o.tangentWS = TransformObjectToWorldDir(attributes.tangent.xyz);
				o.bitangentWS = cross(o.normalWS, o.tangentWS) * attributes.tangent.w;

				// TMP_SDF-Mobile
				vertex_t inputTMP;
				inputTMP.vertex = attributes.positionOS;
				inputTMP.normal = attributes.normal;
				inputTMP.color = attributes.color;
				inputTMP.texcoord0 = attributes.uv;
				inputTMP.texcoord1 = attributes.texcoord1.xy;
				pixel_t outputTMP = VertShader(inputTMP, _FaceColor, _FaceDilate, _OutlineColor, _OutlineWidth, _OutlineSoftness, _UnderlayOffsetX, _UnderlayOffsetY, _UnderlayDilate, _UnderlaySoftness, _WeightNormal, _WeightBold, _ScaleRatioA, _ScaleRatioC, _TextureWidth, _TextureHeight, _GradientScale, _ScaleX, _ScaleY, _PerspectiveFilter, _Sharpness, _VertexOffsetX, _VertexOffsetY, _ClipRect, _MaskSoftnessX, _MaskSoftnessY);

				// TODO: process more outputs from TMP?
				// = outputTMP.vertex;
				o.color = outputTMP.faceColor;
				// = outputTMP.outlineColor;
				// = outputTMP.texcoord0;
				o.param = outputTMP.param;
				// = outputTMP.mask;
				#if (UNDERLAY_ON | UNDERLAY_INNER)
				// = outputTMP.texcoord1;
				// = outputTMP.underlayParam
				#endif

				return o;
			}

			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/NormalsRenderingShared.hlsl"

			half4 NormalsRenderingFragment(Varyings i) : SV_Target
			{
				// TMP_SDF-Mobile
				// TODO: fix artifacts and remove TEMP section below
				// TODO: actually supply more robust TMP inputs?
				//pixel_t inputTMP;
				//inputTMP.vertex = i.positionCS;
				//inputTMP.faceColor = i.color;
				//inputTMP.outlineColor = i.color; // TODO: separate param?
				//inputTMP.texcoord0 = float4(i.uv, 0.0, 0.0); // TODO: actual mask UV?
				//inputTMP.param = i.param;
				//inputTMP.mask = 0;//i.mask;
				//#if (UNDERLAY_ON | UNDERLAY_INNER)
				//inputTMP.texcoord1 = 0;//i.texcoord1;
				//inputTMP.underlayParam = 0;//i.underlayParam;
				//#endif
				//float4 c = PixShader(inputTMP, _MainTex, _UnderlayColor, _ClipRect, sampler_MainTex);

				// TEMP
				UNITY_SETUP_INSTANCE_ID(input);
				half d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy).a * i.param.x;
				half4 c = i.color;

				// Sprite-Lit-Default
				const half4 mainTex = c; //converted from "i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv)" to take TMP input
				const half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv));

				return NormalsRenderingShared(mainTex, normalTS, i.tangentWS.xyz, i.bitangentWS.xyz, i.normalWS.xyz);
			}
			ENDHLSL
		}

		Pass
		{
			// Sprite-Lit-Default
			Tags
			{
				"LightMode" = "UniversalForward"
				"Queue" = "Transparent"
				"RenderType" = "Transparent"
			}

			HLSLPROGRAM
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "TextLitHelpers.hlsl"

			//#pragma enable_d3d11_debug_symbols // enable for RenderDoc debugging w/ source

			#pragma vertex UnlitVertex
			#pragma fragment UnlitFragment

			struct Attributes
			{
				float4 positionOS   : POSITION; // upgraded from float3
				float4 color        : COLOR;
				float2 uv           : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID

				// TMP_SDF-Mobile
				float3	normal		: NORMAL;
				float4	texcoord1	: TEXCOORD1;	// Texture UV, alpha, reserved
			};

			struct Varyings
			{
				float4  positionCS      : SV_POSITION;
				float4  color           : COLOR;
				float2  uv              : TEXCOORD0;
				#if defined(DEBUG_DISPLAY)
				float3  positionWS  : TEXCOORD2;
				#endif
				UNITY_VERTEX_OUTPUT_STEREO

				// TMP_SDF-Mobile
				half4	param			: TEXCOORD1;	// Scale(x), BiasIn(y), BiasOut(z), Bias(w)
			};

			TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);
			float4 _MainTex_ST;

			// TMP_SDF-Mobile
			float4 _FaceColor;
			float _FaceDilate;
			float4 _OutlineColor;
			float _OutlineWidth;
			float _OutlineSoftness;
			float _UnderlayColor;
			float _UnderlayOffsetX;
			float _UnderlayOffsetY;
			float _UnderlayDilate;
			float _UnderlaySoftness;
			float _WeightNormal;
			float _WeightBold;
			float _ScaleRatioA;
			float _ScaleRatioC;
			float _TextureWidth;
			float _TextureHeight;
			float _GradientScale;
			float _ScaleX;
			float _ScaleY;
			float _PerspectiveFilter;
			float _Sharpness;
			float _VertexOffsetX;
			float _VertexOffsetY;
			float _ClipRect;
			float _MaskSoftnessX;
			float _MaskSoftnessY;

			// Sprite-Lit-Default
			Varyings UnlitVertex(Attributes attributes)
			{
				Varyings o = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(attributes);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				o.positionCS = TransformObjectToHClip(attributes.positionOS);
				#if defined(DEBUG_DISPLAY)
				o.positionWS = TransformObjectToWorld(v.positionOS);
				#endif
				o.uv = TRANSFORM_TEX(attributes.uv, _MainTex);
				//o.color = attributes.color;

				// TMP_SDF-Mobile
				vertex_t inputTMP;
				inputTMP.vertex = attributes.positionOS;
				inputTMP.normal = attributes.normal;
				inputTMP.color = attributes.color;
				inputTMP.texcoord0 = attributes.uv;
				inputTMP.texcoord1 = attributes.texcoord1.xy;
				pixel_t outputTMP = VertShader(inputTMP, _FaceColor, _FaceDilate, _OutlineColor, _OutlineWidth, _OutlineSoftness, _UnderlayOffsetX, _UnderlayOffsetY, _UnderlayDilate, _UnderlaySoftness, _WeightNormal, _WeightBold, _ScaleRatioA, _ScaleRatioC, _TextureWidth, _TextureHeight, _GradientScale, _ScaleX, _ScaleY, _PerspectiveFilter, _Sharpness, _VertexOffsetX, _VertexOffsetY, _ClipRect, _MaskSoftnessX, _MaskSoftnessY);

				// TODO: process more outputs from TMP?
				// = outputTMP.vertex;
				o.color = outputTMP.faceColor;
				// = outputTMP.outlineColor;
				// = outputTMP.texcoord0;
				o.param = outputTMP.param;
				// = outputTMP.mask;
				//#if (UNDERLAY_ON | UNDERLAY_INNER)
				// = outputTMP.texcoord1;
				// = outputTMP.underlayParam
				//#endif

				return o;
			}

			float4 UnlitFragment(Varyings i) : SV_Target
			{
				// TMP_SDF-Mobile
				// TODO: actually supply more robust TMP inputs?
				pixel_t inputTMP;
				inputTMP.vertex = i.positionCS;
				inputTMP.faceColor = i.color;
				inputTMP.outlineColor = i.color; // TODO: separate param?
				inputTMP.texcoord0 = float4(i.uv, 0.0, 0.0); // TODO: actual mask UV?
				inputTMP.param = i.param;
				inputTMP.mask = 0;//i.mask;
				#if (UNDERLAY_ON | UNDERLAY_INNER)
				inputTMP.texcoord1 = 0;//i.texcoord1;
				inputTMP.underlayParam = 0;//i.underlayParam;
				#endif
				float4 c = PixShader(inputTMP, _MainTex, _UnderlayColor, _ClipRect, sampler_MainTex);

				// Sprite-Lit-Default
				float4 mainTex = c; //i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

				#if defined(DEBUG_DISPLAY)
				SurfaceData2D surfaceData;
				InputData2D inputData;
				half4 debugColor = 0;

				InitializeSurfaceData(mainTex.rgb, mainTex.a, surfaceData);
				InitializeInputData(i.uv, inputData);
				SETUP_DEBUG_DATA_2D(inputData, i.positionWS);

				if (CanDebugOverrideOutputColor(surfaceData, inputData, debugColor)) {
					return debugColor;
				}
				#endif

				return mainTex;
			}
			ENDHLSL
		}
	}

	Fallback "TextMeshPro/Mobile/Distance Field"
}
