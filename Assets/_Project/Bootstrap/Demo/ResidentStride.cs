using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    // The mesh moves on a visual body joint beneath the simulation root that owns the collision capsule.
    internal sealed class ResidentStride
    {
        internal readonly Transform[] Bones;
        internal readonly float LegLength;
        internal float3 PreviousDirection;
        private readonly float3 _leftShin, _rightShin, _leftThigh, _rightThigh;
        private readonly float _swing, _strideLength;
        private float _phase;
        private float _amplitude;
        private bool _resting = true;

        internal ResidentStride(Transform root, Mesh mesh, float3 hipL, float3 hipR, float3 kneeL, float3 kneeR,
            float3 ankleL, float3 ankleR, in NpcSpec spec)
        {
            LegLength = hipL.y;
            _swing = math.lerp(12f, 26f, spec.Health01) * math.lerp(1f, 0.84f, math.smoothstep(0.62f, 0.92f, spec.Age01));
            _strideLength = 4f * LegLength * math.sin(math.radians(_swing));
            _phase = (spec.Seed & 0xffff) * (2f * math.PI / 65536f);
            _leftShin = ankleL - kneeL;
            _rightShin = ankleR - kneeR;
            _leftThigh = kneeL - hipL;
            _rightThigh = kneeR - hipR;
            Bones = new Transform[7];
            Bones[0] = Joint(root, "Body", float3.zero);
            Bones[1] = Joint(Bones[0], "HipL", hipL);
            Bones[2] = Joint(Bones[1], "KneeL", kneeL - hipL);
            Bones[3] = Joint(Bones[0], "HipR", hipR);
            Bones[4] = Joint(Bones[3], "KneeR", kneeR - hipR);

            Bones[5] = Joint(Bones[2], "AnkleL", _leftShin);
            Bones[6] = Joint(Bones[4], "AnkleR", _rightShin);

            Vector2[] tags = mesh.uv2;
            Vector3[] points = mesh.vertices;
            var weights = new BoneWeight[points.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                float side = tags[i].x;
                if (math.abs(side) == 2f)
                {
                    weights[i] = new BoneWeight { boneIndex0 = side < 0f ? 5 : 6, weight0 = 1f };
                    continue;
                }

                float knee = side < 0f ? kneeL.y : kneeR.y;
                int thigh = side < 0f ? 1 : 3;
                float limb = math.abs(side) * tags[i].y * (1f - math.smoothstep(0.84f * LegLength, LegLength, points[i].y));
                float shin = limb * (1f - math.smoothstep(knee - 0.09f * LegLength, knee + 0.09f * LegLength, points[i].y));
                var w = new BoneWeight { boneIndex0 = 0, weight0 = 1f - limb, boneIndex1 = thigh, weight1 = limb - shin,
                    boneIndex2 = thigh + 1, weight2 = shin };
                // Unity expects decreasing weights, including for renderers limited to fewer influences.
                if (w.weight2 > w.weight1) SwapLastTwo(ref w);
                if (w.weight1 > w.weight0)
                {
                    (w.weight0, w.weight1) = (w.weight1, w.weight0);
                    (w.boneIndex0, w.boneIndex1) = (w.boneIndex1, w.boneIndex0);
                }
                if (w.weight2 > w.weight1) SwapLastTwo(ref w);
                weights[i] = w;
            }

            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(-hipL), Matrix4x4.Translate(-kneeL),
                Matrix4x4.Translate(-hipR), Matrix4x4.Translate(-kneeR), Matrix4x4.Translate(-ankleL), Matrix4x4.Translate(-ankleR) };
            mesh.boneWeights = weights;
        }

        private static void SwapLastTwo(ref BoneWeight weights)
        {
            (weights.weight1, weights.weight2) = (weights.weight2, weights.weight1);
            (weights.boneIndex1, weights.boneIndex2) = (weights.boneIndex2, weights.boneIndex1);
        }

        private static Transform Joint(Transform parent, string name, float3 offset)
        {
            Transform joint = new GameObject(name).transform;
            joint.SetParent(parent, false);
            joint.localPosition = offset;
            return joint;
        }

        internal void Advance(float distance, bool visible)
        {
            _phase = (_phase + distance * (2f * math.PI / _strideLength)) % (2f * math.PI);
            _amplitude = distance > 0f ? math.min(1f, _amplitude + distance * 8f / _strideLength) : math.max(0f, _amplitude - 0.25f);
            if (!visible || (_amplitude == 0f && _resting)) return;
            Pose(_phase, _amplitude);
            _resting = _amplitude == 0f;
        }

        internal void Pose(float phase, float amplitude)
        {
            math.sincos(phase, out float reach, out float passing);
            float lowestRise = float.MaxValue;
            for (int leg = 0; leg < 2; leg++)
            {
                float side = leg == 0 ? 1f : -1f;
                float bend = math.max(0f, side * passing) * _swing * amplitude;
                int thigh = 1 + 2 * leg;
                Quaternion hip = Quaternion.Euler(-side * reach * _swing * amplitude - bend * 0.5f, 0f, 0f);
                Bones[thigh].localRotation = hip;
                Bones[thigh + 1].localRotation = Quaternion.Euler(bend, 0f, 0f);
                Quaternion shin = hip * Bones[thigh + 1].localRotation;
                Bones[5 + leg].localRotation = Quaternion.Inverse(shin);
                Vector3 upper = leg == 0 ? _leftThigh : _rightThigh;
                Vector3 lower = leg == 0 ? _leftShin : _rightShin;
                float rise = (hip * upper + shin * lower).y - upper.y - lower.y;
                lowestRise = math.min(lowestRise, rise);
            }

            // Only the visible skeleton dips; the lower sole stays planted and the capsule follows the simulation.
            Bones[0].localPosition = new Vector3(0f, -lowestRise, 0f);
        }
    }
}
