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
        static ConfigEntry<float> conf_maxZoom;
        static ConfigEntry<bool> conf_reverseSaveOrder;


        static ManualLogSource logger;
        private void Awake()
        {
            logger = Logger;
            logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");


            conf_noDebtDefeat = Config.Bind("Settings", "NoDebtDefeat", false, "Disabling defeat at -5000 deben");
            conf_maxZoom = Config.Bind("General", "MaxZoom", 15f, new ConfigDescription("Set max zoom value. Default is 15.", new AcceptableValueRange<float>(5, 60)));
            conf_reverseSaveOrder = Config.Bind("General", "ReverseSaveOrder", true, "Reverse the order of save files in Load-menu");
            Harmony.CreateAndPatchAll(typeof(QoLFixes));
            logger.LogInfo($"QoLFixes applied!");
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

        // patch CameraManager.GetZoomMax to change maxZoom
        [HarmonyPatch(typeof(CameraManager), "GetZoomMax")]
        [HarmonyPostfix]
        static void CameraManagerGetZoomMaxPostfixPatch(ref float __result)
        {
            __result = conf_maxZoom.Value;
        }

        // patch MapGameplay.ChangeTreasury, which would show the defeat screen

        [HarmonyPatch(typeof(MapGameplay), "ChangeTreasury")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> MapGameplayChangeTreasuryTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            if (conf_noDebtDefeat.Value)
            {
                logger.LogInfo("changeTreasury: noDebtDefeat seems to be true");
                return new CodeMatcher(instructions)
                  .MatchForward(false,
                      new CodeMatch(OpCodes.Ldc_I4, -5000))
                  .SetOperandAndAdvance(-65535)
                  .InstructionEnumeration();
            }
            logger.LogInfo("changeTreasury: noDebtDefeat seems to be false");

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
                logger.LogInfo("IsUnderDebenBuildingLimit: noDebtDefeat seems to be true");

                return new CodeMatcher(instructions)
                .MatchForward(false,
                    new CodeMatch(OpCodes.Ldc_I4, -5000))
                .SetOperandAndAdvance(-65535)
                .InstructionEnumeration();
            }
            logger.LogInfo("IsUnderDebenBuildingLimit: noDebtDefeat seems to be false");

            return instructions;

        }

    }
}
