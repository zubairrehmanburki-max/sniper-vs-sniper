#ifndef SNIPERDUEL_SWAY_WIND
#define SNIPERDUEL_SWAY_WIND

// The lane wind, set by the game: xy = direction it blows toward (world xz, unit), z = speed (m/s).
float4 _SniperWind;

// How far a plant's vertex at picture height v (0 foot, 1 top) moves: rooted at the foot (v²),
// a slow sway that grows with the wind speed and leans downwind, and a quick flutter on top.
// sway.x is the most the top may move at a strong wind (m), sway.y the plant's own phase.
float3 SwayOffset(float3 ws, float v, float2 sway)
{
    float speed = _SniperWind.z;
    float2 dir = _SniperWind.xy;
    float strength = saturate(speed / 9.0);
    float t = _Time.y;
    float phase = sway.y + ws.x * 0.07 + ws.z * 0.05;
    float slow = 0.55 + 0.45 * sin(t * (0.9 + strength) + phase);
    float gust = 0.5 + 0.5 * sin(t * 0.23 + ws.x * 0.011 + ws.z * 0.007);
    float flutter = sin(t * 6.3 + phase * 3.1) * 0.12;
    float bend = sway.x * v * v * (0.15 + strength * (slow + gust * 0.6));
    float3 lean = float3(dir.x, 0, dir.y) * bend;
    float3 side = float3(-dir.y, 0, dir.x) * (bend * flutter);
    return lean + side + float3(0, -bend * bend * 0.15, 0);
}

#endif
