Shader "Custom/BufferRender" {

Properties {

    _Color ("Main Color", Color) = (1,1,1,1)

    _MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}

}

SubShader {

    Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}

    LOD 200
    Cull Off


    // paste in forward rendering passes from Transparent/Diffuse

    UsePass "Transparent/Diffuse/FORWARD"

}

}