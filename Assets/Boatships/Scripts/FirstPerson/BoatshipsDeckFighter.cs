using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>Walkable deck surface of a voxel hull, expressed in the hull's local space so that
    /// people on board ride along with the moving, rolling ship. Holes in the deck are respected.</summary>
    public sealed class DeckSpace
    {
        public readonly VoxelHull Hull;
        public readonly float Top;
        public readonly float Scale;
        private readonly HullDefinition definition;

        private DeckSpace(VoxelHull hull)
        {
            Hull = hull;
            definition = hull.Definition;
            Top = definition.BlockCenter(0, definition.Height - 1, 0).y + definition.BlockSize * 0.5f;
            Scale = Mathf.Max(0.01f, hull.transform.lossyScale.x);
        }

        public static DeckSpace Create(VoxelHull hull) =>
            hull != null && hull.Definition != null ? new DeckSpace(hull) : null;

        public bool Valid => Hull != null;
        public Transform Frame => Hull.transform;
        public float HalfWidth => definition.Width * definition.BlockSize * 0.5f;
        public float HalfLength => definition.Length * definition.BlockSize * 0.5f;

        public bool IsDeck(Vector3 local)
        {
            int x = Mathf.RoundToInt(local.x / definition.BlockSize + (definition.Width - 1) * 0.5f);
            int z = Mathf.RoundToInt(local.z / definition.BlockSize + (definition.Length - 1) * 0.5f);
            return Hull.HasBlock(x, definition.Height - 1, z);
        }

        /// <summary>Moves from one deck point toward another, sliding along rails and stopping at holes.</summary>
        public Vector3 Constrain(Vector3 from, Vector3 to)
        {
            from.y = Top; to.y = Top;
            if (IsDeck(to)) return to;
            Vector3 alongX = new Vector3(to.x, Top, from.z);
            if (IsDeck(alongX)) return alongX;
            Vector3 alongZ = new Vector3(from.x, Top, to.z);
            if (IsDeck(alongZ)) return alongZ;
            return IsDeck(from) ? from : Nearest(from);
        }

        public Vector3 Nearest(Vector3 local)
        {
            if (!Hull.TryNearestDeckPoint(local, out Vector3 point)) point = Vector3.zero;
            point.y = Top;
            return point;
        }

        public Vector3 Center => Nearest(Vector3.zero);

        public Vector3 RandomPoint(System.Random random)
        {
            for (int i = 0; i < 30; i++)
            {
                Vector3 candidate = new Vector3(((float)random.NextDouble() * 2f - 1f) * HalfWidth * 0.8f, Top,
                    ((float)random.NextDouble() * 2f - 1f) * HalfLength * 0.8f);
                if (IsDeck(candidate)) return candidate;
            }
            return Center;
        }

        /// <summary>Rail position on one side (+1 starboard, -1 port), along = -1 stern … +1 bow.</summary>
        public Vector3 Rail(int side, float along) =>
            Nearest(new Vector3(side * HalfWidth, Top, along * HalfLength * 0.8f));

        public Vector3 ToWorld(Vector3 local) => Frame.TransformPoint(local);
        public Vector3 ToLocal(Vector3 world) => Frame.InverseTransformPoint(world);
        public float FrameYaw => Frame.eulerAngles.y;
    }

    /// <summary>Simple melee NPC on a ship deck. Team 0 fights alongside the player, team 1 against.</summary>
    public sealed class BoatshipsDeckFighter : MonoBehaviour
    {
        public static readonly List<BoatshipsDeckFighter> All = new List<BoatshipsDeckFighter>();
        public static BoatshipsFirstPerson Player;
        private const float Speed = 1.25f;

        private DeckSpace deck;
        private Vector3 local;
        private float yaw;
        private float damage;
        private Transform sword;
        private float swing;
        private float nextAttack;
        private bool jumping;
        private Vector3 jumpFrom;
        private float jumpProgress;
        private float diedAt;

        public int Team { get; private set; }
        /// <summary>The ship's crew this sailor works for in a ship battle (null: a pure melee fighter).</summary>
        public BoatshipsShipCrew Crew { get; set; }
        public CrewDuty Duty { get; set; }
        public bool Jumping => jumping;
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public bool Alive => Health > 0f;
        public DeckSpace Deck => deck;
        public Vector3 Local => local;
        public event Action<BoatshipsDeckFighter> Died;
        /// <summary>What delivered the blow that put this fighter down.</summary>
        public Incapacitation FelledBy { get; private set; } = Incapacitation.Blade;

        public static BoatshipsDeckFighter Spawn(string name, int team, DeckSpace deck, Vector3 local, float health, float damage)
        {
            if (deck == null || !deck.Valid) return null;
            GameObject root = new GameObject(name);
            root.transform.SetParent(deck.Frame, false);
            root.transform.localScale = Vector3.one / deck.Scale;
            BoatshipsDeckFighter fighter = root.AddComponent<BoatshipsDeckFighter>();
            fighter.deck = deck;
            fighter.Team = team;
            fighter.MaxHealth = fighter.Health = health;
            fighter.damage = damage;
            fighter.local = deck.Constrain(local, local);
            fighter.yaw = team == 0 ? 0f : 180f;
            fighter.nextAttack = Time.time + UnityEngine.Random.Range(0.5f, 1.5f);
            // Allies wear blue with tricorns; boarders wear red with bandanas, so sides read at a glance.
            Color shirt = team == 0 ? new Color(0.2f, 0.35f, 0.75f) : new Color(0.75f, 0.15f, 0.12f);
            HumanoidHat hat = team == 0 ? HumanoidHat.Tricorn : HumanoidHat.Bandana;
            Color hatColor = team == 0 ? new Color(0.12f, 0.12f, 0.16f) : new Color(0.45f, 0.08f, 0.08f);
            fighter.sword = BoatshipsHumanoid.Build(root.transform, shirt, BoatshipsFirstPerson.HumanHeight, true,
                UnityEngine.Random.Range(0, int.MaxValue), hat, hatColor);
            root.transform.localPosition = fighter.local;
            All.Add(fighter);
            return fighter;
        }

        /// <summary>Turns to watch a point in the world (only matters while there is nobody on deck to fight).</summary>
        public void FaceWorld(Vector3 world)
        {
            if (deck == null || !deck.Valid || !Alive) return;
            Vector3 offset = deck.ToLocal(world) - local;
            if (offset.x * offset.x + offset.z * offset.z > 0.0001f) yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        }

        /// <summary>Swings across from another ship before joining the fight.</summary>
        public void JumpFrom(Vector3 world)
        {
            jumping = true;
            jumpFrom = world;
            jumpProgress = 0f;
            transform.position = world;
        }

        public void TakeHit(float amount, Incapacitation cause = Incapacitation.Blade)
        {
            if (!Alive || amount <= 0f) return;
            Health = Mathf.Max(0f, Health - amount);
            if (Alive) return;
            FelledBy = cause;
            diedAt = Time.time;
            Died?.Invoke(this);
        }

        private void Update()
        {
            if (deck == null || !deck.Valid) { Destroy(gameObject); return; }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (!Alive)
            {
                transform.localRotation = Quaternion.Slerp(transform.localRotation,
                    Quaternion.Euler(0f, yaw, 88f), 1f - Mathf.Exp(-6f * dt));
                if (Time.time - diedAt > 2.5f) Destroy(gameObject);
                return;
            }
            if (jumping)
            {
                jumpProgress += dt / 0.9f;
                Vector3 end = deck.ToWorld(local);
                transform.position = Vector3.Lerp(jumpFrom, end, jumpProgress) + Vector3.up * Mathf.Sin(Mathf.Clamp01(jumpProgress) * Mathf.PI) * 1.2f;
                if (jumpProgress >= 1f) { jumping = false; transform.localPosition = local; }
                return;
            }
            bool hasTarget = FindTarget(out Vector3 targetLocal, out BoatshipsDeckFighter enemy, out bool targetIsPlayer);
            Vector3 toTarget = targetLocal - local;
            toTarget.y = 0f;
            float worldDistance = toTarget.magnitude * deck.Scale;
            // Sailors in a ship battle keep working (guns, holes, water) unless a foe is upon them.
            if (hasTarget && Crew != null && !Crew.ShouldFight(this, worldDistance)) hasTarget = false;
            if (!hasTarget && Crew != null) Crew.Work(this, dt);
            else if (hasTarget)
            {
                if (toTarget.sqrMagnitude > 0.0001f) yaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                if (worldDistance > BoatshipsFirstPerson.Reach * 0.85f)
                    local = deck.Constrain(local, local + toTarget.normalized * (Speed / deck.Scale) * dt);
                else if (Time.time >= nextAttack)
                {
                    nextAttack = Time.time + UnityEngine.Random.Range(1.0f, 1.6f);
                    swing = 1f;
                    BoatshipsAudio.Play(Sfx.Swing, transform.position, 0.7f);
                    if (targetIsPlayer) Player.TakeDamage(damage, transform.position);
                    else
                    {
                        enemy.TakeHit(damage);
                        // Some strokes are parried, steel on steel; the rest land.
                        BoatshipsAudio.Play(UnityEngine.Random.value < 0.35f ? Sfx.Clang : Sfx.SwordHit, enemy.transform.position, 0.8f);
                    }
                }
            }
            Separate(dt);
            transform.localPosition = local;
            transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            swing = Mathf.Max(0f, swing - dt * 3.5f);
            if (sword != null) sword.localRotation = Quaternion.Euler(SwordArmPitch(1f - swing), 0f, 6f);
        }

        /// <summary>Walks toward a deck point. Returns true once there.</summary>
        public bool WalkTo(Vector3 goal, float dt)
        {
            Vector3 to = goal - local;
            to.y = 0f;
            float remaining = to.magnitude;
            if (remaining * deck.Scale < 0.08f) return true;
            yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            Vector3 next = deck.Constrain(local, local + to / remaining * Mathf.Min(remaining, Speed / deck.Scale * dt));
            // Blocked by a hole in the deck: close enough to work from here.
            if ((next - local).sqrMagnitude < 1e-8f) return remaining * deck.Scale < 0.45f;
            local = next;
            return false;
        }

        /// <summary>Turns to face a deck point.</summary>
        public void Face(Vector3 point)
        {
            Vector3 to = point - local;
            if (to.x * to.x + to.z * to.z > 1e-6f) yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        }

        /// <summary>Hammering, hauling, bailing: the arm swings while working.</summary>
        public void Toil() { if (swing <= 0f) swing = 0.8f; }

        /// <summary>Climbs (swings) across onto another ship's deck.</summary>
        public void CrossTo(DeckSpace other, Vector3 landing)
        {
            if (other == null || !other.Valid || !Alive) return;
            Vector3 from = transform.position;
            deck = other;
            transform.SetParent(other.Frame, true);
            transform.localScale = Vector3.one / other.Scale;
            local = other.Constrain(landing, landing);
            JumpFrom(from);
        }

        /// <summary>Guard with the cutlass held forward, wind up overhead, then chop down (t: 0 → 1 over a swing).</summary>
        private static float SwordArmPitch(float t)
        {
            const float guard = -55f, raised = -165f, chop = -10f;
            if (t >= 1f) return guard;
            if (t < 0.35f) return Mathf.Lerp(guard, raised, Mathf.SmoothStep(0f, 1f, t / 0.35f));
            if (t < 0.7f) return Mathf.Lerp(raised, chop, Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 0.35f));
            return Mathf.Lerp(chop, guard, Mathf.SmoothStep(0f, 1f, (t - 0.7f) / 0.3f));
        }

        private bool FindTarget(out Vector3 targetLocal, out BoatshipsDeckFighter enemy, out bool targetIsPlayer)
        {
            targetLocal = local; enemy = null; targetIsPlayer = false;
            float best = float.MaxValue;
            foreach (BoatshipsDeckFighter other in All)
            {
                if (other == this || other.Team == Team || !other.Alive || other.jumping || other.deck == null ||
                    other.deck.Hull != deck.Hull) continue;
                float distance = (other.local - local).sqrMagnitude;
                if (distance >= best) continue;
                best = distance; enemy = other; targetLocal = other.local;
            }
            if (Team == 1 && Player != null && Player.Alive && Player.OnDeck(deck.Hull))
            {
                // Boarders favour the player slightly, so standing back is never entirely safe.
                float distance = (Player.DeckLocal - local).sqrMagnitude * 0.7f;
                if (distance < best) { enemy = null; targetIsPlayer = true; targetLocal = Player.DeckLocal; best = distance; }
            }
            return best < float.MaxValue;
        }

        private void Separate(float dt)
        {
            float spacing = 0.28f / deck.Scale;
            foreach (BoatshipsDeckFighter other in All)
            {
                if (other == this || !other.Alive || other.deck == null || other.deck.Hull != deck.Hull) continue;
                Vector3 away = local - other.local;
                away.y = 0f;
                float distance = away.magnitude;
                if (distance >= spacing || distance < 0.0001f) continue;
                local = deck.Constrain(local, local + away / distance * (spacing - distance) * Mathf.Min(1f, dt * 8f));
            }
        }

        private void OnDestroy() => All.Remove(this);
    }
}
