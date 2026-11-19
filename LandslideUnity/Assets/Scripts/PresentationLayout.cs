using UnityEngine;

namespace LandslideDemo
{
    // Coordinates in the solver remain rectilinear. This layout is used only
    // when placing scene geometry and provides an inverse for display picking.
    public static class PresentationLayout
    {
        public static float Bell(float value, float center, float radius)
        {
            float distance = (value - center) / radius;
            if (Mathf.Abs(distance) >= 1f) return 0f;
            float amount = 1f - distance * distance;
            return amount * amount * amount;
        }

        public static float Center(float x)
        {
            return -9f * Bell(x, 110f, 76f)
                   + 22f * Bell(x, 188f, 78f)
                   - 23f * Bell(x, 264f, 67f)
                   + 11f * Bell(x, 334f, 70f);
        }

        public static float WidthScale(float x)
        {
            return 1f - .34f * Bell(x, 196f, 103f) + .95f * Bell(x, 319f, 81f);
        }

        public static float WorldZ(float x, float solverZ) { return Center(x) + solverZ * WidthScale(x); }
        public static float SolverZ(float x, float worldZ) { return (worldZ - Center(x)) / WidthScale(x); }
        public static Vector3 Point(float x, float y, float solverZ) { return new Vector3(x, y, WorldZ(x, solverZ)); }
        public static Vector3 Point(Vector3 p) { return Point(p.x, p.y, p.z); }

        public static float Island(float x, float solverZ)
        {
            float axis = -.3f + (x - 227f) * .13f;
            return Bell(x, 227f, 18f) * Bell(solverZ, axis, 11.2f);
        }

        public static float IslandExposure(float x, float solverZ)
        {
            return 1f - Mathf.SmoothStep(0f, 1f, (Island(x, solverZ) - .24f) / .20f);
        }

        public static float ShelfInfluence(float x, float solverZ)
        {
            float side = Mathf.SmoothStep(0, 1, (solverZ - .7f) / 3.4f);
            float reach = Mathf.SmoothStep(0,1,(x-208f)/15f) *
                (1f-Mathf.SmoothStep(0,1,(x-249f)/25f));
            return reach * side;
        }

        // Ease the rendered side tongue behind the same solver sequence, then
        // catch up. No delay is used by the simulation or any result fields.
        public static float ShelfDelay(float time)
        {
            const float onset = 10.10f;
            float held = Mathf.Clamp(time-onset,0f,2.30f);
            return held * (1f-Mathf.SmoothStep(0f,1f,(time-12.40f)/1.3f));
        }
    }
}
