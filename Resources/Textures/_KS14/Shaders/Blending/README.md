# Blend shaders

## Why use this, instead of RT's built-in `blend_mode`?

Because that operates on the texture *after* all main rendering has been complete; so after all entities, ***lights***, etc..
What we want is, imagine:

layer 1: no custom blending
layer 2: multiply blending
layer 3: add blending

RT would do:

render everything before this
render layer 1, 2 3
render everything after this
render lighting
apply blend modes with the layer textures onto the final product; this gives very shitty results

What we want:

render everything before this
render layer 1
render layer 2; multiplying it with the rendered texture of layer 1
render layer 3; adding it to the rendered texture of layer 2 (which is layer 1 & 2)
render everything after this
render lighting
win

## What can we do????????????????????

Some shader files start with `preset raw`
There are 2 presets: default and raw
- default: the fragment() function is fed full nearly-finished framebuffer data; affected by lightning, etc
- raw: the fragment() function is fed only the texture/sprite at hand and nothing else; what we want for these byond-style blendmodes
