using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>A soldier fighting on an island's streets during a town assault. Team 0 storms the town
    /// with the player; team 1 defends it. Defenders hold their post until attackers come close.
    /// Walks on the island's height field (no navigation mesh: it steps around nothing).</summary>
    public sealed class BoatshipsGroundFighter : MonoBehaviour
    {
        public static readonly List<BoatshipsGroundFighter> All = new List<BoatshipsGroundFighter>();
        public static BoatshipsFirstPerson Player;
        private const float Speed = 1.3f;
        private const float AttackReach = 0.62f;

        private ArchipelagoWorld.IslandSite site;
        private int seed;
        private Vector2 position;
        private Vector2 post;
        private float leash;
        private float yaw;
        private float damage;
        private Transform sword;
        private float swing;
        private float nextAttack;
        private float diedAt;

        public int Team { get; private set; }
        public float Health { get; private set; }
        public bool Alive => Health > 0f;
        public Vector3 World => transform.position;
        public event Action<BoatshipsGroundFighter> Died;

        public static BoatshipsGroundFighter Spawn(string name, int team, ArchipelagoWorld.IslandSite island, int worldSeed,
            Vector2 at, float health, float damage, Color shirt, HumanoidHat hat, Color hatColor, Transform parent)
        {
            GameObject root = new GameObject(name);
            if (parent != null) root.transform.SetParent(parent, true);
            BoatshipsGroundFighter fighter = root.AddComponent<BoatshipsGroundFighter>();
            fighter.site = island; fighter.seed = worldSeed;
            fighter.Team = team; fighter.Health = health; fighter.damage = damage;
            fighter.position = at; fighter.post = at; fighter.leash = team == 1 ? 5f : 999f;
            fighter.nextAttack = Time.time + UnityEngine.Random.Range(0.4f, 1.2f);
            fighter.sword = BoatshipsHumanoid.Build(root.transform, shirt, BoatshipsFirstPerson.HumanHeight, true,
                UnityEngine.Random.Range(0, int.MaxValue), hat, hatColor);
            fighter.Place();
            All.Add(fighter);
            return fighter;
        }

        /// <summary>Defenders guard <paramref name="point"/> and only chase attackers within <paramref name="radius"/> of it.</summary>
        public void Guard(Vector2 point, float radius) { post = point; leash = radius; }

        public void TakeHit(float amount)
        {
            if (!Alive || amount <= 0f) return;
            Health = Mathf.Max(0f, Health - amount);
            if (Alive) return;
            diedAt = Time.time;
            Died?.Invoke(this);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (!Alive)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, yaw, 85f), 1f - Mathf.Exp(-6f * dt));
                if (Time.time - diedAt > 3f) Destroy(gameObject);
                return;
            }
            if (FindTarget(out Vector2 target, out BoatshipsGroundFighter enemy, out bool isPlayer))
            {
                Vector2 to = target - position;
                float distance = to.magnitude;
                if (distance > 0.001f) yaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                if (distance > AttackReach) Step(to / Mathf.Max(0.001f, distance), dt);
                else if (Time.time >= nextAttack)
                {
                    nextAttack = Time.time + UnityEngine.Random.Range(1.0f, 1.7f);
                    swing = 1f;
                    if (isPlayer) Player.TakeDamage(damage, transform.position);
                    else enemy.TakeHit(damage);
                }
            }
            else if ((post - position).sqrMagnitude > 0.04f)
            {
                Vector2 home = post - position;
                yaw = Mathf.Atan2(home.x, home.y) * Mathf.Rad2Deg;
                Step(home.normalized, dt);
            }
            Separate(dt);
            Place();
            swing = Mathf.Max(0f, swing - dt * 3.5f);
            if (sword != null) sword.localRotation = Quaternion.Euler(Mathf.Lerp(-55f, -150f, Mathf.Sin(swing * Mathf.PI)), 0f, 6f);
        }

        private void Step(Vector2 direction, float dt)
        {
            Vector2 next = position + direction * Speed * dt;
            // Stay on dry land.
            if (BoatshipsTown.Ground(site, seed, next) > 0.45f) position = next;
        }

        private bool FindTarget(out Vector2 target, out BoatshipsGroundFighter enemy, out bool isPlayer)
        {
            target = position; enemy = null; isPlayer = false;
            float best = float.MaxValue;
            float leashSq = leash * leash;
            foreach (BoatshipsGroundFighter other in All)
            {
                if (other == this || other.Team == Team || !other.Alive) continue;
                if ((other.position - post).sqrMagnitude > leashSq && (other.position - position).sqrMagnitude > 2.25f) continue;
                float d = (other.position - position).sqrMagnitude;
                if (d >= best) continue;
                best = d; enemy = other; target = other.position;
            }
            if (Team == 1 && Player != null && Player.Active && Player.Alive && Player.CurrentDeck == null)
            {
                Vector3 feet = Player.FeetWorld;
                Vector2 p = new Vector2(feet.x, feet.z);
                if ((p - post).sqrMagnitude <= leashSq || (p - position).sqrMagnitude <= 2.25f)
                {
                    float d = (p - position).sqrMagnitude * 0.7f;
                    if (d < best) { best = d; enemy = null; isPlayer = true; target = p; }
                }
            }
            return best < float.MaxValue;
        }

        private void Separate(float dt)
        {
            foreach (BoatshipsGroundFighter other in All)
            {
                if (other == this || !other.Alive) continue;
                Vector2 away = position - other.position;
                float d = away.magnitude;
                if (d >= 0.3f || d < 0.0001f) continue;
                position += away / d * (0.3f - d) * Mathf.Min(1f, dt * 8f);
            }
        }

        private void Place()
        {
            transform.SetPositionAndRotation(new Vector3(position.x, BoatshipsTown.Ground(site, seed, position), position.y),
                Quaternion.Euler(0f, yaw, 0f));
        }

        private void OnDestroy() => All.Remove(this);
    }
}
