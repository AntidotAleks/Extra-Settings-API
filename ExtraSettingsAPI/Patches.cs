using HarmonyLib;
using UnityEngine;
using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using PrivateAccess;
using System.IO;
using Sirenix.Utilities;
using UnityEngine.UI;


namespace _ExtraSettingsAPI
{
    [HarmonyPatch(typeof(Settings), "Open")]
    static class Patch_SettingsOpen
    {
        static void Postfix()
        {
            if (!ExtraSettingsAPI.settingsExist)
            {
                ExtraSettingsAPI.insertNewSettingsMenu();
                foreach (var caller in ExtraSettingsAPI.mods.Values)
                    caller.Call(EventCaller.EventTypes.Create);
            }
            foreach (var caller in ExtraSettingsAPI.mods.Values)
                caller.Call(EventCaller.EventTypes.Open);
        }
    }

    [HarmonyPatch(typeof(Settings), "Close")]
    static class Patch_SettingsClose
    {
        static void Prefix(Settings __instance, ref bool __state) => __state = __instance.IsOpen;
        static void Postfix(bool __state)
        {
            if (__state)
            {
                ExtraSettingsAPI.generateSaveJson();
                foreach (EventCaller caller in ExtraSettingsAPI.mods.Values)
                    caller.Call(EventCaller.EventTypes.Close);
            }
        }
    }

    [HarmonyPatch(typeof(MyInput), "IdentifierToKeybind")]
    static class Patch_KeybindsReset
    {
        static bool Prefix(string identifier, ref Keybind __result)
        {
            if (identifier != null && ModSetting_Keybind.MyKeys != null && ModSetting_Keybind.MyKeys.Count > 0 && ModSetting_Keybind.MyKeys.ContainsKey(identifier))
            {
                __result = ModSetting_Keybind.MyKeys[identifier];
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(KeybindInterface))]
    static class Patch_EnterExitKeybind
    {
        public static (KeybindInterface, bool) lastEntered;
        [HarmonyPatch("PointerEnter")]
        [HarmonyPrefix]
        static void Enter(KeybindInterface __instance, KeyConnection key, KeyConnection ___mainKey)
        {
            lastEntered = (__instance, key == ___mainKey);
        }
        [HarmonyPatch("PointerExit")]
        [HarmonyPrefix]
        static void Exit()
        {
            lastEntered = default;
        }
    }

    [HarmonyPatch(typeof(SaveAndLoad), "Save")]
    static class Patch_SaveGame
    {
        static void Postfix(string filename)
        {
            filename = Path.GetDirectoryName(filename);
            if (filename.EndsWith(SaveAndLoad.latestStringNameEnding))
                ExtraSettingsAPI.generateSaveJson(Path.Combine(filename, ExtraSettingsAPI.modInfo.name + ".json"));
        }
    }

    [HarmonyPatch(typeof(LoadGameBox), "Button_LoadGame")]
    static class Patch_LoadGame
    {
        static void Postfix() => ExtraSettingsAPI.loadLocal(true);
    }

    [HarmonyPatch(typeof(NewGameBox), "Button_CreateNewGame")]
    static class Patch_NewGame
    {
        static void Postfix() => ExtraSettingsAPI.loadLocal(false);
    }

    [HarmonyPatch(typeof(LoadSceneManager), "LoadScene")]
    static class Patch_UnloadWorld
    {
        static void Postfix(ref string sceneName)
        {
            if (sceneName == Raft_Network.MenuSceneName)
                ExtraSettingsAPI.OnExitWorld();
        }
    }

    [HarmonyPatch]
    static class Patch_RecieveWorldId
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var l = new List<MethodBase>();
            foreach (var m in typeof(Raft_Network).GetMethods(~BindingFlags.Default))
                if (m.HasMethodBody())
                {
                    try
                    {
                        foreach (var i in PatchProcessor.GetOriginalInstructions(m))
                            if (i.opcode == OpCodes.Stsfld && i.operand is FieldInfo f && f.Name == "WorldGuid")
                            {
                                l.Add(m);
                                break;
                            }
                    }
                    catch { }
                }
            return l;
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            code.InsertRange(
                code.FindIndex(x => x.opcode == OpCodes.Stsfld && x.operand is FieldInfo f && f.Name == "WorldGuid") + 1,
                new[]
                {
                new CodeInstruction(OpCodes.Ldc_I4_1),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ExtraSettingsAPI),nameof(ExtraSettingsAPI.loadLocal)))
                });
            return code;
        }
    }

    [HarmonyPatch(typeof(SaveAndLoad), "SaveUser")]
    static class Patch_SaveUser
    {
        static void Prefix()
        {
            if (!Raft_Network.IsHost)
                ExtraSettingsAPI.generateSaveJson(ExtraSettingsAPI.worldConfigPath);
        }
    }


    [HarmonyPatch(typeof(InputField))]
    static class Patch_OrderCaretSelect
    {
        private static MethodInfo CaretPosSet = AccessTools.Property(typeof(InputField), "caretPositionInternal").GetSetMethod(true);
        private static MethodInfo CaretSelPosSet = AccessTools.Property(typeof(InputField), "caretSelectPositionInternal").GetSetMethod(true);
        
        static IEnumerable<MethodBase> TargetMethods()
        {
            // Use AccessTools.Method or reflection to find the methods you want
            yield return AccessTools.Method(typeof(InputField), "MoveRight");
            yield return AccessTools.Method(typeof(InputField), "MoveLeft");
            yield return AccessTools.Method(typeof(InputField), "MoveDown", new []{typeof(bool), typeof(bool)});
            yield return AccessTools.Method(typeof(InputField), "MoveUp", new []{typeof(bool), typeof(bool)});
        }
        
        static IEnumerable Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (var i = 0; i < code.Count - 4; i++)
            {
                if (!(
                        code[i    ].opcode == OpCodes.Stloc_1 &&
                        code[i + 1].opcode == OpCodes.Call &&
                        code[i + 2].opcode == OpCodes.Ldloc_1 &&
                        code[i + 3].opcode == OpCodes.Call &&
                        
                        code[i + 1].operand is MethodInfo m1 && m1 == CaretSelPosSet &&
                        code[i + 3].operand is MethodInfo m2 && m2 == CaretPosSet
                    )) continue;
                code[i + 1].operand = CaretPosSet;
                code[i + 3].operand = CaretSelPosSet;
            }
            return code;
        }
    }
    
    [HarmonyPatch(typeof(InputField), "caretSelectPositionInternal", MethodType.Setter)]
    static class Patch_InputField_CallCaretMoveEvent
    {
        private static PropertyInfo caretPosInfo = AccessTools.Property(typeof(InputField), "caretPositionInternal");
        
        public static void Prefix(InputField __instance, ref int value)
        {
            if (!ModSetting_Input.InputCache.TryGetValue(__instance, out var input)) return;
            if (input.LastText != __instance.text) return;
            
            var mod = input.parent.parent;
            var tl = __instance.text.Length;
            value = value<0?0 : value>tl?tl : value;
            var result = ExtraSettingsAPI.mods[mod].InputCaretMove(input, __instance.text, value, (int)caretPosInfo.GetValue(__instance));
            
            caretPosInfo.SetValue(__instance, result.anchor);
            value = result.caret;
        }
    }

    static class Patch_ReplaceAPICalls
    {
        public static HashSet<MethodInfo> methodsToLookFor;
        public static IEnumerable<MethodBase> TargetMethods(Assembly assembly)
        {
            var l = new List<MethodBase>();
            foreach (var t in assembly.GetTypes())
                foreach (var m in t.GetMethods(~BindingFlags.Default))
                    try
                    {
                        foreach (var i in PatchProcessor.GetCurrentInstructions(m, out var iL))
                            if (i.opcode == OpCodes.Call && i.operand is MethodInfo method && methodsToLookFor.Contains(method))
                            {
                                l.Add(m);
                                break;
                            }
                    }
                    catch { }
            return l;
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            foreach (var i in code)
                if (i.opcode == OpCodes.Call && i.operand is MethodInfo method && methodsToLookFor.Contains(method) && !method.IsStatic)
                    i.opcode = OpCodes.Callvirt;
            return code;
        }
    }
}