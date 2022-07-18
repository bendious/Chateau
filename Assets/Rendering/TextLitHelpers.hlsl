// NOTE that these helpers are copied from TMP_SDF-Mobile.shader (w/ slight conversion from CG to HLSL) for use in multiple passes in TextLit.shader

struct vertex_t {
	UNITY_VERTEX_INPUT_INSTANCE_ID
	float4	vertex			: POSITION;
	float3	normal			: NORMAL;
	float4	color			: COLOR; // converted from fixed4
	float2	texcoord0		: TEXCOORD0;
	float2	texcoord1		: TEXCOORD1;
};

struct pixel_t {
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
	float4	vertex			: SV_POSITION;
	float4	faceColor		: COLOR; // converted from fixed4
	float4	outlineColor	: COLOR1; // converted from fixed4
	float4	texcoord0		: TEXCOORD0;			// Texture UV, Mask UV
	half4	param			: TEXCOORD1;			// Scale(x), BiasIn(y), BiasOut(z), Bias(w)
	half4	mask			: TEXCOORD2;			// Position in clip space(xy), Softness(zw)
#if (UNDERLAY_ON | UNDERLAY_INNER)
	float4	texcoord1		: TEXCOORD3;			// Texture UV, alpha, reserved
	half2	underlayParam	: TEXCOORD4;			// Scale(x), Bias(y)
#endif
};

pixel_t VertShader(vertex_t input, float4 faceColorIn, float faceDilate, float4 outlineColorIn, float outlineWidth, float outlineSoftness, float underlayOffsetX, float underlayOffsetY, float underlayDilate, float underlaySoftness, float weightNormal, float weightBold, float scaleRatioA, float scaleRatioC, float textureWidth, float textureHeight, float gradientScale, float scaleX, float scaleY, float perspectiveFilter, float sharpness, float vertexOffsetX, float vertexOffsetY, vector clipRect, float maskSoftnessX, float maskSoftnessY)
{
	pixel_t output;

	//UNITY_INITIALIZE_OUTPUT(pixel_t, output);
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	float bold = step(input.texcoord1.y, 0);

	float4 vert = input.vertex;
	vert.x += vertexOffsetX;
	vert.y += vertexOffsetY;
	float4 vPosition = TransformObjectToHClip(vert); // converted from UnityObjectToClipPos()

	float2 pixelSize = vPosition.w;
	pixelSize /= float2(scaleX, scaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

	float scale = rsqrt(dot(pixelSize, pixelSize));
	scale *= abs(input.texcoord1.y) * gradientScale * (sharpness + 1);
	if (UNITY_MATRIX_P[3][3] == 0) scale = lerp(abs(scale) * (1 - perspectiveFilter), scale, abs(dot(TransformObjectToWorldNormal(input.normal.xyz), normalize(GetWorldSpaceViewDir(vert))))); // converted from UnityObjectToWorldNormal()

	float weight = lerp(weightNormal, weightBold, bold) / 4.0;
	weight = (weight + faceDilate) * scaleRatioA * 0.5;

	float layerScale = scale;

	scale /= 1 + (outlineSoftness * scaleRatioA * scale);
	float bias = (0.5 - weight) * scale - 0.5;
	float outline = outlineWidth * scaleRatioA * 0.5 * scale;

	float opacity = input.color.a;
#if (UNDERLAY_ON | UNDERLAY_INNER)
	opacity = 1.0;
#endif

	float4 faceColor = float4(input.color.rgb, opacity) * faceColorIn; // converted from fixed4
	faceColor.rgb *= faceColor.a;

	float4 outlineColor = outlineColorIn; // converted from fixed4
	outlineColor.a *= opacity;
	outlineColor.rgb *= outlineColor.a;
	outlineColor = lerp(faceColor, outlineColor, sqrt(min(1.0, (outline * 2))));

#if (UNDERLAY_ON | UNDERLAY_INNER)
	layerScale /= 1 + ((underlaySoftness * scaleRatioC) * layerScale);
	float layerBias = (.5 - weight) * layerScale - .5 - ((underlayDilate * scaleRatioC) * .5 * layerScale);

	float x = -(underlayOffsetX * scaleRatioC) * gradientScale / textureWidth;
	float y = -(underlayOffsetY * scaleRatioC) * gradientScale / textureHeight;
	float2 layerOffset = float2(x, y);
#endif

	// Generate UV for the Masking Texture
	float4 clampedRect = clamp(clipRect, -2e10, 2e10);
	float2 maskUV = (vert.xy - clampedRect.xy) / (clampedRect.zw - clampedRect.xy);

	// Populate structure for pixel shader
	output.vertex = vPosition;
	output.faceColor = faceColor;
	output.outlineColor = outlineColor;
	output.texcoord0 = float4(input.texcoord0.x, input.texcoord0.y, maskUV.x, maskUV.y);
	output.param = half4(scale, bias - outline, bias + outline, bias);
	output.mask = half4(vert.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(maskSoftnessX, maskSoftnessY) + pixelSize.xy));
#if (UNDERLAY_ON || UNDERLAY_INNER)
	output.texcoord1 = float4(input.texcoord0 + layerOffset, input.color.a, 0);
	output.underlayParam = half2(layerScale, layerBias);
#endif

	return output;
}


// PIXEL SHADER
float4 PixShader(pixel_t input, Texture2D _MainTex, float4 _UnderlayColor, vector _ClipRect, sampler sampler_MainTex) // converted from fixed4
{
	UNITY_SETUP_INSTANCE_ID(input);

	half d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.texcoord0.xy).a * input.param.x; // converted from tex2D()
	half4 c = input.faceColor * saturate(d - input.param.w);

	#ifdef OUTLINE_ON
	c = lerp(input.outlineColor, input.faceColor, saturate(d - input.param.z));
	c *= saturate(d - input.param.y);
	#endif

	#if UNDERLAY_ON
	d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.texcoord1.xy).a * input.underlayParam.x; // converted from tex2D()
	c += float4(_UnderlayColor.rgb * _UnderlayColor.a, _UnderlayColor.a) * saturate(d - input.underlayParam.y) * (1 - c.a);
	#endif

	#if UNDERLAY_INNER
	half sd = saturate(d - input.param.z);
	d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.texcoord1.xy).a * input.underlayParam.x; // converted from tex2D()
	c += float4(_UnderlayColor.rgb * _UnderlayColor.a, _UnderlayColor.a) * (1 - saturate(d - input.underlayParam.y)) * sd * (1 - c.a);
	#endif

	// Alternative implementation to UnityGet2DClipping with support for softness.
	#if UNITY_UI_CLIP_RECT
	half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
	c *= m.x * m.y;
	#endif

	#if (UNDERLAY_ON | UNDERLAY_INNER)
	c *= input.texcoord1.z;
	#endif

	#if UNITY_UI_ALPHACLIP
	clip(c.a - 0.001);
	#endif

	return c;
}
