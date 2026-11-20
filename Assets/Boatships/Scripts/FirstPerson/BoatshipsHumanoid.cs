using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>What a figure wears on its head.</summary>
    public enum HumanoidHat { Random, None, Bandana, Tricorn, StrawHat }

    /// <summary>Primitive low-poly people, sized in world units to match the toy-scale ships.
    /// Figures are jointed (shoulders, elbows, hips, knees) and animated by <see cref="BoatshipsHumanoidRig"/>.
    /// The figure faces +Z and stands with its feet at the parent's origin.</summary>
    public static class BoatshipsHumanoid
    {
        private static readonly Dictionary<Color32, Material> materials = new Dictionary<Color32, Material>();
        public static readonly Color Skin = new Color(0.9f, 0.74f, 0.58f);
        public const string ShirtPart = "Shirt";

        private static readonly Color[] SkinTones =
        {
            new Color(0.96f, 0.8f, 0.66f), new Color(0.9f, 0.74f, 0.58f), new Color(0.78f, 0.58f, 0.42f),
            new Color(0.62f, 0.43f, 0.3f), new Color(0.45f, 0.3f, 0.2f)
        };
        private static readonly Color[] HairColors =
        {
            new Color(0.12f, 0.09f, 0.07f), new Color(0.3f, 0.19f, 0.1f), new Color(0.55f, 0.36f, 0.18f),
            new Color(0.72f, 0.6f, 0.38f), new Color(0.6f, 0.6f, 0.6f), new Color(0.55f, 0.22f, 0.1f)
        };
        private static readonly Color[] TrouserColors =
        {
            new Color(0.25f, 0.22f, 0.2f), new Color(0.36f, 0.3f, 0.22f), new Color(0.2f, 0.24f, 0.33f),
            new Color(0.55f, 0.5f, 0.4f), new Color(0.3f, 0.28f, 0.26f)
        };
        private static readonly Color Leather = new Color(0.24f, 0.15f, 0.09f);
        private static readonly Color Eye = new Color(0.08f, 0.07f, 0.07f);
        private static readonly Color Steel = new Color(0.75f, 0.76f, 0.8f);
        private static readonly Color Brass = new Color(0.78f, 0.6f, 0.25f);

        public static Material MaterialFor(Color color)
        {
            Color32 key = color;
            if (materials.TryGetValue(key, out Material cached) && cached != null) return cached;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Material created = new Material(shader != null ? shader : Shader.Find("Standard")) { color = color, name = "Boatships Figure" };
            materials[key] = created;
            return created;
        }

        public static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
            return part;
        }

        private static Transform Joint(string name, Transform parent, Vector3 position)
        {
            Transform joint = new GameObject(name).transform;
            joint.SetParent(parent, false);
            joint.localPosition = position;
            return joint;
        }

        /// <summary>Builds a figure standing at the parent's origin and adds a walk-cycle rig to the parent.
        /// Returns the sword-arm shoulder (or null). The cutlass lies along the shoulder's +Z when the arm hangs.
        /// <paramref name="seed"/> picks skin, hair, trousers, beard and hat; -1 picks at random.</summary>
        public static Transform Build(Transform parent, Color shirt, float height, bool sword,
            int seed = -1, HumanoidHat hat = HumanoidHat.Random, Color? hatColor = null)
        {
            float h = height;
            System.Random random = new System.Random(seed >= 0 ? seed : Random.Range(0, int.MaxValue));
            Color skin = SkinTones[random.Next(SkinTones.Length)];
            Color hair = HairColors[random.Next(HairColors.Length)];
            Color trousers = TrouserColors[random.Next(TrouserColors.Length)];
            bool beard = random.NextDouble() < 0.35;
            if (hat == HumanoidHat.Random)
            {
                double roll = random.NextDouble();
                hat = roll < 0.45 ? HumanoidHat.None : roll < 0.65 ? HumanoidHat.Bandana : roll < 0.82 ? HumanoidHat.Tricorn : HumanoidHat.StrawHat;
            }

            Transform figure = Joint("Figure", parent, Vector3.zero);

            // Legs: hip → knee → ankle, with boots.
            Transform leftHip = Leg(figure, -1f, h, trousers);
            Transform rightHip = Leg(figure, 1f, h, trousers);

            // Pelvis, belt and torso.
            Part(PrimitiveType.Cube, "Pelvis", figure, new Vector3(0f, h * 0.52f, 0f), new Vector3(h * 0.25f, h * 0.08f, h * 0.15f), trousers);
            Part(PrimitiveType.Cube, "Belt", figure, new Vector3(0f, h * 0.565f, 0f), new Vector3(h * 0.27f, h * 0.035f, h * 0.165f), Leather);
            Part(PrimitiveType.Cube, "Buckle", figure, new Vector3(0f, h * 0.565f, h * 0.084f), new Vector3(h * 0.05f, h * 0.03f, h * 0.01f), Brass);
            Part(PrimitiveType.Cube, ShirtPart, figure, new Vector3(0f, h * 0.68f, 0f), new Vector3(h * 0.27f, h * 0.22f, h * 0.15f), shirt);
            Part(PrimitiveType.Cube, ShirtPart, figure, new Vector3(0f, h * 0.775f, 0f), new Vector3(h * 0.33f, h * 0.06f, h * 0.16f), shirt);

            // Neck and head with a face.
            Part(PrimitiveType.Cylinder, "Neck", figure, new Vector3(0f, h * 0.82f, 0f), new Vector3(h * 0.07f, h * 0.025f, h * 0.07f), skin);
            Transform head = Joint("Head", figure, new Vector3(0f, h * 0.9f, 0f));
            Part(PrimitiveType.Sphere, "Skull", head, Vector3.zero, new Vector3(h * 0.16f, h * 0.18f, h * 0.165f), skin);
            Part(PrimitiveType.Sphere, "Eye", head, new Vector3(-h * 0.033f, h * 0.012f, h * 0.074f), Vector3.one * h * 0.024f, Eye);
            Part(PrimitiveType.Sphere, "Eye", head, new Vector3(h * 0.033f, h * 0.012f, h * 0.074f), Vector3.one * h * 0.024f, Eye);
            Part(PrimitiveType.Cube, "Brow", head, new Vector3(0f, h * 0.035f, h * 0.074f), new Vector3(h * 0.1f, h * 0.012f, h * 0.012f), hair);
            Part(PrimitiveType.Cube, "Nose", head, new Vector3(0f, -h * 0.008f, h * 0.085f), new Vector3(h * 0.022f, h * 0.038f, h * 0.03f), skin * 0.92f);
            Part(PrimitiveType.Sphere, "Ear", head, new Vector3(-h * 0.08f, 0f, 0f), new Vector3(h * 0.02f, h * 0.04f, h * 0.03f), skin);
            Part(PrimitiveType.Sphere, "Ear", head, new Vector3(h * 0.08f, 0f, 0f), new Vector3(h * 0.02f, h * 0.04f, h * 0.03f), skin);
            if (beard)
                Part(PrimitiveType.Cube, "Beard", head, new Vector3(0f, -h * 0.055f, h * 0.05f), new Vector3(h * 0.12f, h * 0.06f, h * 0.07f), hair);
            else
                Part(PrimitiveType.Cube, "Mouth", head, new Vector3(0f, -h * 0.042f, h * 0.075f), new Vector3(h * 0.04f, h * 0.008f, h * 0.01f), skin * 0.6f);
            if (hat != HumanoidHat.Bandana)
                Part(PrimitiveType.Sphere, "Hair", head, new Vector3(0f, h * 0.045f, -h * 0.015f), new Vector3(h * 0.172f, h * 0.12f, h * 0.17f), hair);
            Hat(head, hat, hatColor, h, random);

            // Arms: shoulder → elbow → hand.
            Transform leftShoulder = Arm(figure, -1f, h, shirt, skin, out Transform leftElbow, out _);
            Transform rightShoulder = Arm(figure, 1f, h, shirt, skin, out Transform rightElbow, out Transform rightHand);
            if (sword)
            {
                Part(PrimitiveType.Cube, "Guard", rightHand, new Vector3(0f, 0f, h * 0.035f), new Vector3(h * 0.09f, h * 0.02f, h * 0.02f), Brass);
                Part(PrimitiveType.Cube, "Cutlass", rightHand, new Vector3(0f, 0f, h * 0.26f), new Vector3(h * 0.025f, h * 0.055f, h * 0.42f), Steel);
            }

            BoatshipsHumanoidRig rig = parent.GetComponent<BoatshipsHumanoidRig>();
            if (rig == null) rig = parent.gameObject.AddComponent<BoatshipsHumanoidRig>();
            rig.Bind(h, figure, leftHip, rightHip, leftShoulder, rightShoulder, leftElbow, rightElbow, head, sword);
            return sword ? rightShoulder : null;
        }

        /// <summary>Recolours every shirt part of a figure built by <see cref="Build"/>.</summary>
        public static void SetShirt(Transform root, Color shirt)
        {
            if (root == null) return;
            Material material = MaterialFor(shirt);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer.gameObject.name == ShirtPart) renderer.sharedMaterial = material;
        }

        private static Transform Leg(Transform figure, float side, float h, Color trousers)
        {
            Transform hip = Joint(side < 0f ? "Left Hip" : "Right Hip", figure, new Vector3(side * h * 0.065f, h * 0.5f, 0f));
            Part(PrimitiveType.Cube, "Thigh", hip, new Vector3(0f, -h * 0.115f, 0f), new Vector3(h * 0.105f, h * 0.24f, h * 0.115f), trousers);
            Transform knee = Joint("Knee", hip, new Vector3(0f, -h * 0.23f, 0f));
            Part(PrimitiveType.Cube, "Shin", knee, new Vector3(0f, -h * 0.1f, 0f), new Vector3(h * 0.09f, h * 0.2f, h * 0.1f), trousers);
            Part(PrimitiveType.Cube, "Boot", knee, new Vector3(0f, -h * 0.225f, h * 0.025f), new Vector3(h * 0.1f, h * 0.075f, h * 0.17f), Leather);
            return hip;
        }

        private static Transform Arm(Transform figure, float side, float h, Color shirt, Color skin, out Transform elbow, out Transform hand)
        {
            Transform shoulder = Joint(side < 0f ? "Left Shoulder" : "Right Shoulder", figure, new Vector3(side * h * 0.19f, h * 0.775f, 0f));
            Part(PrimitiveType.Sphere, ShirtPart, shoulder, Vector3.zero, Vector3.one * h * 0.09f, shirt);
            Part(PrimitiveType.Cube, ShirtPart, shoulder, new Vector3(0f, -h * 0.09f, 0f), new Vector3(h * 0.08f, h * 0.19f, h * 0.085f), shirt);
            elbow = Joint("Elbow", shoulder, new Vector3(0f, -h * 0.18f, 0f));
            Part(PrimitiveType.Cube, "Forearm", elbow, new Vector3(0f, -h * 0.08f, 0f), new Vector3(h * 0.07f, h * 0.17f, h * 0.075f), skin);
            hand = Joint("Hand", elbow, new Vector3(0f, -h * 0.185f, 0f));
            Part(PrimitiveType.Sphere, "Fist", hand, Vector3.zero, new Vector3(h * 0.07f, h * 0.075f, h * 0.07f), skin);
            return shoulder;
        }

        private static void Hat(Transform head, HumanoidHat hat, Color? color, float h, System.Random random)
        {
            switch (hat)
            {
                case HumanoidHat.Bandana:
                {
                    Color cloth = color ?? Color.HSVToRGB((float)random.NextDouble(), 0.6f, 0.65f);
                    Part(PrimitiveType.Sphere, "Bandana", head, new Vector3(0f, h * 0.05f, -h * 0.01f), new Vector3(h * 0.172f, h * 0.12f, h * 0.172f), cloth);
                    Part(PrimitiveType.Cube, "Bandana Knot", head, new Vector3(0f, h * 0.02f, -h * 0.09f), new Vector3(h * 0.04f, h * 0.06f, h * 0.03f), cloth);
                    break;
                }
                case HumanoidHat.Tricorn:
                {
                    Color felt = color ?? new Color(0.13f, 0.11f, 0.1f);
                    Part(PrimitiveType.Cylinder, "Hat Brim", head, new Vector3(0f, h * 0.08f, 0f), new Vector3(h * 0.26f, h * 0.012f, h * 0.24f), felt);
                    Part(PrimitiveType.Cylinder, "Hat Crown", head, new Vector3(0f, h * 0.115f, 0f), new Vector3(h * 0.15f, h * 0.035f, h * 0.15f), felt);
                    for (int i = 0; i < 3; i++)
                    {
                        Transform flap = Joint("Hat Flap", head, Vector3.zero);
                        flap.localRotation = Quaternion.Euler(0f, i * 120f, 0f);
                        GameObject piece = Part(PrimitiveType.Cube, "Flap", flap, new Vector3(0f, h * 0.105f, h * 0.1f), new Vector3(h * 0.16f, h * 0.05f, h * 0.012f), felt);
                        piece.transform.localRotation = Quaternion.Euler(-25f, 0f, 0f);
                    }
                    break;
                }
                case HumanoidHat.StrawHat:
                {
                    Color straw = color ?? new Color(0.85f, 0.74f, 0.45f);
                    Part(PrimitiveType.Cylinder, "Hat Brim", head, new Vector3(0f, h * 0.075f, 0f), new Vector3(h * 0.3f, h * 0.01f, h * 0.3f), straw);
                    Part(PrimitiveType.Cylinder, "Hat Crown", head, new Vector3(0f, h * 0.11f, 0f), new Vector3(h * 0.15f, h * 0.035f, h * 0.15f), straw);
                    Part(PrimitiveType.Cylinder, "Hat Band", head, new Vector3(0f, h * 0.09f, 0f), new Vector3(h * 0.155f, h * 0.008f, h * 0.155f), new Color(0.5f, 0.15f, 0.12f));
                    break;
                }
            }
        }
    }

    /// <summary>Procedural walk cycle for a <see cref="BoatshipsHumanoid"/> figure. Speed is measured in the
    /// parent's space, so people riding a moving ship only walk when they move across the deck.</summary>
    public sealed class BoatshipsHumanoidRig : MonoBehaviour
    {
        private float height;
        private Transform figure, leftHip, rightHip, leftShoulder, rightShoulder, leftElbow, rightElbow, head;
        private Transform leftKnee, rightKnee;
        private Vector3 lastLocal;
        private bool hasLast;
        private float phase;
        private float stride;
        private float idleOffset;

        /// <summary>The sword arm is posed by its owner (see <see cref="BoatshipsDeckFighter"/>).</summary>
        public bool RightArmPosed { get; set; }
        /// <summary>Raises the right arm and waves it, e.g. survivors calling for rescue.</summary>
        public bool Waving { get; set; }

        public void Bind(float figureHeight, Transform root, Transform lHip, Transform rHip, Transform lShoulder, Transform rShoulder,
            Transform lElbow, Transform rElbow, Transform headJoint, bool swordArm)
        {
            height = Mathf.Max(0.001f, figureHeight);
            figure = root; leftHip = lHip; rightHip = rHip; leftShoulder = lShoulder; rightShoulder = rShoulder;
            leftElbow = lElbow; rightElbow = rElbow; head = headJoint;
            leftKnee = lHip.Find("Knee"); rightKnee = rHip.Find("Knee");
            RightArmPosed = swordArm;
            idleOffset = Random.value * 10f;
            hasLast = false;
        }

        private void LateUpdate()
        {
            if (figure == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 local = transform.localPosition;
            float moved = 0f;
            if (hasLast)
            {
                Vector3 delta = local - lastLocal;
                delta.y = 0f;
                float parentScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
                float figureScale = Mathf.Max(0.0001f, transform.lossyScale.x);
                // Distance travelled, measured in figure heights. Teleports and jumps count as standing still.
                moved = delta.magnitude * parentScale / (height * figureScale);
                if (moved > 6f * dt) moved = 0f;
            }
            lastLocal = local;
            hasLast = true;

            float speed = moved / dt;
            float target = Mathf.Clamp01(speed / 1.2f);
            stride = Mathf.Lerp(stride, target, 1f - Mathf.Exp(-10f * dt));
            phase = Mathf.Repeat(phase + moved / 1.1f * Mathf.PI * 2f, Mathf.PI * 2f);

            float swing = Mathf.Sin(phase);
            float legSwing = 32f * stride, armSwing = 26f * stride;
            leftHip.localRotation = Quaternion.Euler(-swing * legSwing, 0f, 0f);
            rightHip.localRotation = Quaternion.Euler(swing * legSwing, 0f, 0f);
            if (leftKnee != null) leftKnee.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Cos(phase)) * 50f * stride, 0f, 0f);
            if (rightKnee != null) rightKnee.localRotation = Quaternion.Euler(Mathf.Max(0f, -Mathf.Cos(phase)) * 50f * stride, 0f, 0f);

            float idle = Mathf.Sin((Time.time + idleOffset) * 1.6f);
            leftShoulder.localRotation = Quaternion.Euler(swing * armSwing + idle * 2f, 0f, -6f);
            leftElbow.localRotation = Quaternion.Euler(-12f - 14f * stride, 0f, 0f);
            if (Waving)
            {
                rightShoulder.localRotation = Quaternion.Euler(0f, 0f, 150f + Mathf.Sin((Time.time + idleOffset) * 7f) * 18f);
                rightElbow.localRotation = Quaternion.Euler(0f, 0f, 20f);
            }
            else if (!RightArmPosed)
            {
                rightShoulder.localRotation = Quaternion.Euler(-swing * armSwing - idle * 2f, 0f, 6f);
                rightElbow.localRotation = Quaternion.Euler(-12f - 14f * stride, 0f, 0f);
            }

            figure.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Cos(phase)) * height * 0.025f * stride + idle * height * 0.003f, 0f);
            figure.localRotation = Quaternion.Euler(4f * stride, swing * 4f * stride, 0f);
            if (head != null) head.localRotation = Quaternion.Euler(-3f * stride, -swing * 3f * stride, 0f);
        }
    }
}
