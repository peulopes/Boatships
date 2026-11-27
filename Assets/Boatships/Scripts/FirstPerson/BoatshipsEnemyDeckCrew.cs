using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>
    /// Puts every enemy ship's real crew on her deck for the whole battle, so the captain can see them and fight them
    /// directly: pick them off with the flintlock from your own deck, or cross over and use the cutlass.
    /// The figures always match the encounter's crew count. Cannon casualties drop where they stand, the dead are
    /// replaced from below while any crew remain, and every man the player or the boarding party cuts down is a real
    /// casualty (how he fell decides whether he can be taken prisoner). Kill the last of them and she is taken intact.
    /// While the captain is on deck within musket range, her crew fire back.
    /// </summary>
    public sealed class BoatshipsEnemyDeckCrew : MonoBehaviour
    {
        private const int MaxOnDeck = 6;
        private const float ReinforceSeconds = 2.5f;
        private const float MusketRange = 16f;

        private sealed class Crew
        {
            public ShipCombatTarget ship;
            public DeckSpace deck;
            public readonly List<BoatshipsDeckFighter> men = new List<BoatshipsDeckFighter>();
            public bool mustered;
            public float nextReinforcement;
            public float nextVolley;
        }

        private BoatshipsEncounterDirector director;
        private BoatshipsTravelController travel;
        private BoatshipsFirstPerson player;
        private BoatshipsBoarding boarding;
        private ShipCombatTarget flagship;
        private readonly Dictionary<string, Crew> crews = new Dictionary<string, Crew>();
        private readonly Dictionary<BoatshipsDeckFighter, string> owners = new Dictionary<BoatshipsDeckFighter, string>();
        private readonly List<EncounterRecord> records = new List<EncounterRecord>();
        private readonly List<ShipCombatTarget> ships = new List<ShipCombatTarget>();
        private readonly List<string> stale = new List<string>();
        private GameObject flash;
        private Material flashMaterial;
        private float flashUntil;

        public void Configure(BoatshipsEncounterDirector encounters, BoatshipsTravelController controller,
            BoatshipsFirstPerson firstPerson, BoatshipsBoarding boardingAction, ShipCombatTarget ownShip)
        {
            director = encounters; travel = controller; player = firstPerson; boarding = boardingAction; flagship = ownShip;
        }

        /// <summary>Enemy sailors standing on this ship's deck right now.</summary>
        public int OnDeck(ShipCombatTarget ship)
        {
            int count = 0;
            foreach (Crew crew in crews.Values)
                if (crew.ship == ship) foreach (BoatshipsDeckFighter man in crew.men) if (man != null && man.Alive) count++;
            return count;
        }

        private void Update()
        {
            if (flash != null && Time.time >= flashUntil && flash.activeSelf) flash.SetActive(false);
            // Only in the captain's own battles; a sailor aboard an AI ship has the crew battle's boarders instead.
            bool active = director != null && travel != null && travel.Live && director.IsReady &&
                (travel.Mode == TravelMode.Battle || travel.Mode == TravelMode.Island);
            if (!active) { Clear(); return; }
            director.GetHostiles(records, ships);
            stale.Clear();
            foreach (string id in crews.Keys) if (records.Find(item => item.id == id) == null) stale.Add(id);
            foreach (string id in stale) Disband(id);
            for (int i = 0; i < records.Count; i++) Muster(records[i], ships[i]);
        }

        private void Muster(EncounterRecord record, ShipCombatTarget ship)
        {
            if (!crews.TryGetValue(record.id, out Crew crew))
            {
                crew = new Crew { ship = ship, nextVolley = Time.time + Random.Range(2f, 4f) };
                crews.Add(record.id, crew);
            }
            if (crew.deck == null || !crew.deck.Valid) crew.deck = DeckSpace.Create(ship.Hull);
            if (crew.deck == null) return;
            crew.men.RemoveAll(man => man == null || !man.Alive);

            int alive = Mathf.Max(0, record.crew);
            // Sailors killed elsewhere (cannon fire, the Fighters' melee) fall where they stand.
            while (crew.men.Count > alive)
            {
                BoatshipsDeckFighter man = crew.men[crew.men.Count - 1];
                crew.men.RemoveAt(crew.men.Count - 1);
                owners.Remove(man);
                man.TakeHit(9999f, Incapacitation.Cannon);
            }
            int wanted = Mathf.Min(MaxOnDeck, alive);
            if (crew.men.Count < wanted)
            {
                if (!crew.mustered)
                {
                    var random = new System.Random(record.id.GetHashCode());
                    while (crew.men.Count < wanted) Spawn(record, crew, crew.deck.RandomPoint(random));
                }
                else if (Time.time >= crew.nextReinforcement)
                {
                    // More of her crew come up through the hatch.
                    crew.nextReinforcement = Time.time + ReinforceSeconds;
                    Spawn(record, crew, crew.deck.Center);
                }
            }
            crew.mustered = true;

            // The last man is down: she is taken intact (a boarding in progress claims her itself).
            if (alive == 0 && record.crewMax > 0 && (boarding == null || !boarding.InProgress || boarding.Target != ship))
            {
                if (director.Capture(record, ship)) travel.Notify("The last of her crew falls — " + record.DisplayName + " is yours!");
                return;
            }

            Vector3 watch = flagship != null ? flagship.transform.position : ship.transform.position;
            foreach (BoatshipsDeckFighter man in crew.men) man.FaceWorld(watch);
            FireMuskets(record, crew);
        }

        private void Spawn(EncounterRecord record, Crew crew, Vector3 local)
        {
            BoatshipsDeckFighter man = BoatshipsDeckFighter.Spawn("Enemy Sailor", 1, crew.deck, local,
                26f + record.tier * 8f, 5f + record.tier * 2f);
            if (man == null) return;
            man.Died += OnManDied;
            owners[man] = record.id;
            crew.men.Add(man);
        }

        /// <summary>Cut down by the player's cutlass or pistol, or a boarding-party mate's blade.</summary>
        private void OnManDied(BoatshipsDeckFighter man)
        {
            man.Died -= OnManDied;
            if (!owners.TryGetValue(man, out string id)) return;
            owners.Remove(man);
            EncounterRecord record = null;
            foreach (EncounterRecord candidate in records) if (candidate.id == id) record = candidate;
            director.KillCrew(record, 1, man.FelledBy);
        }

        /// <summary>Her crew fire muskets at the captain standing on an exposed deck within range.</summary>
        private void FireMuskets(EncounterRecord record, Crew crew)
        {
            if (Time.time < crew.nextVolley || crew.men.Count == 0) return;
            crew.nextVolley = Time.time + Random.Range(2.2f, 3.6f);
            if (player == null || !player.Active || !player.Alive || flagship == null || !player.OnDeck(flagship.Hull)) return;
            BoatshipsDeckFighter shooter = crew.men[Random.Range(0, crew.men.Count)];
            if (shooter == null || !shooter.Alive) return;
            Vector3 muzzle = shooter.transform.position + Vector3.up * (BoatshipsFirstPerson.HumanHeight * 0.75f);
            float distance = Vector3.Distance(muzzle, player.EyePosition);
            if (distance > MusketRange) return;
            ShowFlash(muzzle);
            float chance = Mathf.Clamp(0.45f - distance * 0.02f, 0.1f, 0.4f);
            if (Random.value < chance) player.TakeDamage(5f + record.tier * 2f, muzzle);
        }

        private void ShowFlash(Vector3 position)
        {
            if (flash == null)
            {
                flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                flash.name = "Enemy Musket Flash";
                Destroy(flash.GetComponent<Collider>());
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                flashMaterial = new Material(shader) { color = new Color(1f, 0.85f, 0.35f) };
                flash.GetComponent<Renderer>().sharedMaterial = flashMaterial;
                flash.transform.localScale = Vector3.one * 0.18f;
            }
            flash.transform.position = position;
            flash.SetActive(true);
            flashUntil = Time.time + 0.08f;
        }

        private void Disband(string id)
        {
            if (!crews.TryGetValue(id, out Crew crew)) return;
            foreach (BoatshipsDeckFighter man in crew.men)
            {
                if (man == null) continue;
                man.Died -= OnManDied;
                owners.Remove(man);
                Destroy(man.gameObject);
            }
            crews.Remove(id);
        }

        private void Clear()
        {
            if (crews.Count == 0) return;
            stale.Clear();
            stale.AddRange(crews.Keys);
            foreach (string id in stale) Disband(id);
        }

        private void OnDestroy()
        {
            Clear();
            if (flash != null) Destroy(flash);
            if (flashMaterial != null) Destroy(flashMaterial);
        }
    }
}
