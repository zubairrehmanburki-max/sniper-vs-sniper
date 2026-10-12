/**
 * Open water: a flat plane whose surface normal ripples in the shader (a few sine waves summed),
 * reflecting the sky by Fresnel with a sun glint. Cheap enough for phones: one quad, no render
 * targets.
 */
import * as THREE from 'three';

export interface WaterSurface {
  mesh: THREE.Mesh;
  update(time: number, wind: { x: number; z: number }, cameraPos: THREE.Vector3): void;
}

export function buildWater(level: number, size: number, sunDir: THREE.Vector3, colours: { deep: THREE.Color; shallow: THREE.Color; horizon: THREE.Color; zenith: THREE.Color }, fog: THREE.Fog | null): WaterSurface {
  const uniforms = {
    uTime: { value: 0 },
    uSun: { value: sunDir.clone() },
    uDeep: { value: colours.deep },
    uShallow: { value: colours.shallow },
    uHorizon: { value: colours.horizon },
    uZenith: { value: colours.zenith },
    uWind: { value: new THREE.Vector2(1, 0) },
    uChop: { value: 1 },
    ...THREE.UniformsUtils.clone(THREE.UniformsLib.fog),
  };
  const material = new THREE.ShaderMaterial({
    uniforms,
    fog: true,
    vertexShader: `
      #include <fog_pars_vertex>
      varying vec3 vWorld;
      void main() {
        vec4 wp = modelMatrix * vec4(position, 1.0);
        vWorld = wp.xyz;
        vec4 mvPosition = viewMatrix * wp;
        gl_Position = projectionMatrix * mvPosition;
        #include <fog_vertex>
      }`,
    fragmentShader: `
      #include <fog_pars_fragment>
      uniform float uTime; uniform vec3 uSun; uniform vec3 uDeep; uniform vec3 uShallow;
      uniform vec3 uHorizon; uniform vec3 uZenith; uniform vec2 uWind; uniform float uChop;
      varying vec3 vWorld;
      void wave(vec2 p, vec2 dir, float freq, float amp, float speed, inout vec2 grad) {
        float ph = dot(p, dir) * freq + uTime * speed;
        grad += dir * (amp * freq * cos(ph));
      }
      void main() {
        vec2 p = vWorld.xz;
        vec2 w = normalize(uWind + vec2(0.001, 0.0));
        vec2 w2 = vec2(-w.y, w.x);
        float dist = length(cameraPosition - vWorld);
        // Fade the ripples out with distance so the far water does not sparkle with aliasing.
        float fade = 1.0 - smoothstep(300.0, 2200.0, dist);
        vec2 grad = vec2(0.0);
        wave(p, w, 0.9, 0.08 * uChop, 1.6, grad);
        wave(p, normalize(w + 0.6 * w2), 1.7, 0.05 * uChop, 2.3, grad);
        wave(p, normalize(w - 0.5 * w2), 3.1, 0.025 * uChop, 3.1, grad);
        wave(p, normalize(w + 0.2 * w2), 6.3, 0.012 * uChop, 4.4, grad);
        wave(p, w2, 11.0, 0.006 * uChop, 5.0, grad);
        grad *= fade;
        vec3 n = normalize(vec3(-grad.x, 1.0, -grad.y));
        vec3 v = normalize(cameraPosition - vWorld);
        float ndv = max(dot(n, v), 0.0);
        float fresnel = 0.04 + 0.96 * pow(1.0 - ndv, 5.0);
        vec3 r = reflect(-v, n);
        vec3 skyRefl = mix(uHorizon, uZenith, clamp(r.y * 2.2, 0.0, 1.0));
        vec3 body = mix(uDeep, uShallow, 0.35 + 0.35 * ndv);
        float glint = pow(max(dot(r, normalize(uSun)), 0.0), 420.0) * 3.0 * (0.4 + 0.6 * fade);
        float sheen = pow(max(dot(r, normalize(uSun)), 0.0), 12.0) * 0.12;
        vec3 color = mix(body, skyRefl, fresnel) + vec3(1.0, 0.96, 0.85) * (glint + sheen);
        gl_FragColor = vec4(color, 1.0);
        #include <tonemapping_fragment>
        #include <colorspace_fragment>
        #include <fog_fragment>
      }`,
  });
  if (fog) {
    uniforms.fogColor.value = fog.color;
    uniforms.fogNear.value = fog.near;
    uniforms.fogFar.value = fog.far;
  }
  const geo = new THREE.PlaneGeometry(size, size, 1, 1);
  geo.rotateX(-Math.PI / 2);
  const mesh = new THREE.Mesh(geo, material);
  mesh.position.y = level;
  mesh.frustumCulled = false;
  return {
    mesh,
    update(time, wind, cameraPos) {
      uniforms.uTime.value = time;
      const speed = Math.hypot(wind.x, wind.z);
      if (speed > 0.05) uniforms.uWind.value.set(wind.x / speed, wind.z / speed);
      uniforms.uChop.value = 0.5 + Math.min(1.5, speed * 0.18);
      mesh.position.x = cameraPos.x;
      mesh.position.z = cameraPos.z;
    },
  };
}
