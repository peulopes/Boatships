using System;
using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>New-game character creation: name, background, skill points, home waters and colors.</summary>
    public sealed class BoatshipsCharacterCreation : MonoBehaviour
    {
        public const int FreePoints = 5;
        public const int CreationCap = 6;

        private static readonly string[] SkillHelp =
        {
            "Faster world-map travel, better escapes and a wider sighting range.",
            "More cannon damage in battle. Pays well as a gunner's mate.",
            "Wins fights aboard ships you work on. Pays well as a marine.",
            "Cheaper stores, fares and ships. Pays well as a purser.",
            "Pirates hesitate to attack you, and every job pays a little more."
        };
        private static readonly Color[] SailColors =
        {
            new Color(0.95f, 0.9f, 0.72f), new Color(0.97f, 0.97f, 0.97f), new Color(0.7f, 0.12f, 0.12f),
            new Color(0.15f, 0.22f, 0.45f), new Color(0.14f, 0.14f, 0.15f), new Color(0.2f, 0.45f, 0.3f)
        };
        private static readonly Color[] FlagColors =
        {
            new Color(0.95f, 0.75f, 0.2f), new Color(0.85f, 0.15f, 0.15f), new Color(0.2f, 0.42f, 0.95f),
            new Color(0.1f, 0.1f, 0.1f), new Color(0.97f, 0.97f, 0.97f), new Color(0.55f, 0.25f, 0.65f)
        };
        private static readonly string[] RandomNames =
        {
            "Anne Halyard", "Edmund Reef", "Isla Storm", "Corin Vale", "Mara Blackwood", "Silas Drake",
            "Tamsin Gale", "Bram Marsh", "Wren Cross", "Elin Stern", "Jules Harbour", "Rosa Kestrel"
        };

        private Canvas canvas;
        private InputField nameField;
        private readonly Button[] backgroundButtons = new Button[5];
        private readonly Button[] homeButtons = new Button[3];
        private readonly Text[] skillLabels = new Text[CharacterProfile.SkillCount];
        private readonly Image[] sailSwatches = new Image[6];
        private readonly Image[] flagSwatches = new Image[6];
        private Text backgroundText;
        private Text pointsText;
        private Text summaryText;
        private Text statusText;
        private Button backButton;
        private CharacterBackground background = CharacterBackground.MerchantsChild;
        private HomeRegion home = HomeRegion.MerchantLeague;
        private readonly int[] allocated = new int[CharacterProfile.SkillCount];
        private int sailColor;
        private int flagColor;
        private Action onBack;
        private Func<CharacterProfile, string> onBegin;
        private bool submitting;

        public bool IsOpen => canvas != null && canvas.gameObject.activeSelf;

        private void Awake()
        {
            Build();
            canvas.gameObject.SetActive(false);
        }

        /// <summary>Shows the screen. onBegin receives the profile and returns an error message, or null on success.</summary>
        public void Open(bool allowBack, Action back, Func<CharacterProfile, string> begin)
        {
            onBack = back; onBegin = begin; submitting = false;
            backButton.gameObject.SetActive(allowBack);
            statusText.text = "";
            if (string.IsNullOrEmpty(nameField.text)) nameField.text = RandomNames[UnityEngine.Random.Range(0, RandomNames.Length)];
            canvas.gameObject.SetActive(true);
            Refresh();
        }

        public void Close() { if (canvas != null) canvas.gameObject.SetActive(false); }

        /// <summary>Editor play-validation helper.</summary>
        public void BeginForTesting(CharacterBackground chosen)
        {
            background = chosen;
            Array.Clear(allocated, 0, allocated.Length);
            nameField.text = "Test Captain";
            Begin();
        }

        // ───────────────────────────── Layout ─────────────────────────────

        private void Build()
        {
            canvas = BoatshipsUI.CreateCanvas("Boatships Character Creation", 50, transform);
            RectTransform safe = BoatshipsUI.SafeArea(canvas);
            Image backdrop = BoatshipsUI.Image("Backdrop", canvas.transform, BoatshipsUI.Background);
            BoatshipsUI.Stretch(backdrop.rectTransform);
            backdrop.transform.SetAsFirstSibling();

            Text title = BoatshipsUI.Label("CREATE YOUR CHARACTER", safe, 50, TextAnchor.MiddleCenter);
            title.color = BoatshipsUI.Title;
            BoatshipsUI.Anchor(title.rectTransform, 0.1f, 0.9f, 0.9f, 0.99f);

            RectTransform left = Column(safe, 0.02f, 0.335f);
            BoatshipsUI.FixedLabel("Name", left, 26, 34f).color = BoatshipsUI.Title;
            HorizontalLayoutGroup nameRow = BoatshipsUI.Row(left, 60f, 8f);
            nameRow.childForceExpandWidth = false;
            nameField = CreateInputField(nameRow.transform);
            Button random = BoatshipsUI.Button("Random", nameRow.transform,
                () => nameField.text = RandomNames[UnityEngine.Random.Range(0, RandomNames.Length)], 60f, 20);
            random.gameObject.GetComponent<LayoutElement>().preferredWidth = 140f;
            BoatshipsUI.FixedLabel("Background", left, 26, 40f).color = BoatshipsUI.Title;
            for (int i = 0; i < CharacterBackgrounds.All.Length; i++)
            {
                CharacterBackground option = CharacterBackgrounds.All[i];
                BackgroundInfo info = CharacterBackgrounds.Get(option);
                backgroundButtons[i] = BoatshipsUI.Button(info.Title + (info.Ship ? "" : "  (no ship)"), left,
                    () => { background = option; ClampAllocation(); Refresh(); }, 54f, 22);
            }
            backgroundText = BoatshipsUI.FixedLabel("", left, 20, 190f, TextAnchor.UpperLeft);

            RectTransform middle = Column(safe, 0.35f, 0.645f);
            pointsText = BoatshipsUI.FixedLabel("", middle, 26, 40f);
            pointsText.color = BoatshipsUI.Title;
            for (int i = 0; i < CharacterProfile.SkillCount; i++)
            {
                int skill = i;
                HorizontalLayoutGroup row = BoatshipsUI.Row(middle, 96f, 8f);
                row.childForceExpandWidth = false;
                Text label = BoatshipsUI.Label("", row.transform, 21, TextAnchor.MiddleLeft);
                label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                skillLabels[i] = label;
                Button minus = BoatshipsUI.Button("-", row.transform, () => ChangeSkill(skill, -1), 70f, 32);
                minus.GetComponent<LayoutElement>().preferredWidth = 70f;
                Button plus = BoatshipsUI.Button("+", row.transform, () => ChangeSkill(skill, 1), 70f, 32);
                plus.GetComponent<LayoutElement>().preferredWidth = 70f;
            }

            RectTransform right = Column(safe, 0.66f, 0.98f);
            BoatshipsUI.FixedLabel("Home waters", right, 26, 36f).color = BoatshipsUI.Title;
            for (int i = 0; i < 3; i++)
            {
                HomeRegion region = (HomeRegion)i;
                homeButtons[i] = BoatshipsUI.Button(BoatshipsFactions.Name(region), right, () => { home = region; Refresh(); }, 52f, 22);
            }
            BoatshipsUI.FixedLabel("Sail color", right, 24, 36f).color = BoatshipsUI.Title;
            Swatches(right, SailColors, sailSwatches, index => { sailColor = index; Refresh(); });
            BoatshipsUI.FixedLabel("Flag color", right, 24, 36f).color = BoatshipsUI.Title;
            Swatches(right, FlagColors, flagSwatches, index => { flagColor = index; Refresh(); });
            summaryText = BoatshipsUI.FixedLabel("", right, 20, 230f, TextAnchor.UpperLeft);

            RectTransform footer = BoatshipsUI.Rect("Footer", safe, typeof(HorizontalLayoutGroup));
            BoatshipsUI.Anchor(footer, 0.02f, 0.015f, 0.98f, 0.1f);
            HorizontalLayoutGroup bar = footer.GetComponent<HorizontalLayoutGroup>();
            bar.spacing = 16f; bar.childControlWidth = true; bar.childControlHeight = true;
            bar.childForceExpandWidth = false; bar.childForceExpandHeight = true;
            backButton = BoatshipsUI.Button("Back", footer, () => { Close(); onBack?.Invoke(); }, 70f, 26);
            backButton.GetComponent<LayoutElement>().preferredWidth = 260f;
            statusText = BoatshipsUI.Label("", footer, 22, TextAnchor.MiddleCenter);
            statusText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            Button begin = BoatshipsUI.Button("Begin your story", footer, Begin, 70f, 28);
            begin.GetComponent<LayoutElement>().preferredWidth = 420f;
            begin.GetComponent<Image>().color = BoatshipsUI.Accent;
            begin.GetComponentInChildren<Text>().color = BoatshipsUI.Ink;
        }

        private static RectTransform Column(RectTransform parent, float xMin, float xMax)
        {
            RectTransform column = BoatshipsUI.Image("Column", parent, BoatshipsUI.Panel).rectTransform;
            BoatshipsUI.Anchor(column, xMin, 0.12f, xMax, 0.89f);
            BoatshipsUI.Column(column, 8f, 16);
            return column;
        }

        private static InputField CreateInputField(Transform parent)
        {
            RectTransform rect = BoatshipsUI.Rect("Name Field", parent, typeof(Image), typeof(InputField), typeof(LayoutElement));
            rect.GetComponent<LayoutElement>().flexibleWidth = 1f;
            Image image = rect.GetComponent<Image>();
            image.color = new Color(0.08f, 0.2f, 0.25f);
            Text text = BoatshipsUI.Label("", rect, 26);
            BoatshipsUI.Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(14f, 4f); text.rectTransform.offsetMax = new Vector2(-14f, -4f);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            Text placeholder = BoatshipsUI.Label("Your name", rect, 26);
            BoatshipsUI.Stretch(placeholder.rectTransform);
            placeholder.rectTransform.offsetMin = text.rectTransform.offsetMin;
            placeholder.rectTransform.offsetMax = text.rectTransform.offsetMax;
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            placeholder.fontStyle = FontStyle.Italic;
            InputField field = rect.GetComponent<InputField>();
            field.targetGraphic = image;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.characterLimit = 22;
            field.lineType = InputField.LineType.SingleLine;
            return field;
        }

        private static void Swatches(RectTransform parent, Color[] colors, Image[] swatches, Action<int> select)
        {
            HorizontalLayoutGroup row = BoatshipsUI.Row(parent, 58f, 10f);
            for (int i = 0; i < colors.Length; i++)
            {
                int index = i;
                RectTransform rect = BoatshipsUI.Rect("Swatch " + i, row.transform, typeof(Image), typeof(Button), typeof(Outline));
                Image image = rect.GetComponent<Image>();
                image.color = colors[i];
                Button button = rect.GetComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => { BoatshipsUI.ClickSound?.Invoke(); select(index); });
                Outline outline = rect.GetComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(4f, -4f);
                swatches[i] = image;
            }
        }

        // ───────────────────────────── State ─────────────────────────────

        private int BaseSkill(int skill) => CharacterBackgrounds.Get(background).SkillBonus[skill];

        private int Remaining()
        {
            int used = 0;
            foreach (int points in allocated) used += points;
            return FreePoints - used;
        }

        private void ChangeSkill(int skill, int delta)
        {
            int next = allocated[skill] + delta;
            if (next < 0 || (delta > 0 && (Remaining() <= 0 || BaseSkill(skill) + next > CreationCap))) return;
            allocated[skill] = next;
            Refresh();
        }

        private void ClampAllocation()
        {
            for (int i = 0; i < allocated.Length; i++)
                allocated[i] = Mathf.Clamp(allocated[i], 0, Mathf.Max(0, CreationCap - BaseSkill(i)));
        }

        private void Refresh()
        {
            BackgroundInfo info = CharacterBackgrounds.Get(background);
            for (int i = 0; i < backgroundButtons.Length; i++)
                backgroundButtons[i].GetComponent<Image>().color =
                    CharacterBackgrounds.All[i] == background ? BoatshipsUI.Selected : BoatshipsUI.ButtonColor;
            for (int i = 0; i < homeButtons.Length; i++)
                homeButtons[i].GetComponent<Image>().color = (int)home == i ? BoatshipsUI.Selected : BoatshipsUI.ButtonColor;
            for (int i = 0; i < sailSwatches.Length; i++) sailSwatches[i].GetComponent<Outline>().enabled = i == sailColor;
            for (int i = 0; i < flagSwatches.Length; i++) flagSwatches[i].GetComponent<Outline>().enabled = i == flagColor;
            backgroundText.text = info.Description;
            pointsText.text = "Skills — " + Remaining() + " point(s) to spend";
            for (int i = 0; i < skillLabels.Length; i++)
            {
                int bonus = BaseSkill(i);
                skillLabels[i].text = "<b>" + (CharacterSkill)i + "  " + (bonus + allocated[i]) + "</b>" +
                    (bonus > 0 ? "  (background +" + bonus + ")" : "") + "\n<size=17>" + SkillHelp[i] + "</size>";
                skillLabels[i].supportRichText = true;
            }
            summaryText.text = "Start: " + info.Gold + " gold, " + info.Rations + " rations" +
                (info.Crew > 0 ? ", " + info.Crew + " sailors" : "") + "\n" +
                (info.Ship ? "You begin as captain of your own sloop." :
                    "You begin with NO ship. Work aboard AI ships for wages, or book passage, until you can buy one (about 600 gold).") +
                "\nHome port: a " + BoatshipsFactions.Name(home) + " harbor." +
                (info.HomeReputation > 0 ? "\nStanding there +" + info.HomeReputation + "." : "") +
                (info.NavyReputation < 0 ? "\nThe Royal Navy wants you back in chains (" + info.NavyReputation + ")." : "");
        }

        private void Begin()
        {
            if (submitting) return;
            string chosenName = nameField.text != null ? nameField.text.Trim() : "";
            if (chosenName.Length == 0) { statusText.text = "Enter a name for your character."; return; }
            var profile = new CharacterProfile
            {
                name = chosenName,
                background = background,
                home = home,
                sailColor = SailColors[sailColor],
                flagColor = FlagColors[flagColor],
                ownsShip = CharacterBackgrounds.Get(background).Ship
            };
            for (int i = 0; i < CharacterProfile.SkillCount; i++) profile.skills[i] = BaseSkill(i) + allocated[i];
            submitting = true;
            string error = onBegin != null ? onBegin(profile) : "Character creation is not connected.";
            if (error == null) { statusText.text = "Preparing your world…"; return; }
            submitting = false;
            statusText.text = error;
        }
    }
}
