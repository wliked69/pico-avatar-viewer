Shader "AvatarViewer/PortableToon"
{
    Properties
    {
        _MainTex ("Base texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0
        _SrcBlend ("Source blend", Float) = 1
        _DstBlend ("Destination blend", Float) = 0
        _ZWrite ("Depth write", Float) = 1
        _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull [_Cull] ZWrite [_ZWrite] Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 head:TEXCOORD2; float3 normal:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 normal:TEXCOORD1; float head:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex; float4 _MainTex_ST,_Color; float _Cutoff,_AvatarFirstPerson,_AvatarHeadObject,_AvatarMirror;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=UnityObjectToClipPos(v.vertex); o.uv=TRANSFORM_TEX(v.uv,_MainTex); o.normal=UnityObjectToWorldNormal(v.normal); o.head=max(v.head.x,_AvatarHeadObject); return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if(_AvatarFirstPerson>.5 && _AvatarMirror<.5) clip(.75-i.head);
                fixed4 c=tex2D(_MainTex,i.uv)*_Color; clip(c.a-_Cutoff);
                float light=dot(normalize(i.normal),normalize(_WorldSpaceLightPos0.xyz));
                c.rgb *= lerp(.65,1.05,smoothstep(-.15,.4,light)); return c;
            }
            ENDCG
        }
    }
}
