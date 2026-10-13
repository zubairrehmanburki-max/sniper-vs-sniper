/**
 * The post-processing chain: scene → bloom (sun glitter, the flare stack, muzzle flash) → a grade
 * pass (vignette, a touch of contrast, and the scope's edge softening) → tone mapping and sRGB.
 * Renders into a multisampled target so edges stay smooth.
 */
import * as THREE from 'three';
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js';
import { OutputPass } from 'three/examples/jsm/postprocessing/OutputPass.js';
import { RenderPass } from 'three/examples/jsm/postprocessing/RenderPass.js';
import { ShaderPass } from 'three/examples/jsm/postprocessing/ShaderPass.js';
import { UnrealBloomPass } from 'three/examples/jsm/postprocessing/UnrealBloomPass.js';

export interface PostChain {
  setScene(scene: THREE.Scene, camera: THREE.Camera): void;
  setSize(w: number, h: number, pixelRatio: number): void;
  /** 0 = eye view, 1 = looking through the scope (soft edge and slight blur at the rim). */
  setScoped(k: number): void;
  render(dt: number): void;
}

const GradeShader = {
  uniforms: {
    tDiffuse: { value: null as THREE.Texture | null },
    uScoped: { value: 0 },
    uAspect: { value: 1 },
  },
  vertexShader: `
    varying vec2 vUv;
    void main() { vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }`,
  fragmentShader: `
    uniform sampler2D tDiffuse; uniform float uScoped; uniform float uAspect;
    varying vec2 vUv;
    void main() {
      vec2 c = (vUv - 0.5) * vec2(uAspect, 1.0);
      float r = length(c);
      // Through the scope the image softens and separates slightly towards the rim of the glass.
      float rim = uScoped * smoothstep(0.25, 0.5, r);
      vec2 dir = normalize(c + 1e-5) / vec2(uAspect, 1.0);
      vec4 col = texture2D(tDiffuse, vUv);
      if (rim > 0.0) {
        float d = rim * 0.004;
        col.r = mix(col.r, texture2D(tDiffuse, vUv + dir * d).r, rim);
        col.b = mix(col.b, texture2D(tDiffuse, vUv - dir * d).b, rim);
        vec4 blur = (texture2D(tDiffuse, vUv + vec2(d, 0.0)) + texture2D(tDiffuse, vUv - vec2(d, 0.0))
                   + texture2D(tDiffuse, vUv + vec2(0.0, d)) + texture2D(tDiffuse, vUv - vec2(0.0, d))) * 0.25;
        col = mix(col, blur, rim * 0.7);
      }
      float vignette = 1.0 - smoothstep(0.45, 0.95, r) * mix(0.35, 0.0, uScoped);
      col.rgb *= vignette;
      gl_FragColor = col;
    }`,
};

export function buildPostChain(renderer: THREE.WebGLRenderer): PostChain {
  const size = renderer.getSize(new THREE.Vector2());
  const samples = renderer.capabilities.isWebGL2 ? 4 : 0;
  const target = new THREE.WebGLRenderTarget(size.x, size.y, { type: THREE.HalfFloatType, samples });
  const composer = new EffectComposer(renderer, target);
  let renderPass: RenderPass | null = null;
  // Only the sun, the flare stack and the brightest glitter cross the threshold.
  const bloom = new UnrealBloomPass(new THREE.Vector2(size.x, size.y), 0.3, 0.25, 3.0);
  const grade = new ShaderPass(GradeShader);
  const output = new OutputPass();
  let scoped = 0;
  let scopedTarget = 0;
  return {
    setScene(scene, camera) {
      if (renderPass) composer.removePass(renderPass);
      renderPass = new RenderPass(scene, camera);
      composer.passes.length = 0;
      composer.addPass(renderPass);
      composer.addPass(bloom);
      composer.addPass(grade);
      composer.addPass(output);
    },
    setSize(w, h, pixelRatio) {
      composer.setPixelRatio(pixelRatio);
      composer.setSize(w, h);
      grade.uniforms.uAspect!.value = w / h;
    },
    setScoped(k) {
      scopedTarget = k;
    },
    render(dt) {
      scoped += (scopedTarget - scoped) * Math.min(1, dt * 12);
      grade.uniforms.uScoped!.value = scoped;
      composer.render(dt);
    },
  };
}
