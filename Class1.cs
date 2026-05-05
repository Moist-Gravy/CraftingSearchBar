using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CraftingSearchBar
{
    [BepInPlugin("com.MoistGravy.CraftingSearchBar", "Crafting Search Bar", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            _harmony = new Harmony("com.MoistGravy.CraftingSearchBar");
            _harmony.PatchAll();
            Logger.LogInfo("Crafting Search Bar loaded successfully!");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        /// <summary>Public wrapper around the protected BepInEx Logger.</summary>
        public void LogInfo(string message)
        {
            Logger.LogInfo(message);
        }
    }

    /// <summary>
    /// Manages the search bar UI and filtering state.
    /// Kept as a static helper so Harmony patches can access it.
    /// </summary>
    internal static class SearchManager
    {
        // ── UI References ──
        internal static GameObject SearchBarRoot;
        internal static InputField SearchInput;
        internal static Text PlaceholderText;
        internal static Text InputText;

        // ── State ──
        internal static string CurrentFilter = "";
        internal static bool IsInputFocused;

        /// <summary>
        /// Creates the search bar UI and parents it into the crafting panel.
        /// Called once from the InventoryGui.Awake postfix.
        /// </summary>
        internal static void CreateSearchBar(InventoryGui gui)
        {
            if (SearchBarRoot != null)
            {
                return; // already created
            }

            // ────────────────────────────────────────────
            // Find the crafting panel's recipe list area.
            // The hierarchy is:  InventoryGui → Crafting → RecipeList
            // We want to place the search bar above the recipe scroll view.
            // ────────────────────────────────────────────
            Transform craftingRoot = gui.transform.Find("root/Crafting");
            if (craftingRoot == null)
            {
                // Fallback: try alternate paths used by some versions
                craftingRoot = gui.transform.Find("root/crafting");
            }

            if (craftingRoot == null)
            {
                Log("Could not find Crafting panel in InventoryGui hierarchy.");
                return;
            }

            // Find the recipe list container to position relative to
            Transform recipeList = craftingRoot.Find("RecipeList");
            if (recipeList == null)
            {
                recipeList = craftingRoot.Find("recipeList");
            }

            if (recipeList == null)
            {
                // Try to find any ScrollRect as a fallback
                var scrollRect = craftingRoot.GetComponentInChildren<ScrollRect>(true);
                if (scrollRect != null)
                {
                    recipeList = scrollRect.transform;
                }
            }

            if (recipeList == null)
            {
                Log("Could not find RecipeList in crafting panel.");
                return;
            }

            // ────────────────────────────────────────────
            // Build the search bar
            // ────────────────────────────────────────────

            SearchBarRoot = new GameObject("CSB_SearchBar", typeof(RectTransform));
            SearchBarRoot.transform.SetParent(recipeList.parent, false);

            var rootRect = SearchBarRoot.GetComponent<RectTransform>();

            // Get the recipe list's RectTransform to position relative to it
            var recipeRect = recipeList.GetComponent<RectTransform>();
            if (recipeRect != null)
            {
                float searchBarHeight = 30f;
                float spacing = 5f;
                float totalOffset = searchBarHeight + spacing; // 35f

                // Configure Search Bar RectTransform
                rootRect.anchorMin = recipeRect.anchorMin; // (0, 1)
                rootRect.anchorMax = recipeRect.anchorMax; // (0, 1)
                rootRect.pivot = recipeRect.pivot; // (0, 1) Top-Left
                
                // Position where the recipe list originally was
                rootRect.anchoredPosition = new Vector2(recipeRect.anchoredPosition.x, recipeRect.anchoredPosition.y);
                rootRect.sizeDelta = new Vector2(recipeRect.sizeDelta.x, searchBarHeight);

                // Push the recipe list down and shrink it
                recipeRect.anchoredPosition = new Vector2(
                    recipeRect.anchoredPosition.x,
                    recipeRect.anchoredPosition.y - totalOffset
                );
                recipeRect.sizeDelta = new Vector2(
                    recipeRect.sizeDelta.x,
                    recipeRect.sizeDelta.y - totalOffset
                );
            }
            else
            {
                // Fallback sizing
                rootRect.anchorMin = new Vector2(0f, 1f);
                rootRect.anchorMax = new Vector2(0f, 1f);
                rootRect.pivot = new Vector2(0f, 1f);
                rootRect.anchoredPosition = new Vector2(10f, -100f);
                rootRect.sizeDelta = new Vector2(196f, 30f);
            }

            // ── Background Image ──
            var bgImage = SearchBarRoot.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.6f);

            // ── Border (optional visual polish) ──
            var borderGo = new GameObject("CSB_Border", typeof(RectTransform));
            borderGo.transform.SetParent(SearchBarRoot.transform, false);
            var borderRect = borderGo.GetComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.sizeDelta = Vector2.zero;
            borderRect.offsetMin = new Vector2(-1f, -1f);
            borderRect.offsetMax = new Vector2(1f, 1f);
            var borderImage = borderGo.AddComponent<Image>();
            borderImage.color = new Color(1f, 0.82f, 0.36f, 0.4f); // subtle gold border
            borderGo.transform.SetAsFirstSibling();

            // ── Input Text ──
            var textGo = new GameObject("CSB_Text", typeof(RectTransform));
            textGo.transform.SetParent(SearchBarRoot.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 2f);
            textRect.offsetMax = new Vector2(-8f, -2f);

            InputText = textGo.AddComponent<Text>();
            InputText.font = GetValheimFont();
            InputText.fontSize = 16;
            InputText.color = new Color(1f, 0.95f, 0.82f, 1f); // warm cream
            InputText.alignment = TextAnchor.MiddleLeft;
            InputText.horizontalOverflow = HorizontalWrapMode.Overflow;
            InputText.supportRichText = false;

            // ── Placeholder Text ──
            var placeholderGo = new GameObject("CSB_Placeholder", typeof(RectTransform));
            placeholderGo.transform.SetParent(SearchBarRoot.transform, false);
            var phRect = placeholderGo.GetComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = new Vector2(8f, 2f);
            phRect.offsetMax = new Vector2(-8f, -2f);

            PlaceholderText = placeholderGo.AddComponent<Text>();
            PlaceholderText.font = GetValheimFont();
            PlaceholderText.fontSize = 16;
            PlaceholderText.fontStyle = FontStyle.Italic;
            PlaceholderText.color = new Color(1f, 0.95f, 0.82f, 0.4f); // faded cream
            PlaceholderText.alignment = TextAnchor.MiddleLeft;
            PlaceholderText.horizontalOverflow = HorizontalWrapMode.Overflow;
            PlaceholderText.text = "Search recipes...";

            // ── InputField Component ──
            SearchInput = SearchBarRoot.AddComponent<InputField>();
            SearchInput.targetGraphic = bgImage;
            SearchInput.textComponent = InputText;
            SearchInput.placeholder = PlaceholderText;
            SearchInput.characterLimit = 50;
            SearchInput.contentType = InputField.ContentType.Standard;
            SearchInput.lineType = InputField.LineType.SingleLine;
            SearchInput.caretColor = new Color(1f, 0.82f, 0.36f, 1f); // gold caret
            SearchInput.selectionColor = new Color(1f, 0.82f, 0.36f, 0.25f);

            // ── Event Hooks ──
            SearchInput.onValueChanged.AddListener(OnSearchChanged);

            Log("Search bar created successfully.");
        }

        /// <summary>
        /// Try to find and use the same font Valheim uses for its UI.
        /// Falls back to Arial if not found.
        /// </summary>
        private static Font GetValheimFont()
        {
            // Try to find Valheim's "AveriaSerifLibre-Bold" or "Norse" font
            var allFonts = Resources.FindObjectsOfTypeAll<Font>();
            foreach (var f in allFonts)
            {
                if (f.name.IndexOf("Averia", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return f;
                }
            }

            foreach (var f in allFonts)
            {
                if (f.name.IndexOf("Norse", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return f;
                }
            }

            // Fallback
            return Font.CreateDynamicFontFromOSFont("Arial", 16);
        }

        /// <summary>
        /// Called when the search input text changes.
        /// </summary>
        private static void OnSearchChanged(string newText)
        {
            CurrentFilter = newText ?? "";

            if (InventoryGui.instance != null)
            {
                try
                {
                    // Directly apply the filter to the existing UI elements.
                    ApplyFilter(InventoryGui.instance);
                }
                catch (Exception ex)
                {
                    Log("Error applying filter: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Clears the search bar text and filter state.
        /// </summary>
        internal static void ClearSearch()
        {
            CurrentFilter = "";
            if (SearchInput != null)
            {
                SearchInput.text = "";
            }
        }

        /// <summary>
        /// Applies the current search filter to the recipe list elements.
        /// Called from the UpdateRecipeList postfix.
        /// </summary>
        internal static void ApplyFilter(InventoryGui gui)
        {
            // Get the recipe list root (the content container for recipe UI elements)
            var recipeListRootField = AccessTools.Field(typeof(InventoryGui), "m_recipeListRoot");
            Transform listRoot = null;
            if (recipeListRootField != null)
            {
                listRoot = recipeListRootField.GetValue(gui) as Transform;
            }

            if (listRoot == null)
            {
                return;
            }

            string filter = "";
            if (!string.IsNullOrEmpty(CurrentFilter))
            {
                filter = CurrentFilter.Trim().ToLowerInvariant();
            }

            // Determine the vertical spacing used by Valheim between recipe elements
            float elementSpacing = 34f; // fallback default
            var spaceField = AccessTools.Field(typeof(InventoryGui), "m_recipeListSpace");
            if (spaceField != null)
            {
                elementSpacing = (float)spaceField.GetValue(gui);
            }

            // Get the available recipes list to know exactly how many valid elements there are.
            // Valheim's UpdateRecipeList destroys old elements but they linger until the end of the frame.
            // By knowing the count, we only process the newly instantiated elements at the end of the list.
            var availableRecipesField = AccessTools.Field(typeof(InventoryGui), "m_availableRecipes");
            int validCount = 0;
            System.Collections.IList recipesList = null;
            if (availableRecipesField != null)
            {
                var recipes = availableRecipesField.GetValue(gui);
                recipesList = recipes as System.Collections.IList;
                if (recipesList != null)
                {
                    validCount = recipesList.Count;
                }
            }

            int startIndex = listRoot.childCount - validCount;
            if (startIndex < 0) startIndex = 0;

            float currentY = 0f;

            for (int i = startIndex; i < listRoot.childCount; i++)
            {
                var child = listRoot.GetChild(i);
                if (child == null || child.gameObject == null) continue;

                // Elements with "RecipeElement" in their name are the ones we want to filter
                if (!child.name.Contains("RecipeElement"))
                {
                    continue;
                }

                bool shouldShow = true;
                
                if (!string.IsNullOrEmpty(filter))
                {
                    string uiName = GetRecipeNameFromUI(child);
                    if (!string.IsNullOrEmpty(uiName))
                    {
                        string searchKeywords = uiName.ToLowerInvariant();
                        
                        // Try to append category keywords directly from the recipe data
                        if (recipesList != null)
                        {
                            int recipeIndex = i - startIndex;
                            if (recipeIndex >= 0 && recipeIndex < recipesList.Count)
                            {
                                try
                                {
                                    var kvp = recipesList[recipeIndex];
                                    Recipe recipe = kvp as Recipe;
                                    if (recipe == null)
                                    {
                                        var keyProp = kvp.GetType().GetProperty("Key");
                                        if (keyProp != null)
                                        {
                                            recipe = keyProp.GetValue(kvp, null) as Recipe;
                                        }
                                    }

                                    if (recipe?.m_item?.m_itemData?.m_shared != null)
                                    {
                                        string itemType = recipe.m_item.m_itemData.m_shared.m_itemType.ToString().ToLowerInvariant();
                                        
                                        if (itemType == "consumable") searchKeywords += " food eat meal";
                                        else if (itemType == "helmet") searchKeywords += " head hat armor armour helmet";
                                        else if (itemType == "chest") searchKeywords += " body shirt armor armour breastplate";
                                        else if (itemType == "legs") searchKeywords += " pants trousers armor armour leggings";
                                        else if (itemType == "shoulder") searchKeywords += " cape cloak armor armour back";
                                        else if (itemType == "shield") searchKeywords += " armor armour defend";
                                        else if (itemType == "ammo") searchKeywords += " arrow arrows bolt bolts ammo";
                                        else if (itemType.Contains("weapon") || itemType == "bow") searchKeywords += " weapon combat";
                                        else if (itemType == "tool") searchKeywords += " tool axe pickaxe hammer";
                                    }
                                }
                                catch
                                {
                                    // Ignore reflection errors for categories, just fallback to name
                                }
                            }
                        }

                        // Basic match check
                        shouldShow = searchKeywords.Contains(filter);
                        
                        // Handle simple plurals (e.g., searching "arrows" matches "arrow")
                        if (!shouldShow && filter.EndsWith("s") && filter.Length > 3)
                        {
                            string singular = filter.Substring(0, filter.Length - 1);
                            shouldShow = searchKeywords.Contains(singular);
                        }
                    }
                    else
                    {
                        // If we can't extract the name, default to showing it to be safe
                        shouldShow = true;
                    }
                }

                child.gameObject.SetActive(shouldShow);

                if (shouldShow)
                {
                    var childRect = child.GetComponent<RectTransform>();
                    if (childRect != null)
                    {
                        childRect.anchoredPosition = new Vector2(childRect.anchoredPosition.x, -currentY);
                    }
                    currentY += elementSpacing;
                }
            }

            // Adjust the scroll view content size using Valheim's minimum base size
            // This prevents the container from shrinking too much and pulling top-anchored elements downwards.
            var listRect = listRoot.GetComponent<RectTransform>();
            if (listRect != null)
            {
                float baseSize = 0f;
                var baseSizeField = AccessTools.Field(typeof(InventoryGui), "m_recipeListBaseSize");
                if (baseSizeField != null)
                {
                    baseSize = (float)baseSizeField.GetValue(gui);
                }
                listRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(baseSize, currentY));
            }
        }

        /// <summary>
        /// Robustly extracts the text from the UI element, supporting both standard Unity Text and TextMeshPro.
        /// </summary>
        private static string GetRecipeNameFromUI(Transform child)
        {
            Transform nameTrans = child.Find("name");
            if (nameTrans == null) return null;

            // Try standard Unity Text
            var textComp = nameTrans.GetComponent<Text>();
            if (textComp != null && !string.IsNullOrEmpty(textComp.text))
            {
                return textComp.text;
            }

            // Try any component that has a "text" property (like TextMeshProUGUI)
            foreach (var comp in nameTrans.GetComponents<Component>())
            {
                if (comp == null) continue;
                var prop = comp.GetType().GetProperty("text");
                if (prop != null && prop.PropertyType == typeof(string))
                {
                    string textVal = prop.GetValue(comp, null) as string;
                    if (!string.IsNullOrEmpty(textVal))
                    {
                        return textVal;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the localized display name for a recipe.
        /// </summary>
        private static string GetLocalizedName(Recipe recipe)
        {
            if (recipe == null || recipe.m_item == null)
            {
                return "";
            }

            string rawName = recipe.m_item.m_itemData?.m_shared?.m_name ?? "";

            // Use Valheim's localization to get the display name
            try
            {
                if (Localization.instance != null && !string.IsNullOrEmpty(rawName))
                {
                    return Localization.instance.Localize(rawName);
                }
            }
            catch
            {
                // Fall through to raw name
            }

            return rawName;
        }

        internal static void Log(string message)
        {
            if (Plugin.Instance != null)
            {
                Plugin.Instance.LogInfo("[CraftingSearchBar] " + message);
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    //  HARMONY PATCHES
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Patch InventoryGui.Awake to inject our search bar into the UI.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class InventoryGui_Awake_Patch
    {
        private static void Postfix(InventoryGui __instance)
        {
            try
            {
                SearchManager.CreateSearchBar(__instance);
            }
            catch (Exception ex)
            {
                SearchManager.Log("Error creating search bar: " + ex);
            }
        }
    }

    /// <summary>
    /// Patch UpdateRecipeList to apply our search filter after the game
    /// populates the recipe list.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
    internal static class InventoryGui_UpdateRecipeList_Patch
    {
        private static void Postfix(InventoryGui __instance)
        {
            try
            {
                SearchManager.ApplyFilter(__instance);
            }
            catch (Exception ex)
            {
                SearchManager.Log("Error applying filter: " + ex);
            }
        }
    }

    /// <summary>
    /// Patch InventoryGui.Hide to clear the search when the inventory closes.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "Hide")]
    internal static class InventoryGui_Hide_Patch
    {
        private static void Postfix()
        {
            SearchManager.ClearSearch();
            SearchManager.IsInputFocused = false;
        }
    }

    /// <summary>
    /// Patch PlayerController.TakeInput to block game input while typing
    /// in the search bar. This prevents WASD movement, ESC closing the menu, etc.
    /// </summary>
    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class PlayerController_TakeInput_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            if (SearchManager.SearchInput != null && SearchManager.SearchInput.isFocused)
            {
                __result = false;
                return false; // skip original method
            }

            return true; // run original
        }
    }

    /// <summary>
    /// Patch to prevent the menu from closing while we're typing.
    /// The game checks for ESC / Tab to close menus.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class InventoryGui_Update_Patch
    {
        private static void Postfix(InventoryGui __instance)
        {
            // Track focus state for the search input
            if (SearchManager.SearchInput != null)
            {
                SearchManager.IsInputFocused = SearchManager.SearchInput.isFocused;
            }
        }
    }

    /// <summary>
    /// Block ZInput while search bar is focused to prevent game hotkeys
    /// from firing (e.g., ESC, Tab, inventory toggle).
    /// </summary>
    [HarmonyPatch(typeof(ZInput), "GetButtonDown")]
    internal static class ZInput_GetButtonDown_Patch
    {
        private static bool Prefix(string name, ref bool __result)
        {
            if (SearchManager.SearchInput != null && SearchManager.SearchInput.isFocused)
            {
                // Allow only specific buttons while focused
                // Block everything else to prevent menu toggling
                __result = false;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Also block ZInput.GetButton while typing.
    /// </summary>
    [HarmonyPatch(typeof(ZInput), "GetButton")]
    internal static class ZInput_GetButton_Patch
    {
        private static bool Prefix(string name, ref bool __result)
        {
            if (SearchManager.SearchInput != null && SearchManager.SearchInput.isFocused)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}