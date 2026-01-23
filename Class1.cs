using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using System;

namespace ValheimMod
{
    [BepInPlugin("com.VerdoxOP.valheim-modmenu", "Valheim ModMenu", "4.9.0")]
    [BepInProcess("valheim.exe")]
    public class ValheimModMenu : BaseUnityPlugin
    {
        public static ValheimModMenu context;
        public bool showMenu = false;
        private Rect windowRect = new Rect(20, 20, 750, 850);
        private const string Watermark = "VerdoxOP";

        private enum MenuMode { Spawner, Skills, World, Combat, Inventory, Recipes }
        private MenuMode currentMode = MenuMode.Spawner;

        // --- TOGGLES ---
        public bool isGodMode = false;
        public bool isInfStamina = false;
        public bool isNoCostBuild = false;
        public bool isFlightMode = false;
        public bool isSuperStats = false;
        public bool isInstaMine = false;
        public bool isNoDurability = false;
        public bool isNoFog = false;
        public bool isNoDof = false;
        public bool isVacuum = false;
        public bool isGhost = false;
        public bool isWaterWalk = false;
        public bool isFastProcess = false;

        public Dictionary<string, bool> activeBossPowers = new Dictionary<string, bool>()
        {
            { "GP_Eikthyr", false }, { "GP_TheElder", false }, { "GP_Bonemass", false },
            { "GP_Moder", false }, { "GP_Yagluth", false }, { "GP_Queen", false }, { "GP_Fader", false }
        };

        // --- COMBAT ---
        public bool isAimbot = false;
        public bool isPredictor = false;
        public float aimFov = 150f;
        public bool isMagicBullet = false;
        public bool isRapidAttack = false;
        public bool isEsp = false;
        public Dictionary<string, bool> espFilters = new Dictionary<string, bool>();
        private Vector2 espScrollPos;
        private bool espListPopulated = false;

        // --- INVENTORY ---
        public static ItemDrop.ItemData LastHoveredItem = null;
        public bool isRecipeHelper = false;
        private string itemStackInput = "1";

        // --- RECIPES ---
        private Vector2 recipeScrollPos;
        private string recipeSearch = "";
        private bool showUnlockConfirmation = false;
        private bool showResetConfirmation = false;
        private List<string> recipeBackup = new List<string>();

        // --- SPAWNER ---
        private string selectedPrefabName = "SwordIron";
        private string amountInput = "1";
        private string searchInput = "";
        private enum Category { All, Weapons, Gear, Food, Resources, Creatures, Misc }
        private Category currentCategory = Category.All;
        private Dictionary<Category, List<string>> categorizedItems = new Dictionary<Category, List<string>>();

        // --- TELEPORT LOGIC ---
        private bool isTeleporting = false;
        private Vector3 teleportTargetXZ;

        private Vector2 scrollPosition;
        private Vector2 skillScrollPosition;
        private Dictionary<Skills.SkillType, Skills.Skill> cachedSkills = new Dictionary<Skills.SkillType, Skills.Skill>();

        void Awake()
        {
            context = this;
            foreach (Category cat in System.Enum.GetValues(typeof(Category))) categorizedItems[cat] = new List<string>();
            Harmony harmony = new Harmony("com.VerdoxOP.valheim-modmenu");
            harmony.PatchAll();
            Logger.LogInfo("VerdoxOP ModMenu Loaded Successfully.");
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Insert)) ToggleMenu();
            if (showMenu && Input.GetKeyDown(KeyCode.Escape)) ToggleMenu();

            if (showMenu)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (InventoryGui.instance != null && InventoryGui.IsVisible() && !IsMouseOverMenu())
            {
                InventoryGrid grid = InventoryGui.instance.m_playerGrid;
                if (grid != null)
                {
                    ItemDrop.ItemData hover = Traverse.Create(grid).Method("GetHoveredItem").GetValue<ItemDrop.ItemData>();
                    if (hover != null) LastHoveredItem = hover;
                }
            }

            if (isNoFog) RenderSettings.fog = false;

            if (isNoDof && Camera.main != null)
            {
                MonoBehaviour[] behaviours = Camera.main.GetComponents<MonoBehaviour>();
                foreach (var b in behaviours) { if (b.GetType().Name.Contains("PostProcessLayer")) b.enabled = false; }
            }
            else if (!isNoDof && Camera.main != null)
            {
                MonoBehaviour[] behaviours = Camera.main.GetComponents<MonoBehaviour>();
                foreach (var b in behaviours) { if (b.GetType().Name.Contains("PostProcessLayer")) b.enabled = true; }
            }

            if (Player.m_localPlayer != null)
            {
                Player p = Player.m_localPlayer;
                var pTraverse = Traverse.Create(p);

                // --- SMART TELEPORT LANDING ---
                if (isTeleporting)
                {
                    float groundHeight;
                    if (ZoneSystem.instance.GetGroundHeight(teleportTargetXZ, out groundHeight))
                    {
                        Vector3 landPos = teleportTargetXZ;
                        landPos.y = groundHeight + 1f;
                        p.TeleportTo(landPos, p.transform.rotation, true);
                        MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "VerdoxOP: Ground Verified - Landing!");
                        isTeleporting = false;
                    }
                }

                if (isInfStamina) p.AddStamina(p.GetMaxStamina());

                bool currentNoCost = pTraverse.Field("m_noPlacementCost").GetValue<bool>();
                if (isNoCostBuild != currentNoCost) pTraverse.Field("m_noPlacementCost").SetValue(isNoCostBuild);

                pTraverse.Field("m_debugFly").SetValue(isFlightMode);
                pTraverse.Field("m_noclip").SetValue(isFlightMode);

                if (isSuperStats) { p.m_maxCarryWeight = 9999f; p.m_runSpeed = 20f; p.m_jumpForce = 20f; }
                else { if (p.m_maxCarryWeight > 3000) p.m_maxCarryWeight = 300f; if (p.m_runSpeed > 10) p.m_runSpeed = 7f; }

                if (isVacuum) p.m_autoPickupRange = 50f;
                else p.m_autoPickupRange = 2f;

                p.SetGhostMode(isGhost);

                if (isWaterWalk && !isFlightMode)
                {
                    float waterLevel = pTraverse.Field("m_waterLevel").GetValue<float>();

                    if (p.transform.position.y < waterLevel && !p.IsSwimming())
                    {
                        Vector3 pos = p.transform.position;
                        pos.y = waterLevel;
                        p.transform.position = pos;

                        Rigidbody rb = p.GetComponent<Rigidbody>();
                        if (rb)
                        {
                            Vector3 vel = rb.linearVelocity;
                            vel.y = 0;
                            rb.linearVelocity = vel;
                        }
                    }
                }

                if (isNoDurability)
                {
                    if (p.GetInventory() != null)
                    {
                        foreach (ItemDrop.ItemData item in p.GetInventory().GetAllItems())
                        {
                            if (item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability())
                            {
                                item.m_durability = item.GetMaxDurability();
                            }
                        }
                    }
                }

                if (isRapidAttack)
                {
                    pTraverse.Field("m_attackCooldown").SetValue(0f);
                    ItemDrop.ItemData weapon = p.GetCurrentWeapon();
                    if (weapon != null && weapon.m_shared != null && weapon.m_shared.m_attack != null)
                    {
                        var attackTraverse = Traverse.Create(weapon.m_shared.m_attack);
                        attackTraverse.Field("m_attackCooldown").SetValue(0f);
                        attackTraverse.Field("m_drawDurationMin").SetValue(0f);
                    }
                }

                if (isAimbot && Input.GetMouseButton(1))
                {
                    Character target = GetBestTarget();
                    if (target != null)
                    {
                        Vector3 direction = (target.GetCenterPoint() - GameCamera.instance.transform.position).normalized;
                        pTraverse.Field("m_lookDir").SetValue(Vector3.Slerp(p.GetLookDir(), direction, Time.deltaTime * 5f));
                        p.transform.rotation = Quaternion.LookRotation(p.GetLookDir());
                        GameCamera.instance.transform.rotation = Quaternion.LookRotation(p.GetLookDir());
                    }
                }

                var seMan = p.GetSEMan();
                foreach (var power in activeBossPowers)
                {
                    int powerHash = power.Key.GetStableHashCode();
                    if (power.Value)
                    {
                        if (!seMan.HaveStatusEffect(powerHash))
                        {
                            StatusEffect effectPrefab = ObjectDB.instance.GetStatusEffect(powerHash);
                            if (effectPrefab != null) seMan.AddStatusEffect(effectPrefab);
                        }
                        else
                        {
                            StatusEffect effect = seMan.GetStatusEffect(powerHash);
                            if (effect != null) Traverse.Create(effect).Field("m_time").SetValue(0f);
                        }
                    }
                    else
                    {
                        if (seMan.HaveStatusEffect(powerHash)) seMan.RemoveStatusEffect(powerHash);
                    }
                }
            }
        }

        public bool IsMouseOverMenu()
        {
            if (!showMenu) return false;
            Vector2 mousePos = Input.mousePosition;
            mousePos.y = Screen.height - mousePos.y;
            return windowRect.Contains(mousePos);
        }

        void ToggleMenu()
        {
            showMenu = !showMenu;
            if (showMenu)
            {
                if (categorizedItems[Category.All].Count == 0) LoadPrefabs();
                if (Player.m_localPlayer != null) LoadSkills();
                if (!espListPopulated) PopulateEspList();
            }
        }

        void OnGUI()
        {
            if (Player.m_localPlayer != null && !showMenu)
            {
                if (isEsp) DrawEsp();
                if (isPredictor) DrawPredictor();
            }

            if (showMenu)
            {
                GUI.backgroundColor = Color.black;
                windowRect = GUI.Window(888, windowRect, DrawMyMenu, "Valheim ModMenu by VerdoxOP");
            }
        }

        void DrawMyMenu(int windowID)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(isGodMode ? "God: <color=green>ON</color>" : "God: <color=red>OFF</color>")) isGodMode = !isGodMode;
            if (GUILayout.Button(isInfStamina ? "Stamina: <color=green>ON</color>" : "Stamina: <color=red>OFF</color>")) isInfStamina = !isInfStamina;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(isNoCostBuild ? "Build (NoCost/Anywhere): <color=green>ON</color>" : "Build (NoCost/Anywhere): <color=red>OFF</color>")) isNoCostBuild = !isNoCostBuild;
            if (GUILayout.Button(isFlightMode ? "Flight: <color=green>ON</color>" : "Flight: <color=red>OFF</color>")) isFlightMode = !isFlightMode;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            DrawTabButton("SPAWNER", MenuMode.Spawner);
            DrawTabButton("SKILLS", MenuMode.Skills);
            DrawTabButton("WORLD", MenuMode.World);
            DrawTabButton("COMBAT", MenuMode.Combat);
            DrawTabButton("INVENTORY", MenuMode.Inventory);
            DrawTabButton("RECIPES", MenuMode.Recipes);
            GUILayout.EndHorizontal();

            GUILayout.BeginVertical(GUI.skin.box);
            switch (currentMode)
            {
                case MenuMode.Spawner: DrawSpawnerInterface(); break;
                case MenuMode.Skills: DrawSkillsInterface(); break;
                case MenuMode.World: DrawWorldInterface(); break;
                case MenuMode.Combat: DrawCombatInterface(); break;
                case MenuMode.Inventory: DrawInventoryInterface(); break;
                case MenuMode.Recipes: DrawRecipeInterface(); break;
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<color=cyan>Made by {Watermark}</color>", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        void DrawTabButton(string name, MenuMode mode)
        {
            GUI.backgroundColor = (currentMode == mode) ? Color.cyan : Color.white;
            if (GUILayout.Button(name)) currentMode = mode;
            GUI.backgroundColor = Color.black;
        }

        void DrawSpawnerInterface()
        {
            int columns = 4;
            var categories = System.Enum.GetValues(typeof(Category)).Cast<Category>().ToArray();
            for (int i = 0; i < categories.Length; i += columns)
            {
                GUILayout.BeginHorizontal();
                for (int j = 0; j < columns; j++)
                {
                    if (i + j < categories.Length)
                    {
                        Category cat = categories[i + j];
                        GUI.backgroundColor = (cat == currentCategory) ? Color.green : Color.white;
                        if (GUILayout.Button(cat.ToString())) { currentCategory = cat; scrollPosition = Vector2.zero; GUI.FocusControl(null); }
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUI.backgroundColor = Color.black;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(50));
            searchInput = GUILayout.TextField(searchInput);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Selected:", GUILayout.Width(60));
            selectedPrefabName = GUILayout.TextField(selectedPrefabName);
            GUILayout.Label("Qty:", GUILayout.Width(30));
            amountInput = GUILayout.TextField(amountInput, GUILayout.Width(40));
            if (GUILayout.Button("SPAWN")) { if (!int.TryParse(amountInput, out int qty)) qty = 1; SpawnObject(selectedPrefabName, qty); GUI.FocusControl(null); }
            GUILayout.EndHorizontal();

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUI.skin.box);

            if (categorizedItems.ContainsKey(currentCategory))
            {
                List<string> currentList = categorizedItems[currentCategory];
                if (currentList != null && currentList.Count > 0)
                {
                    foreach (string name in currentList)
                    {
                        if (string.IsNullOrEmpty(searchInput) || name.ToLower().Contains(searchInput.ToLower()))
                        {
                            if (GUILayout.Button(name)) { selectedPrefabName = name; GUI.FocusControl(null); }
                        }
                    }
                }
                else
                {
                    if (GUILayout.Button("Load Database")) LoadPrefabs();
                }
            }
            GUILayout.EndScrollView();
        }

        void LoadPrefabs()
        {
            if (ZNetScene.instance == null) return;
            foreach (var list in categorizedItems.Values) list.Clear();

            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                string name = prefab.name;
                categorizedItems[Category.All].Add(name);
                ItemDrop item = prefab.GetComponent<ItemDrop>();
                Character character = prefab.GetComponent<Character>();

                if (character != null && name != "Player") categorizedItems[Category.Creatures].Add(name);
                else if (item != null)
                {
                    switch (item.m_itemData.m_shared.m_itemType)
                    {
                        case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                        case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                        case ItemDrop.ItemData.ItemType.Bow:
                        case ItemDrop.ItemData.ItemType.Shield:
                        case ItemDrop.ItemData.ItemType.Ammo: categorizedItems[Category.Weapons].Add(name); break;
                        case ItemDrop.ItemData.ItemType.Helmet:
                        case ItemDrop.ItemData.ItemType.Chest:
                        case ItemDrop.ItemData.ItemType.Legs:
                        case ItemDrop.ItemData.ItemType.Shoulder:
                        case ItemDrop.ItemData.ItemType.Utility: categorizedItems[Category.Gear].Add(name); break;
                        case ItemDrop.ItemData.ItemType.Consumable: categorizedItems[Category.Food].Add(name); break;
                        case ItemDrop.ItemData.ItemType.Material:
                        case ItemDrop.ItemData.ItemType.Trophy: categorizedItems[Category.Resources].Add(name); break;
                        default: categorizedItems[Category.Misc].Add(name); break;
                    }
                }
                else categorizedItems[Category.Misc].Add(name);
            }
            foreach (var list in categorizedItems.Values) list.Sort();
        }

        void DrawRecipeInterface()
        {
            if (Player.m_localPlayer == null) { GUILayout.Label("Enter world first."); return; }

            GUILayout.Label("<b>Recipe Manager by VerdoxOP</b>");
            GUILayout.BeginHorizontal();

            if (!showUnlockConfirmation)
            {
                if (GUILayout.Button("UNLOCK ALL RECIPES")) showUnlockConfirmation = true;
            }
            else
            {
                GUILayout.Label("<color=red><b>SURE?</b></color>");
                if (GUILayout.Button("YES")) { UnlockAllRecipes(); showUnlockConfirmation = false; }
                if (GUILayout.Button("NO")) showUnlockConfirmation = false;
            }

            if (!showResetConfirmation)
            {
                if (GUILayout.Button("RESET TO BASICS")) showResetConfirmation = true;
            }
            else
            {
                GUILayout.Label("<color=red><b>WIPE?</b></color>");
                if (GUILayout.Button("YES")) { ResetRecipesToBasics(); showResetConfirmation = false; }
                if (GUILayout.Button("NO")) showResetConfirmation = false;
            }

            if (recipeBackup.Count > 0)
            {
                if (GUILayout.Button("UNDO")) RestoreRecipeBackup();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("<b>Manual Selection</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(60));
            recipeSearch = GUILayout.TextField(recipeSearch);
            GUILayout.EndHorizontal();

            recipeScrollPos = GUILayout.BeginScrollView(recipeScrollPos, GUI.skin.box);

            if (ObjectDB.instance != null)
            {
                var playerTraverse = Traverse.Create(Player.m_localPlayer);
                HashSet<string> knownRecipes = playerTraverse.Field("m_knownRecipes").GetValue<HashSet<string>>();

                foreach (Recipe r in ObjectDB.instance.m_recipes)
                {
                    if (r == null || r.m_item == null) continue;

                    string recipeName = r.m_item.m_itemData.m_shared.m_name;
                    string rawName = r.name;

                    if (!string.IsNullOrEmpty(recipeSearch) &&
                        !recipeName.ToLower().Contains(recipeSearch.ToLower()) &&
                        !rawName.ToLower().Contains(recipeSearch.ToLower())) continue;

                    bool isKnown = knownRecipes.Contains(rawName);

                    GUILayout.BeginHorizontal();
                    if (isKnown)
                    {
                        if (GUILayout.Button($"<color=green>[KNOWN]</color> {recipeName}"))
                        {
                            knownRecipes.Remove(rawName);
                            playerTraverse.Method("UpdateKnownRecipesList").GetValue();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button($"<color=red>[LOCKED]</color> {recipeName}"))
                        {
                            playerTraverse.Method("AddKnownRecipe", r).GetValue();
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
        }

        void UnlockAllRecipes()
        {
            if (Player.m_localPlayer == null || ObjectDB.instance == null) return;
            var playerTraverse = Traverse.Create(Player.m_localPlayer);
            HashSet<string> knownRecipes = playerTraverse.Field("m_knownRecipes").GetValue<HashSet<string>>();
            recipeBackup.Clear();
            foreach (string r in knownRecipes) recipeBackup.Add(r);
            foreach (Recipe r in ObjectDB.instance.m_recipes) playerTraverse.Method("AddKnownRecipe", r).GetValue();
            playerTraverse.Method("UpdateKnownRecipesList").GetValue();
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "VerdoxOP: All Recipes Unlocked!");
        }

        void ResetRecipesToBasics()
        {
            if (Player.m_localPlayer == null) return;
            var playerTraverse = Traverse.Create(Player.m_localPlayer);
            HashSet<string> knownRecipes = playerTraverse.Field("m_knownRecipes").GetValue<HashSet<string>>();
            recipeBackup.Clear();
            foreach (string r in knownRecipes) recipeBackup.Add(r);
            knownRecipes.Clear();
            string[] basics = { "Hammer", "AxeStone", "Torch", "Club", "Hoe" };
            foreach (string item in basics) knownRecipes.Add(item);
            playerTraverse.Method("UpdateKnownRecipesList").GetValue();
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "VerdoxOP: Reset to Basics!");
        }

        void RestoreRecipeBackup()
        {
            if (Player.m_localPlayer == null) return;
            var playerTraverse = Traverse.Create(Player.m_localPlayer);
            HashSet<string> knownRecipes = playerTraverse.Field("m_knownRecipes").GetValue<HashSet<string>>();
            knownRecipes.Clear();
            foreach (string r in recipeBackup) knownRecipes.Add(r);
            playerTraverse.Method("UpdateKnownRecipesList").GetValue();
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "VerdoxOP: Recipes Restored!");
        }

        void DrawWorldInterface()
        {
            if (Player.m_localPlayer == null) { GUILayout.Label("Enter world first."); return; }
            GUILayout.Label($"<b>VerdoxOP World Mods</b>");
            if (GUILayout.Button(isSuperStats ? "Super Stats: <color=green>ON</color>" : "Super Stats: <color=red>OFF</color>")) isSuperStats = !isSuperStats;
            if (GUILayout.Button(isInstaMine ? "InstaMine: <color=green>ON</color>" : "InstaMine: <color=red>OFF</color>")) isInstaMine = !isInstaMine;
            if (GUILayout.Button(isNoDurability ? "Infinite Durability: <color=green>ON</color>" : "Infinite Durability: <color=red>OFF</color>")) isNoDurability = !isNoDurability;
            if (GUILayout.Button(isNoFog ? "No Fog (Clear Sky): <color=green>ON</color>" : "No Fog (Clear Sky): <color=red>OFF</color>")) isNoFog = !isNoFog;
            if (GUILayout.Button(isNoDof ? "No Blur (Depth of Field): <color=green>ON</color>" : "No Blur (Depth of Field): <color=red>OFF</color>")) isNoDof = !isNoDof;

            GUILayout.Space(10);
            if (GUILayout.Button(isVacuum ? "Loot Vacuum (50m): <color=green>ON</color>" : "Loot Vacuum (50m): <color=red>OFF</color>")) isVacuum = !isVacuum;
            if (GUILayout.Button(isGhost ? "Ghost Mode (Invisible): <color=green>ON</color>" : "Ghost Mode (Invisible): <color=red>OFF</color>")) isGhost = !isGhost;
            if (GUILayout.Button(isWaterWalk ? "Jesus Mode (Water Walk): <color=green>ON</color>" : "Jesus Mode (Water Walk): <color=red>OFF</color>")) isWaterWalk = !isWaterWalk;
            // NEW: Fast Process
            if (GUILayout.Button(isFastProcess ? "Fast Process (Cook/Smelt): <color=green>ON</color>" : "Fast Process (Cook/Smelt): <color=red>OFF</color>")) isFastProcess = !isFastProcess;

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("TELEPORT TO PIN")) TeleportToPin();
            if (GUILayout.Button("TAME ALL NEARBY")) TameNearby();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("<b>Boss Powers (Infinite)</b>");
            DrawBossToggle("Eikthyr (Run/Jump)", "GP_Eikthyr");
            DrawBossToggle("The Elder (Wood Cutting)", "GP_TheElder");
            DrawBossToggle("Bonemass (Phys Resist)", "GP_Bonemass");
            DrawBossToggle("Moder (Tailwind)", "GP_Moder");
            DrawBossToggle("Yagluth (Elem Resist)", "GP_Yagluth");
            DrawBossToggle("The Queen (Eitr/Mining)", "GP_Queen");
            DrawBossToggle("Fader (Carry/Speed)", "GP_Fader");

            GUILayout.Space(10);
            GUILayout.Label("<b>Environment</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Skip to Morning")) { if (EnvMan.instance) EnvMan.instance.SkipToMorning(); }
            if (GUILayout.Button("Force Tailwind"))
            {
                if (EnvMan.instance)
                {
                    Vector3 lookDir = Player.m_localPlayer.transform.forward;
                    float angle = Vector3.SignedAngle(Vector3.forward, lookDir, Vector3.up);
                    if (angle < 0) angle += 360f;
                    var envTraverse = Traverse.Create(EnvMan.instance);
                    envTraverse.Field("m_debugWind").SetValue(true);
                    envTraverse.Field("m_debugWindAngle").SetValue(angle);
                    envTraverse.Field("m_debugWindIntensity").SetValue(1.0f);
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "VerdoxOP: Wind Set!");
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label("<b>Map & Bosses</b>");
            if (GUILayout.Button("REVEAL MAP")) { if (Minimap.instance) Minimap.instance.ExploreAll(); }
            if (GUILayout.Button("UNLOCK BOSSES"))
            {
                if (ZoneSystem.instance)
                {
                    string[] bosses = { "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen", "defeated_fader" };
                    foreach (string boss in bosses) ZoneSystem.instance.SetGlobalKey(boss);
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "VerdoxOP: All Bosses Defeated!");
                }
            }
        }

        void TeleportToPin()
        {
            if (Minimap.instance == null || Player.m_localPlayer == null) return;
            var miniTraverse = Traverse.Create(Minimap.instance);
            List<Minimap.PinData> pins = miniTraverse.Field("m_pins").GetValue<List<Minimap.PinData>>();
            if (pins == null) return;

            Minimap.PinData targetPin = null;
            foreach (var pin in pins) { if (pin.m_checked) { targetPin = pin; break; } }

            if (targetPin != null)
            {
                teleportTargetXZ = targetPin.m_pos;
                teleportTargetXZ.y = 200f;
                Player.m_localPlayer.TeleportTo(teleportTargetXZ, Player.m_localPlayer.transform.rotation, true);
                isTeleporting = true;
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "VerdoxOP: Teleporting... Wait for landing.");
            }
            else MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "VerdoxOP: Check (X) a map pin first!");
        }

        void TameNearby()
        {
            if (Player.m_localPlayer == null) return;
            int count = 0;
            List<Character> chars = Character.GetAllCharacters();
            foreach (Character c in chars)
            {
                if (Vector3.Distance(c.transform.position, Player.m_localPlayer.transform.position) < 50f)
                {
                    Tameable t = c.GetComponent<Tameable>();
                    if (t != null && !t.IsTamed())
                    {
                        Traverse.Create(t).Method("Tame").GetValue();
                        count++;
                    }
                }
            }
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"VerdoxOP: Tamed {count} creatures!");
        }

        void DrawBossToggle(string label, string powerName)
        {
            bool isActive = activeBossPowers[powerName];
            string color = isActive ? "green" : "red";
            if (GUILayout.Button($"{label}: <color={color}>{(isActive ? "ON" : "OFF")}</color>")) activeBossPowers[powerName] = !isActive;
        }

        void DrawInventoryInterface()
        {
            GUILayout.Label("<b>Inventory Manager</b>");
            if (GUILayout.Button(isRecipeHelper ? "Recipe Viewer (Hover Item): <color=green>ON</color>" : "Recipe Viewer (Hover Item): <color=red>OFF</color>")) isRecipeHelper = !isRecipeHelper;

            GUILayout.Space(10);
            if (LastHoveredItem != null)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"<b>Selected:</b> {LastHoveredItem.m_shared.m_name}");
                GUILayout.Label($"<b>Stack:</b> {LastHoveredItem.m_stack} / {LastHoveredItem.m_shared.m_maxStackSize}");
                GUILayout.EndVertical();
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                GUILayout.Label("New Amount:", GUILayout.Width(80));
                itemStackInput = GUILayout.TextField(itemStackInput, GUILayout.Width(60));
                if (GUILayout.Button("SET AMOUNT")) { if (int.TryParse(itemStackInput, out int newStack)) { LastHoveredItem.m_stack = newStack; MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "VerdoxOP: Stack Updated!"); } }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("FULL REPAIR ITEM")) { LastHoveredItem.m_durability = LastHoveredItem.GetMaxDurability(); MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "VerdoxOP: Item Repaired!"); }
                if (GUILayout.Button("CLONE ITEM")) { if (Player.m_localPlayer) { ItemDrop.ItemData clone = LastHoveredItem.Clone(); clone.m_stack = clone.m_shared.m_maxStackSize; Player.m_localPlayer.GetInventory().AddItem(clone); MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "VerdoxOP: Item Cloned!"); } }
            }
            else { GUILayout.Label("<color=grey>Hover over an item in inventory...</color>"); }
        }

        void DrawPredictor()
        {
            ItemDrop.ItemData weapon = Player.m_localPlayer.GetCurrentWeapon();
            if (weapon == null || weapon.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Bow) return;
            Character target = GetBestTarget();
            if (target == null) return;

            float velocity = weapon.m_shared.m_attack.m_projectileVel;
            if (velocity < 1) velocity = 50f;
            float gravity = 9.81f;

            Vector3 startPos = Player.m_localPlayer.GetEyePoint();
            Vector3 targetPos = target.GetCenterPoint();
            Vector3 diff = targetPos - startPos;
            float distXZ = new Vector2(diff.x, diff.z).magnitude;
            float y = diff.y;

            float v2 = velocity * velocity;
            float v4 = v2 * v2;
            float underRoot = v4 - gravity * (gravity * distXZ * distXZ + 2 * y * v2);

            if (underRoot >= 0)
            {
                float root = Mathf.Sqrt(underRoot);
                float angle = Mathf.Atan((v2 - root) / (gravity * distXZ));

                float vy = velocity * Mathf.Sin(angle);
                float vxz = velocity * Mathf.Cos(angle);

                Vector3 flatDir = new Vector3(diff.x, 0, diff.z).normalized;
                Vector3 launchVelocity = flatDir * vxz + Vector3.up * vy;

                Vector3 aimDir = launchVelocity.normalized;
                Vector3 screenPoint = Camera.main.WorldToScreenPoint(startPos + aimDir * 20f);

                if (screenPoint.z > 0)
                {
                    float size = 30f;
                    GUI.color = Color.red;
                    GUI.Label(new Rect(screenPoint.x - size / 2, Screen.height - screenPoint.y - size / 2, size, size), "<size=25><b>+</b></size>");
                    GUI.color = Color.white;
                }
            }
        }

        void DrawCombatInterface()
        {
            GUILayout.Label("<b>Aimbot by VerdoxOP</b>");
            if (GUILayout.Button(isAimbot ? "Aimbot: <color=green>ON</color>" : "Aimbot: <color=red>OFF</color>")) isAimbot = !isAimbot;
            if (GUILayout.Button(isPredictor ? "Arrow Predictor: <color=green>ON</color>" : "Arrow Predictor: <color=red>OFF</color>")) isPredictor = !isPredictor;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"FOV: {aimFov:F0}", GUILayout.Width(60));
            aimFov = GUILayout.HorizontalSlider(aimFov, 10f, 360f);
            GUILayout.EndHorizontal();
            GUILayout.Label("<b>Weapon Mods</b>");
            if (GUILayout.Button(isMagicBullet ? "Smart Arrow (Homing): <color=green>ON</color>" : "Smart Arrow (Homing): <color=red>OFF</color>")) isMagicBullet = !isMagicBullet;
            if (GUILayout.Button(isRapidAttack ? "Rapid Attack (Bow & Melee): <color=green>ON</color>" : "Rapid Attack (Bow & Melee): <color=red>OFF</color>")) isRapidAttack = !isRapidAttack;

            GUILayout.Space(10);
            GUILayout.Label("<b>ESP</b>");
            if (GUILayout.Button(isEsp ? "Creature ESP: <color=green>ON</color>" : "Creature ESP: <color=red>OFF</color>")) isEsp = !isEsp;
            if (GUILayout.Button("Select All ESP")) SetAllEsp(true);
            if (GUILayout.Button("Deselect All ESP")) SetAllEsp(false);
            GUILayout.Label("<b>Filter List:</b>");
            espScrollPos = GUILayout.BeginScrollView(espScrollPos, GUI.skin.box, GUILayout.Height(300));
            var keys = new List<string>(espFilters.Keys);
            keys.Sort();
            foreach (string name in keys) { bool toggled = espFilters[name]; bool newState = GUILayout.Toggle(toggled, name); if (newState != toggled) espFilters[name] = newState; }
            GUILayout.EndScrollView();
        }
        void PopulateEspList()
        {
            if (ZNetScene.instance == null) return;
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Character c = prefab.GetComponent<Character>();
                if (c != null && prefab.name != "Player") { if (!espFilters.ContainsKey(prefab.name)) { bool isEnemy = c.m_faction == Character.Faction.Undead || c.m_faction == Character.Faction.Demon || c.m_faction == Character.Faction.PlainsMonsters || c.m_faction == Character.Faction.ForestMonsters; espFilters.Add(prefab.name, isEnemy); } }
            }
            espListPopulated = true;
        }
        void SetAllEsp(bool state) { var keys = new List<string>(espFilters.Keys); foreach (string key in keys) espFilters[key] = state; }
        void DrawEsp()
        {
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c.IsPlayer() || c.IsDead()) continue;
                string name = c.gameObject.name.Replace("(Clone)", "");
                if (espFilters.ContainsKey(name) && espFilters[name] == true)
                {
                    Vector3 screenPos = Camera.main.WorldToScreenPoint(c.GetCenterPoint());
                    if (screenPos.z > 0) { float dist = Vector3.Distance(Player.m_localPlayer.transform.position, c.transform.position); GUI.color = Color.red; GUI.Label(new Rect(screenPos.x - 50, Screen.height - screenPos.y - 25, 100, 50), $"{name} [{dist:F0}m]"); }
                }
            }
            GUI.color = Color.white;
        }
        public Character GetBestTarget()
        {
            Character bestTarget = null; float closestAngle = aimFov; Vector3 camForward = GameCamera.instance.transform.forward; Vector3 camPos = GameCamera.instance.transform.position;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c.IsPlayer() || c.IsDead()) continue; if (c.m_faction == Character.Faction.Players) continue;
                Vector3 dirToTarget = (c.GetCenterPoint() - camPos).normalized; float angle = Vector3.Angle(camForward, dirToTarget); if (angle < closestAngle) { closestAngle = angle; bestTarget = c; }
            }
            return bestTarget;
        }
        public static Character GetClosestEnemy(Vector3 pos, float radius)
        {
            Character closest = null; float minDist = radius;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c.IsPlayer() || c.IsDead()) continue; if (c.m_faction == Character.Faction.Players) continue;
                float dist = Vector3.Distance(pos, c.GetCenterPoint()); if (dist < minDist) { minDist = dist; closest = c; }
            }
            return closest;
        }
        void LoadSkills() { if (Player.m_localPlayer == null) return; var skillsObject = Player.m_localPlayer.GetSkills(); var field = Traverse.Create(skillsObject).Field("m_skillData"); cachedSkills = field.GetValue<Dictionary<Skills.SkillType, Skills.Skill>>(); if (cachedSkills == null) cachedSkills = new Dictionary<Skills.SkillType, Skills.Skill>(); }
        void DrawSkillsInterface() { if (GUILayout.Button("MAX ALL SKILLS (100)")) { foreach (var kvp in cachedSkills) kvp.Value.m_level = 100f; } skillScrollPosition = GUILayout.BeginScrollView(skillScrollPosition, GUI.skin.box); foreach (var kvp in cachedSkills) { GUILayout.BeginHorizontal(); GUILayout.Label($"{kvp.Key} <color=yellow>({kvp.Value.m_level:F0})</color>", GUILayout.Width(180)); if (GUILayout.Button("+1", GUILayout.Width(40))) kvp.Value.m_level = Mathf.Clamp(kvp.Value.m_level + 1, 0, 100); if (GUILayout.Button("MAX", GUILayout.Width(50))) kvp.Value.m_level = 100; GUILayout.EndHorizontal(); } GUILayout.EndScrollView(); }
        private void SpawnObject(string name, int count) { Player player = Player.m_localPlayer; if (player == null || ZNetScene.instance == null) return; GameObject prefab = ZNetScene.instance.GetPrefab(name); if (!prefab) { MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "Invalid: " + name); return; } Vector3 spawnPos = player.transform.position + player.transform.forward * 2f + Vector3.up; Quaternion spawnRot = player.transform.rotation; ItemDrop itemDrop = prefab.GetComponent<ItemDrop>(); if (itemDrop != null) { GameObject spawned = Instantiate(prefab, spawnPos, spawnRot); spawned.GetComponent<ItemDrop>().m_itemData.m_stack = count; MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, $"Spawned {count}x {name}"); } else { for (int i = 0; i < count; i++) Instantiate(prefab, spawnPos, spawnRot); MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, $"Spawned {count} {name}(s)"); } }

        [HarmonyPatch(typeof(Player), "TakeInput")]
        public static class BlockInputPatch { static bool Prefix(Player __instance) { if (ValheimModMenu.context != null && ValheimModMenu.context.showMenu) { var traverse = Traverse.Create(__instance); traverse.Field("m_moveDir").SetValue(Vector3.zero); traverse.Field("m_lookDir").SetValue(Vector3.zero); return false; } return true; } }

        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        public static class BlockInventoryInteract
        {
            static bool Prefix()
            {
                if (ValheimModMenu.context != null && ValheimModMenu.context.showMenu && ValheimModMenu.context.IsMouseOverMenu())
                {
                    return false;
                }
                return true;
            }
        }

        // --- NEW: FIXED FAST PROCESS ---
        [HarmonyPatch(typeof(Smelter), "GetDeltaTime")]
        public static class FastSmeltPatch
        {
            static void Postfix(ref double __result)
            {
                if (ValheimModMenu.context.isFastProcess) __result *= 50.0;
            }
        }

        // FIXED: Replaced bad GetDeltaTime patch with this SlowUpdate patch for Fermenters
        [HarmonyPatch(typeof(Fermenter), "SlowUpdate")]
        public static class FastFermentPatch
        {
            static void Prefix(Fermenter __instance)
            {
                if (ValheimModMenu.context.isFastProcess)
                {
                    // Force duration to 10 seconds so it finishes immediately
                    Traverse.Create(__instance).Field("m_fermentationDuration").SetValue(10f);
                }
            }
        }

        [HarmonyPatch(typeof(CookingStation), "UpdateCooking")]
        public static class FastCookPatch
        {
            static void Prefix(CookingStation __instance)
            {
                if (ValheimModMenu.context.isFastProcess)
                {
                    // FIXED: Use Reflection to access private m_items list
                    var traverse = Traverse.Create(__instance);
                    System.Collections.IList items = traverse.Field("m_items").GetValue<System.Collections.IList>();

                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            // Modifying cookTime via reflection
                            var itemTraverse = Traverse.Create(item);
                            float current = itemTraverse.Field("m_cookTime").GetValue<float>();
                            itemTraverse.Field("m_cookTime").SetValue(current + (Time.deltaTime * 50f));
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(InventoryGrid), "CreateItemTooltip")]
        public static class TooltipPatch
        {
            static void Postfix(ItemDrop.ItemData item, GameObject tooltip)
            {
                try
                {
                    if (ValheimModMenu.context.isRecipeHelper && item != null && tooltip != null && ObjectDB.instance != null)
                    {
                        Recipe r = ObjectDB.instance.GetRecipe(item);
                        if (r != null)
                        {
                            Text textComp = tooltip.transform.Find("Text")?.GetComponent<Text>();
                            if (textComp != null)
                            {
                                string addedInfo = "\n\n<color=orange><b>-- RECIPE --</b></color>";
                                foreach (var res in r.m_resources)
                                {
                                    addedInfo += $"\n<color=yellow>{res.m_resItem.m_itemData.m_shared.m_name}</color>: <color=white>{res.m_amount}</color>";
                                }
                                if (r.m_craftingStation != null)
                                {
                                    addedInfo += $"\nStation: <color=cyan>{r.m_craftingStation.m_name}</color> (Lvl {r.m_minStationLevel})";
                                }
                                textComp.text += addedInfo;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(Player), "CheckCanRemovePiece")]
        public static class RemovePiecePatch { static bool Prefix(ref bool __result) { if (ValheimModMenu.context.isNoCostBuild) { __result = true; return false; } return true; } }

        [HarmonyPatch(typeof(Location), "IsInsideNoBuildLocation")]
        public static class NoBuildLocPatch { static bool Prefix(ref bool __result) { if (ValheimModMenu.context.isNoCostBuild) { __result = false; return false; } return true; } }

        [HarmonyPatch(typeof(PrivateArea), "CheckAccess")]
        public static class WardPatch { static bool Prefix(ref bool __result) { if (ValheimModMenu.context.isNoCostBuild) { __result = true; return false; } return true; } }

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        public static class GodModePatch { static bool Prefix(Character __instance) { if (__instance == Player.m_localPlayer && ValheimModMenu.context.isGodMode) return false; return true; } }

        [HarmonyPatch(typeof(Projectile), "Setup")]
        public static class MagicBulletPatch { static void Postfix(Projectile __instance, Vector3 velocity) { if (ValheimModMenu.context.isMagicBullet && Player.m_localPlayer != null) { Character target = ValheimModMenu.context.GetBestTarget(); if (target != null) { Vector3 direction = (target.GetCenterPoint() - __instance.transform.position).normalized; float speed = velocity.magnitude; Traverse.Create(__instance).Field("m_vel").SetValue(direction * speed); __instance.m_gravity = 0f; } } } }

        [HarmonyPatch(typeof(Projectile), "FixedUpdate")]
        public static class HomingArrowPatch
        {
            static void Postfix(Projectile __instance)
            {
                if (ValheimModMenu.context.isMagicBullet && Player.m_localPlayer != null)
                {
                    var traverse = Traverse.Create(__instance);
                    Vector3 velocity = traverse.Field("m_vel").GetValue<Vector3>();
                    Character target = ValheimModMenu.GetClosestEnemy(__instance.transform.position, 50f);

                    if (target != null)
                    {
                        Vector3 targetPos = target.GetCenterPoint();
                        Vector3 currentPos = __instance.transform.position;
                        Vector3 dirToTarget = (targetPos - currentPos).normalized;
                        float distToTarget = Vector3.Distance(currentPos, targetPos);

                        int layerMask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece");

                        if (Physics.Raycast(currentPos, dirToTarget, out RaycastHit hit, distToTarget, layerMask))
                        {
                            Vector3 avoidance = hit.normal + Vector3.up;
                            dirToTarget = Vector3.Lerp(dirToTarget, avoidance, 0.6f).normalized;
                        }

                        float speed = velocity.magnitude;
                        Vector3 newVelocity = Vector3.RotateTowards(velocity, dirToTarget * speed, 20f * Time.fixedDeltaTime, 0f);

                        traverse.Field("m_vel").SetValue(newVelocity);
                        __instance.transform.rotation = Quaternion.LookRotation(newVelocity);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(MineRock5), "RPC_Damage")]
        public static class InstaMinePatch1 { static void Prefix(MineRock5 __instance, HitData hit) { if (ValheimModMenu.context.isInstaMine && hit.GetAttacker() == Player.m_localPlayer) ModifyHit(hit); } }
        [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
        public static class InstaMinePatch2 { static void Prefix(TreeBase __instance, HitData hit) { if (ValheimModMenu.context.isInstaMine && hit.GetAttacker() == Player.m_localPlayer) ModifyHit(hit); } }
        [HarmonyPatch(typeof(TreeLog), "RPC_Damage")]
        public static class InstaMinePatch3 { static void Prefix(TreeLog __instance, HitData hit) { if (ValheimModMenu.context.isInstaMine && hit.GetAttacker() == Player.m_localPlayer) ModifyHit(hit); } }
        [HarmonyPatch(typeof(Destructible), "RPC_Damage")]
        public static class InstaMinePatch4 { static void Prefix(Destructible __instance, HitData hit) { if (ValheimModMenu.context.isInstaMine && hit.GetAttacker() == Player.m_localPlayer) ModifyHit(hit); } }
        static void ModifyHit(HitData hit) { hit.m_damage.m_damage = 99999f; hit.m_damage.m_pickaxe = 99999f; hit.m_damage.m_chop = 99999f; }
    }
}