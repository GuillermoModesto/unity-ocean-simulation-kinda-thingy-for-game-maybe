Shader "Hidden/ClearContactMask"
{
    SubShader { Pass {
        ZTest Always Cull Off ZWrite Off
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float _Persistence;
        fixed4 frag(v2f_img i):SV_Target { return tex2D(_MainTex,i.uv) * _Persistence; }
        ENDCG
    }}
}
