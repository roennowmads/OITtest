
Shader "Unlit/SortingTestGLSLPoints" { // defines the name of the shader 

	SubShader { // Unity chooses the subshader that fits the GPU best
		Tags{ "QUEUE" = "Transparent" "IGNOREPROJECTOR" = "true" "RenderType" = "Transparent" }

		//Blend SrcAlpha OneMinusSrcAlpha
		//ZWrite Off

		Pass { // some shaders require multiple passes
			ColorMask 0

			GLSLPROGRAM
			#version 310 es

	#ifdef VERTEX
			//#version 310 es

			/*const vec4 quadCoordsAndTexCoords[6] = vec4[6] (
			vec4(-1.0, -1.0, 0.0, 0.0),
			vec4(1.0, 1.0, 1.0, 1.0),
			vec4(1.0, -1.0, 1.0, 0.0),

			vec4(-1.0, 1.0, 0.0, 1.0),
			vec4(1.0, 1.0, 1.0, 1.0),
			vec4(-1.0, -1.0, 0.0, 0.0)
			);*/

			/*const vec2 quadCoordsAndTexCoords[6] = vec2[6] (
			vec2(0.0, 0.0),
			vec2(1.0, 1.0),
			vec2(1.0, 0.0),

			vec2(0.0, 1.0),
			vec2(1.0, 1.0),
			vec2(0.0, 0.0)
			);*/


			uniform 	float aspect;
			uniform lowp sampler2D _ColorTex;
			uniform lowp float pointSizeScale;
			uniform lowp float pointSizeScaleIndependent;
			uniform lowp float orthographicSizeScale;
			struct _Points_type {
				float[3] value;
				//float value1;
				//float value2;
				//float value3;
				//vec3 value;
			};

			layout(std430, binding = 0) readonly buffer _Points {
				_Points_type _Points_buf[];
			};

			layout(std430, binding = 1) readonly buffer _IndicesValues {
				uint _IndicesValues_buf[];
			};
			out mediump vec3 vs_COLOR0;
			out highp vec2 vs_TEXCOORD0;

			//out highp vec4 pos;

			const float inv6 = 1.0 / 6.0;
			const float inv255 = 1.0 / 255.0;

			vec2 colorCoords = vec2(0.0);

			void main()
			{
				int quadId = gl_VertexID;//int(float(gl_VertexID) * inv6);
				int quad_vertexID = /*-6 * quadId +*/ gl_VertexID;

				//bvec4 bits = equal(ivec4(quad_vertexID), ivec4(1, 4, 2, 3));
				//bool bit = bits.x || bits.y;
				//vec2 quadCoordsAndTexCoord = vec2(bit || bits.z, bit || bits.w);
				//vec2 quadCoordsAndTexCoord = vec2(bit || bits.z, bit || bits.w) * 2.0 - 1.0;

				//vec2 quadCoordsAndTexCoord = quadCoordsAndTexCoords[quad_vertexID];
				//vec4 quadCoordsAndTexCoord = quadCoordsAndTexCoords[quad_vertexID];

				float size = 0.352;//0.02;
				vec2 quadSize = vec2(size, size * aspect);

				//vec2 deltaSize = (quadCoordsAndTexCoord * 2.0 - 1.0) * quadSize;
				//vec2 deltaSize = quadCoordsAndTexCoord * quadSize;
				//vec2 deltaSize = quadCoordsAndTexCoord.xy * quadSize;

				uint value = _IndicesValues_buf[quadId];

				uint index = value >> 8u;
				float colorValue = float(value & 255u) * inv255;

				if (colorValue == 0.0) {
					return;
				}

				//vec3 pos =  vec3(_Points_buf[index].value);
				//vec3 pos =  vec3(_Points_buf[index].value1, _Points_buf[index].value2, _Points_buf[index].value3); 

				vec4 position = /*vec4(pos, -1.0);*/vec4(_Points_buf[index].value[0], _Points_buf[index].value[1], _Points_buf[index].value[2], -1.0);



				//vs_TEXCOORD0 = quadCoordsAndTexCoord + vec2(0.02, -0.015);
				//vs_TEXCOORD0 = quadCoordsAndTexCoord.zw;

				gl_Position = gl_ModelViewProjectionMatrix * (-position);

				//pos = gl_Position;

				//gl_Position.xy += deltaSize;

				//gl_PointSize = 100.0 * (gl_Position.z / gl_Position.w);

				float radius = 1.0;

				gl_PointSize = /*pointSizeScaleIndependent*pointSizeScale**/ 0.25*5.0 * gl_ProjectionMatrix[1][1] * radius / gl_Position.w; //Tango device 

				//gl_PointSize = pointSizeScaleIndependent*pointSizeScale* 511.0 * gl_ProjectionMatrix[1][1] * radius / gl_Position.w; //Tango device 
				//gl_PointSize = pointSizeScaleIndependent*pointSizeScale* 402.0 * gl_ProjectionMatrix[1][1] * orthographicSizeScale * radius / gl_Position.w; // Tango tablet // for orthographic 

				//gl_PointSize = pointSizeScaleIndependent*pointSizeScale*382.0 * gl_ProjectionMatrix[1][1] * orthographicSizeScale * radius / gl_Position.w; //Tegra tablet // for orthographic 
				//gl_PointSize = /*pointSizeScaleIndependent*pointSizeScale**/382.0 * gl_ProjectionMatrix[1][1] * radius / gl_Position.w; //Tegra tablet


				colorCoords.x = pow((colorValue), .0625);
				vs_COLOR0 = textureLod(_ColorTex, colorCoords, 0.0).xyz;
			}

		#endif
		#ifdef FRAGMENT

			uniform highp sampler2D _AlbedoTex;

			uniform 	vec4 _ScreenParams;

			//layout(early_fragment_tests) in;

			in mediump vec3 vs_COLOR0;
			in highp vec2 vs_TEXCOORD0;
			//in highp vec4 pos;

			layout(location = 0) out highp vec4 SV_Target0;

			void main()
			{
				//float albedo = length(gl_FragCoord.xy);

				//float albedo = length(gl_PointCoord.xy);

				//float c = pos.x;//1.0 - distance(pos.xy / pos.w, gl_FragCoord.xy / _ScreenParams.xy);
				//vec2 c = gl_FragCoord.xy / _ScreenParams.xy;
				//c = (c - 0.5) * 2.0;

				//float d = length(pos.xy - (gl_FragCoord.xy / _ScreenParams.xy)) - 10.0;
				//float t = d;//clamp(d, 0.0, 1.0);
				//float albedo = 1.0 - t;

				//adjusting point size so it fits with the original texture lookup. (so we can easily do performance comparison)

				//float albedo = 1.0 - pow(dot(vs_TEXCOORD0, vs_TEXCOORD0), 0.72);
				//float albedo = 1.0 - length(vs_TEXCOORD0);

				float albedo = textureLod(_AlbedoTex, vec2(gl_PointCoord.x + 0.02, gl_PointCoord.y - 0.02), 0.0).a;
				if (albedo < 0.325) {
					discard;
				}

				SV_Target0.xyz = vs_COLOR0;
				//SV_Target0.xyz = vec3(c);//vec3(gl_FragCoord.xy / _ScreenParams.xy, 0.0);//vs_COLOR0;
				SV_Target0.w = albedo*0.25;//0.1;//0.1;//albedo*0.25;

			}

		#endif

			ENDGLSL

		}


		Pass { // some shaders require multiple passes

			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off

			GLSLPROGRAM
			#version 310 es

	#ifdef VERTEX
			//#version 310 es

			/*const vec4 quadCoordsAndTexCoords[6] = vec4[6] (
			vec4(-1.0, -1.0, 0.0, 0.0),
			vec4(1.0, 1.0, 1.0, 1.0),
			vec4(1.0, -1.0, 1.0, 0.0),

			vec4(-1.0, 1.0, 0.0, 1.0),
			vec4(1.0, 1.0, 1.0, 1.0),
			vec4(-1.0, -1.0, 0.0, 0.0)
			);*/

			/*const vec2 quadCoordsAndTexCoords[6] = vec2[6] (
			vec2(0.0, 0.0),
			vec2(1.0, 1.0),
			vec2(1.0, 0.0),

			vec2(0.0, 1.0),
			vec2(1.0, 1.0),
			vec2(0.0, 0.0)
			);*/


			uniform 	float aspect;
			uniform lowp sampler2D _ColorTex;
			uniform lowp float pointSizeScale;
			uniform lowp float pointSizeScaleIndependent;
			uniform lowp float orthographicSizeScale;
			struct _Points_type {
				float[3] value;
				//float value1;
				//float value2;
				//float value3;
				//vec3 value;
			};

			layout(std430, binding = 0) readonly buffer _Points {
				_Points_type _Points_buf[];
			};

			layout(std430, binding = 1) readonly buffer _IndicesValues {
				uint _IndicesValues_buf[];
			};
			out mediump vec3 vs_COLOR0;
			out highp vec2 vs_TEXCOORD0;

			//out highp vec4 pos;

			const float inv6 = 1.0 / 6.0;
			const float inv255 = 1.0 / 255.0;

			vec2 colorCoords = vec2(0.0);

			void main()
			{
				int quadId = gl_VertexID;//int(float(gl_VertexID) * inv6);
				int quad_vertexID = /*-6 * quadId +*/ gl_VertexID;

				//bvec4 bits = equal(ivec4(quad_vertexID), ivec4(1, 4, 2, 3));
				//bool bit = bits.x || bits.y;
				//vec2 quadCoordsAndTexCoord = vec2(bit || bits.z, bit || bits.w);
				//vec2 quadCoordsAndTexCoord = vec2(bit || bits.z, bit || bits.w) * 2.0 - 1.0;

				//vec2 quadCoordsAndTexCoord = quadCoordsAndTexCoords[quad_vertexID];
				//vec4 quadCoordsAndTexCoord = quadCoordsAndTexCoords[quad_vertexID];

				float size = 0.352;//0.02;
				vec2 quadSize = vec2(size, size * aspect);

				//vec2 deltaSize = (quadCoordsAndTexCoord * 2.0 - 1.0) * quadSize;
				//vec2 deltaSize = quadCoordsAndTexCoord * quadSize;
				//vec2 deltaSize = quadCoordsAndTexCoord.xy * quadSize;

				uint value = _IndicesValues_buf[quadId];

				uint index = value >> 8u;
				float colorValue = float(value & 255u) * inv255;
				
				if (colorValue == 0.0) {
					return;
				}

				//vec3 pos =  vec3(_Points_buf[index].value);
				//vec3 pos =  vec3(_Points_buf[index].value1, _Points_buf[index].value2, _Points_buf[index].value3); 

				vec4 position = /*vec4(pos, -1.0);*/vec4(_Points_buf[index].value[0], _Points_buf[index].value[1], _Points_buf[index].value[2], -1.0);



				//vs_TEXCOORD0 = quadCoordsAndTexCoord + vec2(0.02, -0.015);
				//vs_TEXCOORD0 = quadCoordsAndTexCoord.zw;

				gl_Position = gl_ModelViewProjectionMatrix * (-position);

				//pos = gl_Position;

				//gl_Position.xy += deltaSize;

				//gl_PointSize = 100.0 * (gl_Position.z / gl_Position.w);

				float radius = 1.0;

				gl_PointSize = /*pointSizeScaleIndependent*pointSizeScale**/ 0.25*5.0 * gl_ProjectionMatrix[1][1] * radius / gl_Position.w; //Tango device 

				//gl_PointSize = pointSizeScaleIndependent*pointSizeScale* 511.0 * gl_ProjectionMatrix[1][1] * radius / gl_Position.w; //Tango device 
				//gl_PointSize = pointSizeScaleIndependent*pointSizeScale* 402.0 * gl_ProjectionMatrix[1][1] * orthographicSizeScale * radius / gl_Position.w; // Tango tablet // for orthographic 

				//gl_PointSize = pointSizeScaleIndependent*pointSizeScale*382.0 * gl_ProjectionMatrix[1][1] * orthographicSizeScale * radius / gl_Position.w; //Tegra tablet // for orthographic 
				//gl_PointSize = /*pointSizeScaleIndependent*pointSizeScale**/382.0 * gl_ProjectionMatrix[1][1] * radius / gl_Position.w; //Tegra tablet


				colorCoords.x = pow((colorValue), .0625);
				vs_COLOR0 = textureLod(_ColorTex, colorCoords, 0.0).xyz;
			}

		#endif
		#ifdef FRAGMENT

			uniform highp sampler2D _AlbedoTex;

			uniform 	vec4 _ScreenParams;

			//layout(early_fragment_tests) in;

			in mediump vec3 vs_COLOR0;
			in highp vec2 vs_TEXCOORD0;
			//in highp vec4 pos;

			layout(location = 0) out highp vec4 SV_Target0;

			void main()
			{
				//float albedo = length(gl_FragCoord.xy);

				//float albedo = length(gl_PointCoord.xy);

				//float c = pos.x;//1.0 - distance(pos.xy / pos.w, gl_FragCoord.xy / _ScreenParams.xy);
				//vec2 c = gl_FragCoord.xy / _ScreenParams.xy;
				//c = (c - 0.5) * 2.0;

				//float d = length(pos.xy - (gl_FragCoord.xy / _ScreenParams.xy)) - 10.0;
				//float t = d;//clamp(d, 0.0, 1.0);
				//float albedo = 1.0 - t;

				//adjusting point size so it fits with the original texture lookup. (so we can easily do performance comparison)

				//float albedo = 1.0 - pow(dot(vs_TEXCOORD0, vs_TEXCOORD0), 0.72);
				//float albedo = 1.0 - length(vs_TEXCOORD0);

				float albedo = textureLod(_AlbedoTex, vec2(gl_PointCoord.x + 0.02, gl_PointCoord.y - 0.02), 0.0).a;
				if (albedo < 0.325) {
					discard;
				}

				SV_Target0.xyz = vs_COLOR0;
				//SV_Target0.xyz = vec3(c);//vec3(gl_FragCoord.xy / _ScreenParams.xy, 0.0);//vs_COLOR0;
				SV_Target0.w = albedo*0.25;//0.1;//0.1;//albedo*0.25;

			}

		#endif

			ENDGLSL

		}
	}
}