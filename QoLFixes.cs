using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
namespace QoLFixes
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class QoLFixes : BaseUnityPlugin
    {
        static ConfigEntry<bool> conf_noDebtDefeat;
        static ConfigEntry<bool> conf_reverseSaveOrder;
        static ConfigEntry<bool> conf_debugMenu;
        static ConfigEntry<int> conf_maxSpeed;

        static ConfigEntry<float> conf_maxZoom;
        static ConfigEntry<float> conf_zoomStep;
        static ConfigEntry<int> conf_keyboardSpeed;

        static ConfigEntry<bool> conf_DisableFullscreenNotifications;
        static ConfigEntry<bool> conf_DisableFullscreenNotificationsHighSpeed;
        static ConfigEntry<bool> conf_DisableNotificationsCompletely;
        static ConfigEntry<bool> conf_DisableRoadMissingNotification;

        static ManualLogSource logger;
        private void Awake()
        {
            logger = Logger;
            logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            // General settings
            conf_noDebtDefeat = Config.Bind("General", "NoDebtDefeat", false, "Disabling defeat at -5000 deben. THIS SETTING NEEDS A RESTART OF THE GAME");
            conf_reverseSaveOrder = Config.Bind("General", "ReverseSaveOrder", true, "Reverse the order of save files in Load-menu");
            conf_maxSpeed = Config.Bind("General", "MaxSpeed", 5, new ConfigDescription("Set the speed of the fastest speed setting.", new AcceptableValueRange<int>(5, 50)));
            conf_debugMenu = Config.Bind("General", "DebugMenu", false, "Enable the debug menu. NEED TO RELOAD SAVE FILE");

            // Camera settings
            conf_zoomStep = Config.Bind("Camera", "ZoomStep", 0.5f, new ConfigDescription("How fast the mouse wheel zoom is", new AcceptableValueRange<float>(0.5f, 3f)));
            conf_keyboardSpeed = Config.Bind("Camera", "KeyboardSpeed", 10, new ConfigDescription("How fast the mouse wheel zoom is", new AcceptableValueRange<int>(10, 50)));
            conf_maxZoom = Config.Bind("Camera", "MaxZoom", 8.5f, new ConfigDescription("Set max zoom value.", new AcceptableValueRange<float>(5, 60)));

            // Notification settings
            conf_DisableFullscreenNotifications = Config.Bind("Notifications","DisableFullscreenNotifications", false, "Disable fullscreen notifications");
            conf_DisableFullscreenNotificationsHighSpeed = Config.Bind("Notifications", "DisableFullscreenNotificationsHighSpeed", false, "Disable fullscreen notifications when speed is 5 or higher");
            conf_DisableNotificationsCompletely = Config.Bind("Notifications", "DisableNotificationsCompletely", false, "Disable notifications completely");
            conf_DisableRoadMissingNotification = Config.Bind("Notifications", "DisableRoadMissingNotification", false, "Disable No Road Access notifications");

            Harmony.CreateAndPatchAll(typeof(QoLFixes));
            logger.LogInfo($"QoLFixes applied!");
        }


        [HarmonyPatch(typeof(CameraManager), "Update")]
        [HarmonyPrefix]
        static void CameraManagerZoomStepPatch(ref float ____zoomStep, ref float ____keyboardSpeed)
        {
            ____keyboardSpeed = (float)conf_keyboardSpeed.Value;
            ____zoomStep = conf_zoomStep.Value;
        }

        // patch CameraManager.GetZoomMax to change maxZoom
        [HarmonyPatch(typeof(CameraManager), "GetZoomMax")]
        [HarmonyPostfix]
        static void CameraManagerGetZoomMaxPostfixPatch(ref float __result)
        {
            logger.LogInfo($"patching GetZoomMax to {conf_maxZoom.Value.ToString()}");
            __result = conf_maxZoom.Value;
        }
        // notification patches

        [HarmonyPatch(typeof(NotificationsUI), "ShowAndRegisterNotification")]
        [HarmonyPrefix]
        private static bool ShowAndRegisterNotification_Patch(NotificationContext context, Stack<NotificationContext> ____toDisplayFullscreen, Stack<NotificationContext> ____toDisplay)
        {
            Stack<NotificationContext> stack;
            TimeManager timemanager = TimeManager.Instance;

            if (conf_DisableNotificationsCompletely.Value && !(context.Type == NotificationType.Invasion))
                return false;

            if (timemanager != null && timemanager.SpeedFactor > 4f && conf_DisableFullscreenNotificationsHighSpeed.Value && !(context.Type == NotificationType.Invasion))
                context.ShowFullscreen = false;

            if (conf_DisableFullscreenNotifications.Value && !(context.Type == NotificationType.Invasion))
                context.ShowFullscreen = false;

            if (conf_DisableRoadMissingNotification.Value && context.SourceType == SourceType.RoadBlocked)
                return false;

            if (context.AsPopup || context.ShowFullscreen)
            {
                stack = ____toDisplayFullscreen;
            }
            else
            {
                stack = ____toDisplay;
            }
            if (stack.Count == 0 || stack.Peek().HasDifferentInfos(context))
            {
                stack.Push(context);
            }
            return false;
        }

        // enable debug menu

        [HarmonyPatch(typeof(UILeftBar), "Start")]
        [HarmonyPostfix]
        private static void UILeftBarStartPostfixPatch(ref LeftBarButton ____cheatCodesButton)
        {
            if (conf_debugMenu.Value)
            {
                ____cheatCodesButton.gameObject.SetActive(value: true);
                ____cheatCodesButton.Button.onClick.AddListener(delegate
                {
                    CheatManager.Instance.gameObject.SetActive(value: true);
                });
            }

        }

        // speed patch

        [HarmonyPatch(typeof(TimeManager), "SetGameSpeed")]
        [HarmonyPrefix]
        private static bool SetGameSpeed_Patch(TimeManager.GameSpeed newGameSpeed, ref TimeManager __instance, ref float ____currentSpeed, ref TimeManager.GameSpeed ___CurrentGameSpeed, bool force = false)
        {
            if (___CurrentGameSpeed == newGameSpeed && !force)
            {
                return false;
            }
            ___CurrentGameSpeed = newGameSpeed;
            switch (newGameSpeed)
            {
                case TimeManager.GameSpeed.Low:
                    Traverse.Create(__instance).Property("SpeedFactor").SetValue(0.5f);
                    break;
                case TimeManager.GameSpeed.Normal:
                    Traverse.Create(__instance).Property("SpeedFactor").SetValue(1f);
                    break;
                case TimeManager.GameSpeed.Fast:
                    Traverse.Create(__instance).Property("SpeedFactor").SetValue(1.5f);
                    break;
                case TimeManager.GameSpeed.VeryFast:
                    Traverse.Create(__instance).Property("SpeedFactor").SetValue((float)conf_maxSpeed.Value);
                    break;
            }
            ____currentSpeed = (float)Traverse.Create(__instance).Property("SpeedFactor").GetValue() * (__instance.IsPaused ? 0f : 1f);
            __instance._uiManager.DisplayGameSpeed(___CurrentGameSpeed);
            return false;
        }


        // patch FileManager.ScanForSaves to reverse save order
        [HarmonyPatch(typeof(FileManager), "ScanForSaves")]
        [HarmonyPrefix]
        private static bool ScanForSaves_Patch(ref IEnumerable<string> __result)
        {
            if (conf_reverseSaveOrder.Value)
            {
                if (!FileManager.IsFamilyValidOrTryToFix())
                {
                    __result = new string[0];
                }
                __result = from p in new DirectoryInfo(SerializationSettings.CurrentFamilySavePath).GetFiles("*." + SerializationSettings.SaveExtension)
                           orderby p.LastWriteTimeUtc descending
                           select Path.GetFileNameWithoutExtension(p.FullName);

                return false;
            }
            return true;
        }





        // patch MapGameplay.ChangeTreasury, which would show the defeat screen

        [HarmonyPatch(typeof(MapGameplay), "ChangeTreasury")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> MapGameplayChangeTreasuryTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            if (conf_noDebtDefeat.Value)
            {
                return new CodeMatcher(instructions)
                  .MatchForward(false,
                      new CodeMatch(OpCodes.Ldc_I4, -5000))
                  .SetOperandAndAdvance(-999999999)
                  .InstructionEnumeration();
            }

            return instructions;
        }


        // patch MapGamePlay.IsUnderDebenBuildingLimit, which would prevent placing buildings 

        [HarmonyPatch(typeof(MapGameplay))]
        [HarmonyPatch("IsUnderDebenBuildingLimit", MethodType.Getter)]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> MapGameplayIsUnderDebenBuildingLimitTranspiler(IEnumerable<CodeInstruction> instructions)
        {

            if (conf_noDebtDefeat.Value)
            {
                
                return new CodeMatcher(instructions)
                .MatchForward(false,
                    new CodeMatch(OpCodes.Ldc_I4, -5000))
                .SetOperandAndAdvance(-999999999)
                .InstructionEnumeration();
            }

            return instructions;

        }

    }
}
