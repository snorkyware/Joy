using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;

namespace Joy
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(VqolGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(TargetPortalGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Joy : BaseUnityPlugin
    {
        private const string
            Guid = "com.snorkyware.valheim.joy",
            Name = "Joy",
            Version = "0.0",
            VqolGuid = "com.maxbridgland.ValheimQualityOfLife",
            TargetPortalGuid = "org.bepinex.plugins.targetportal";
        
        private const BindingFlags PublicStaticBinding = BindingFlags.Static | BindingFlags.Public;

        private static readonly Harmony Harmony;

        static Joy() => Harmony = new Harmony(Guid + ".harmony");

        private static void Log(string msg) => Debug.Log("[" + Guid + "]" + msg);
        
        private void Awake()
        {
            EmotePatch.Awake();
            
            Harmony.PatchAll(typeof(FlyModePatch));
            Harmony.PatchAll(typeof(EmotePatch));
            Harmony.PatchAll(typeof(MinimapPatch));

            Log("Awake");
        }
        
        [HarmonyPatch]
        private class FlyModePatch
        {
            private static IFlyModeToggler _flyModeToggler;
            
            static FlyModePatch()
            {
                try
                {
                    if (Chainloader.PluginInfos.TryGetValue(VqolGuid, out PluginInfo info))
                    {
                        _flyModeToggler = new VqolFlyModeToggler(info);
                        return;
                    }
                }
                catch (Exception e)
                {
                    Log(e.ToString());
                }

                _flyModeToggler = new DefaultFlyModeToggler();
            }

            // Joy Button Combo to Enable Fly Mode
            [HarmonyPrefix, HarmonyPatch(typeof(Player)), HarmonyPatch("Update"), HarmonyPriority(Priority.VeryLow)]
            private static bool UpdatePrefix(Player __instance)
            {
                if (!__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner() || !__instance.TakeInput()) 
                    return true;
                
                if (ZInput.GetButton("JoyAltKeys") && !ZInput.GetButton("JoyRun") && ZInput.GetButtonUp("JoySit"))
                {
                    _flyModeToggler.ToggleFlyMode(ref __instance.m_debugFly, ref __instance.m_noPlacementCost);
                    return false;
                }
                
                // Don't actually sit down if in fly mode.
                if (__instance.m_debugFly && ZInput.GetButtonUp("JoySit")) return false;

                return true;
            }

            // Fly down with sit button.
            [HarmonyPostfix, HarmonyPatch(typeof(Character)), HarmonyPatch("UpdateDebugFly")]
            private static void UpdateDebugFlyPostfix(Character __instance, float dt)
            {
                if (!__instance.TakeInput() || !ZInput.GetButton("JoySit")) return;
                
                float speed = __instance.m_run ? Character.m_debugFlySpeed * 2.5f : Character.m_debugFlySpeed;
                Vector3 v = __instance.m_moveDir * speed;
                v.y = -speed;
                __instance.m_body.velocity = __instance.m_currentVel = Vector3.Lerp(__instance.m_currentVel, v, 0.5f);
            }
            
            private interface IFlyModeToggler
            {
                void ToggleFlyMode(ref bool debugFly, ref bool noPlacementCost);
            }
            
            // Toggle fly mode via Valheim Quality of Life plugin.
            private class VqolFlyModeToggler : IFlyModeToggler
            {
                private const string vqolMenu = "ValheimQualityOfLife.Menu";
                
                private readonly FieldInfo 
                    _enableFlyMode,
                    _enableUnlimitedStamina,
                    _enableNoPlacementCost,
                    _noFoodDegrade,
                    _enableNoCarryLimit;
                
                public VqolFlyModeToggler(PluginInfo info)
                {
                    Type? type = info.Instance.GetType().Assembly.GetType(vqolMenu, false);
                    FieldInfo?
                        enableFlyMode = null,
                        enableUnlimitedStamina = null,
                        enableNoPlacementCost = null,
                        noFoodDegrade = null,
                        enableNoCarryLimit = null;
                    
                    if (type != null)
                    {
                        enableFlyMode = type.GetField("enableFlyMode", PublicStaticBinding);
                        enableUnlimitedStamina = type.GetField("enableUnlimitedStamina", PublicStaticBinding);
                        enableNoPlacementCost = type.GetField("enableNoPlacementCost", PublicStaticBinding);
                        noFoodDegrade = type.GetField("noFoodDegrade", PublicStaticBinding);
                        enableNoCarryLimit = type.GetField("enableNoCarryLimit", PublicStaticBinding);
                    }

                    if (enableFlyMode == null || enableUnlimitedStamina == null || enableNoPlacementCost == null ||
                        noFoodDegrade == null || enableNoCarryLimit == null)
                        throw new Exception("failed to load all " + vqolMenu + " fields");

                    _enableFlyMode = enableFlyMode;
                    _enableUnlimitedStamina = enableUnlimitedStamina;
                    _enableNoPlacementCost = enableNoPlacementCost;
                    _noFoodDegrade = noFoodDegrade;
                    _enableNoCarryLimit = enableNoCarryLimit;
                }
                
                public void ToggleFlyMode(ref bool debugFly, ref bool noPlacementCost)
                {
                    bool on = !debugFly;
                    
                    _enableFlyMode.SetValue(null, on);
                    _enableUnlimitedStamina.SetValue(null, on);
                    _enableNoPlacementCost.SetValue(null, on);
                    _noFoodDegrade.SetValue(null, on);
                    _enableNoCarryLimit.SetValue(null, on);
                    
                    debugFly = on;
                    noPlacementCost = on;
                }
            }

            // Toggle fly mode without using Valheim Quality of Life plugin.
            private class DefaultFlyModeToggler : IFlyModeToggler
            {
                private static bool _enableFlyMode;
                
                public DefaultFlyModeToggler() => Harmony.PatchAll(typeof(DefaultFlyModeTogglerPlayerPatch));

                public void ToggleFlyMode(ref bool debugFly, ref bool noPlacementCost)
                {
                    bool on = !debugFly;
                    _enableFlyMode = on;
                    debugFly = on;
                    noPlacementCost = on;
                }
                
                [HarmonyPatch(typeof(Player))]
                private class DefaultFlyModeTogglerPlayerPatch
                {
                    [HarmonyPrefix, HarmonyPatch("UseStamina")]
                    private static void UseStaminaPrefix(ref float v)
                    {
                        if (_enableFlyMode) v = 0.0f;
                    }
                    
                    [HarmonyPostfix, HarmonyPatch("GetMaxCarryWeight")]
                    private static void GetMaxCarryWeightPostfix(ref float __result)
                    {
                        if (_enableFlyMode) __result = 99999f;
                    }
                    
                    [HarmonyPrefix, HarmonyPatch("UpdateFood")]
                    private static void UpdateFoodPrefix(float dt, bool forceUpdate)
                    {
                        if (!_enableFlyMode) return;
                        foreach (Player.Food food in Player.m_localPlayer.GetFoods())
                        {
                            food.m_health = food.m_item.m_shared.m_food;
                            food.m_stamina = food.m_item.m_shared.m_foodStamina;
                        }
                    }
                }
            }
        }

        // Joy emotes
        [HarmonyPatch]
        private class EmotePatch
        {
            private const string
                L1 = "EmoteL1",
                L2 = "EmoteL2",
                L3 = "EmoteL3",
                R1 = "EmoteR1",
                R2 = "EmoteR2",
                R3 = "EmoteR3",
                Up = "EmoteUp",
                Left = "EmoteLeft",
                Right = "EmoteRight",
                Down = "EmoteDown",
                Y = "EmoteY",
                X = "EmoteX",
                B = "EmoteB",
                A = "EmoteA";

            private static List<string> _buttons = new List<string>
            {
                L1,
                L2,
                R1,
                R2,
                Up,
                Left,
                Right,
                Down,
                Y,
                X,
                B,
                A,
                L3,
                R3
            };

            private enum EmoteState
            {
                None,
                First,
                Second
            }
    
            private static EmoteState _emoteState = EmoteState.None;

            private static Dictionary<EmoteState, Dictionary<string, string>> _emotes = 
                new()
                {
                    {EmoteState.First, new Dictionary<string, string>
                    {
                        { Up, "wave" },
                        { Left, "cheer" },
                        { Right, "thumbsup" },
                        { Down, "point" },
                        { Y, "blowkiss" },
                        { X, "bow" },
                        { B, "flex" },
                        { A, "laugh" },
                        { L3, "headbang" },
                        { R3, "dance" }
                    }},
                    {EmoteState.Second, new Dictionary<string, string>
                    {
                        { Up, "comehere" },
                        { Left, "roar" },
                        { Right, "kneel" },
                        { Down, "shrug" },
                        { Y, "challenge" },
                        { X, "nonono" },
                        { B, "cower" },
                        { A, "cry" },
                        { L3, "despair" },
                    }}
                };

            internal static void Awake()
            {
                string t = "Emote set 1";
                Localization.instance.AddWord("settings_" + L1.ToLower(), t);
                Localization.instance.AddWord("settings_" + L2.ToLower(), t);
                
                t = "Emote set 2";
                Localization.instance.AddWord("settings_" + R1.ToLower(), t);
                Localization.instance.AddWord("settings_" + R2.ToLower(), t);
                
                foreach (var kvp in _emotes[EmoteState.First])
                {
                    t = "Emote(" + kvp.Value;
                    if (_emotes[EmoteState.Second].ContainsKey(kvp.Key))
                        t += "/" + _emotes[EmoteState.Second][kvp.Key];
                    Localization.instance.AddWord("settings_" + kvp.Key.ToLower(), t + ")");
                }
            }

            private static bool Emote(Player __instance, string b1, string b2, EmoteState state)
            {
                bool
                    gb1 = ZInput.GetButton(b1),
                    gb2 = ZInput.GetButton(b2);

                if (gb1 && gb2)
                {
                    foreach (KeyValuePair<string, string> kvp in _emotes[state]) 
                        if (ZInput.GetButtonDown(kvp.Key))
                        {
                            _emoteState = EmoteState.None;
                            __instance.StartEmote(kvp.Value);
                            break;
                        }
                    _emoteState = state;
                    return true;
                }

                if ((gb1 || gb2) && _emoteState == state) return true;

                return false;
            }

            [HarmonyPostfix, HarmonyPatch(typeof(ZInput)), HarmonyPatch("Reset")]
            private static void ResetPostfix(ZInput __instance)
            {
                __instance.AddButton(L1, GamepadInput.BumperL);
                __instance.AddButton(L2, GamepadInput.TriggerL);
                __instance.AddButton(R1, GamepadInput.BumperR);
                __instance.AddButton(R2, GamepadInput.TriggerR);
                __instance.AddButton(Left, GamepadInput.DPadLeft);
                __instance.AddButton(Right, GamepadInput.DPadRight);
                __instance.AddButton(Up, GamepadInput.DPadUp);
                __instance.AddButton(Down, GamepadInput.DPadDown);
                __instance.AddButton(A, GamepadInput.FaceButtonA);
                __instance.AddButton(B, GamepadInput.FaceButtonB);
                __instance.AddButton(X, GamepadInput.FaceButtonX);
                __instance.AddButton(Y, GamepadInput.FaceButtonY);
                __instance.AddButton(L3, GamepadInput.StickLButton);
                __instance.AddButton(R3, GamepadInput.StickRButton);
            }

            [HarmonyPostfix, HarmonyPatch(typeof(ZInput)), HarmonyPatch("GetButton")]
            private static void GetButtonPostfix(ref bool __result, string name) => 
                ShouldCancel(ref __result, name);
            
            [HarmonyPostfix, HarmonyPatch(typeof(ZInput)), HarmonyPatch("GetButtonDown")]
            private static void GetButtonDownPostfix(ref bool __result, string name) => 
                ShouldCancel(ref __result, name);
            
            private static void ShouldCancel(ref bool __result, string name)
            {
                if (_emoteState != EmoteState.None && !_buttons.Contains(name))
                    __result = false;
            }

            [HarmonyPrefix, HarmonyPatch(typeof(Player)), HarmonyPatch("Update"), HarmonyPriority(Priority.VeryLow)]
            private static bool UpdatePrefix(Player __instance)
            {
                if (!__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner() || !__instance.TakeInput()) 
                    return true;
                if (Emote(__instance, L1, L2, EmoteState.First) || 
                    Emote(__instance, R1, R2, EmoteState.Second)) return false;
                _emoteState = EmoteState.None;
                return true;
            }
        }

        // Increased icon radius when using joypad. Add mouseover state for icons.
        [HarmonyPatch(typeof(Minimap))]
        private class MinimapPatch
        {
            private static IClosestPinGetter _closestPinGetter;
            
            static MinimapPatch()
            {
                try
                {
                    if (Chainloader.PluginInfos.TryGetValue(TargetPortalGuid, out PluginInfo info))
                    {
                        _closestPinGetter = new TargetPortalClosestPinGetter(info);
                        return;
                    }
                }
                catch (Exception e)
                {
                    Log(e.ToString());
                }

                _closestPinGetter = new DefaultClosestPinGetter();
            }
            
            private const float
                MouseRemoveRadius = 128f,
                JoyRemoveRadius = 1024f;
            
            private static readonly Color ColorGold = new (1f, 0.7f, 0.35f);

            [HarmonyPostfix, HarmonyPatch("UpdatePins")]
            private static void OverState(Minimap __instance)
            {
                if (__instance.m_mode != Minimap.MapMode.Large) return;

                __instance.m_removeRadius = ZInput.IsGamepadActive() ? JoyRemoveRadius : MouseRemoveRadius;
                
                Minimap.PinData? pin = _closestPinGetter.GetClosestPin();
                if (pin is null) return;
				
                pin.m_iconElement.color = ColorGold;
                if (pin.m_NamePinData != null && pin.m_NamePinData.PinNameText)
                    pin.m_NamePinData.PinNameText.color = ColorGold;
            }

            private interface IClosestPinGetter
            {
                Minimap.PinData? GetClosestPin();
            }

            private class TargetPortalClosestPinGetter : IClosestPinGetter
            {
                private readonly MethodInfo _method;
                public TargetPortalClosestPinGetter(PluginInfo info)
                {
                    Type? type = info.Instance.GetType().Assembly.GetType("TargetPortal.Map", false);
                    MethodInfo? method = null;
                    if (type != null) method = type.GetMethod("GetClosestPin", PublicStaticBinding);
                    if (method == null) throw new Exception(
                        "failed to load TargetPortal.Map.GetClosestPin method");
                    _method = method;
                }
                
                public Minimap.PinData? GetClosestPin() => (Minimap.PinData)_method.Invoke(null, null);
            }

            private class DefaultClosestPinGetter : IClosestPinGetter
            {
                public Minimap.PinData? GetClosestPin()
                {
                    Minimap mm = Minimap.instance;
                    Vector3 pos = ZInput.IsGamepadActive() 
                        ? mm.ScreenToWorldPoint(new Vector3(Screen.width / 2f, Screen.height / 2f))
                        : mm.ScreenToWorldPoint(Input.mousePosition);

                    return mm.GetClosestPin(pos, mm.m_removeRadius * (mm.m_largeZoom * 2f));
                }
            }
        }
    }
}
