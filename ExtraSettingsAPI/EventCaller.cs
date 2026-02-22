using HarmonyLib;
using HMLLibrary;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using Debug = UnityEngine.Debug;

namespace _ExtraSettingsAPI
{
    public class EventCaller
    {
        public Harmony harmony;
        public Mod parent { get; }
        public Traverse modTraverse;
        Dictionary<EventTypes, Traverse> settingsEvents = new Dictionary<EventTypes, Traverse>();
        public Traverse<bool> APIBool;
        public EventCaller(Mod mod)
        {
            parent = mod;
            modTraverse = Traverse.Create(parent);
            var settingsField = modTraverse.Field("ExtraSettingsAPI_Settings");
            if (settingsField.FieldExists())
            {
                if (settingsField.GetValue() == null)
                    try
                    {
                        var fType = settingsField.GetValueType();
                        if (fType == typeof(Type))
                            throw new InvalidOperationException("Cannot create instance of class " + fType.FullName);
                        if (fType.IsAbstract)
                            throw new InvalidOperationException("Cannot create instance of abstract class " + fType.FullName);
                        if (fType.IsInterface)
                            throw new InvalidOperationException("Cannot create instance of interface class " + fType.FullName);
                        var c = fType.GetConstructors((BindingFlags)(-1)).FirstOrDefault(x => x.GetParameters().Length == 0);
                        if (c == null)
                            throw new MissingMethodException("No parameterless constructor found for class " + fType.FullName);
                        else
                            settingsField.SetValue(c.Invoke(new object[0]));
                    }
                    catch (Exception e)
                    {
                        ExtraSettingsAPI.Log($"Found settings field of mod {parent.modlistEntry.jsonmodinfo.name}'s main class but failed to create an instance for it. You may need to create the class instance yourself.\n{e}");
                    }
                if (settingsField.GetValue() != null)
                {
                    if (settingsField.GetValue() is Type t)
                        modTraverse = Traverse.Create(t);
                    else
                        modTraverse = Traverse.Create(settingsField.GetValue());
                }
            }
            foreach (KeyValuePair<EventTypes, string> pair in EventNames)
                if (pair.Key != EventTypes.Button && pair.Key != EventTypes.InputValueChange && pair.Key != EventTypes.InputCaretMove)
                    settingsEvents.Add(pair.Key, modTraverse.Method(pair.Value, new Type[] { }, new object[] { }));
            APIBool = modTraverse.Field<bool>("ExtraSettingsAPI_Loaded");
            modTraverse.Field<Traverse>("ExtraSettingsAPI_Traverse").Value = ExtraSettingsAPI.self;
            var patchedMethods = new HashSet<MethodInfo>();
            foreach (var modMethod in (modTraverse.GetValue() as Type ?? modTraverse.GetValue().GetType()).GetMethods(~BindingFlags.Default))
                if (modMethod.Name.StartsWith("ExtraSettingsAPI_") && !EventNames.ContainsValue(modMethod.Name))
                {
                    var matches = new List<MethodInfo>();
                    MethodInfo m1 = null;
                    var s = -1;
                    List<int> pars = null;
                    foreach (var m in typeof(ExtraSettingsAPI).GetMethods(~BindingFlags.Default))
                        if (m.Name.Equals(modMethod.Name.Remove(0, "ExtraSettingsAPI_".Length), StringComparison.InvariantCultureIgnoreCase))
                        {
                            matches.Add(m);
                            if (Transpiler.CheckPatchParameters(modMethod, m, out var l, out var skip) && (s == -1 || skip < s))
                            {
                                s = skip;
                                m1 = m;
                                pars = l;
                            }
                        }
                    if (matches.Count == 0)
                        ExtraSettingsAPI.LogWarning($"{parent.modlistEntry.jsonmodinfo.name} >> Could not find any methods matching the name of method {modMethod.DeclaringType.FullName}::{modMethod}. You may have misspelled the method name or not meant to implement the ExtraSettingsAPI here");
                    else if (m1 == null)
                        ExtraSettingsAPI.LogWarning($"{parent.modlistEntry.jsonmodinfo.name} >> Could not find suitable implementation for method {modMethod.DeclaringType.FullName}::{modMethod}. You may have misspelled the method name, not meant to implement the ExtraSettingsAPI here or used the wrong parameters. The following methods were found with the same name:" + matches.Join(y => "\n" + y.ReturnType?.FullName + " " + modMethod.Name + "(" + y.GetParameters().Skip(1).Join(x => x.ParameterType.FullName) + ")", ""));
                    else
                    {
                        try
                        {
                            Transpiler.newMethod = m1;
                            Transpiler.modClass = parent.GetType();
                            Transpiler.argumentPairs = pars;
                            GetHarmony().Patch(modMethod, transpiler: new HarmonyMethod(typeof(Transpiler), nameof(Transpiler.Transpile)));
                            patchedMethods.Add(modMethod);
                        }
                        catch (Exception e)
                        {
                            ExtraSettingsAPI.LogError($"An error occured while trying to implement the {modMethod.Name} method for the {parent.modlistEntry.jsonmodinfo.name} mod\n{e}");
                        }
                    }
                }
            Patch_ReplaceAPICalls.methodsToLookFor = patchedMethods;
            foreach (var m in Patch_ReplaceAPICalls.TargetMethods(parent.GetType().Assembly))
                GetHarmony().Patch(m, transpiler: new HarmonyMethod(typeof(Patch_ReplaceAPICalls), nameof(Patch_ReplaceAPICalls.Transpiler)));
        }

        Harmony GetHarmony() => harmony == null ? harmony = new Harmony("com.aidanamite.ExtraSettingsAPI-" + parent.GetType().FullName + "-" + DateTime.UtcNow.Ticks) : harmony;

        static class Transpiler
        {
            public static Type modClass;
            public static MethodInfo newMethod;
            public static List<int> argumentPairs;
            public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase method)
            {
                var bArgs = GetArguments(method);
                var nArgs = GetArguments(newMethod);
                CodeInstruction GetArg(int index) => (index >= 0 && index <= 3) ? new CodeInstruction(new[] { OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3 }[index]) : new CodeInstruction(OpCodes.Ldarg_S, index);
                var code = new List<CodeInstruction>();
                for (int i = 0; i < argumentPairs.Count; i++)
                {
                    if (argumentPairs[i] == -1)
                    {
                        code.AddRange(new[]
                            {
                            new CodeInstruction(OpCodes.Ldtoken,modClass),
                            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Type),"GetTypeFromHandle")),
                            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ExtraSettingsAPI),nameof(ExtraSettingsAPI.GetMod)))
                        });
                        if (CanCastTo(nArgs[i], typeof(EventCaller)))
                            code.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ExtraSettingsAPI), nameof(ExtraSettingsAPI.GetCallerFromMod))));
                    }
                    else if (argumentPairs[i] != -1 && CanCastTo(bArgs[argumentPairs[i]], nArgs[i]))
                        code.Add(GetArg(argumentPairs[i]));
                    else if (CanCastTo(nArgs[i], typeof(Mod)))
                        code.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ExtraSettingsAPI), nameof(ExtraSettingsAPI.GetCallerFromMod))));
                    else
                        code.AddRange(new[]
                        {
                            GetArg(argumentPairs[i]),
                            new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(EventCaller),nameof(parent)))
                        });
                }
                code.AddRange(new[]
                {
                    new CodeInstruction(OpCodes.Call, newMethod),
                    new CodeInstruction(OpCodes.Ret)
                });
                return code;
            }

            public static bool CheckPatchParameters(MethodInfo caller, MethodInfo target, out List<int> patchParams, out int skipped)
            {
                var callerParams = GetArguments(caller);
                var targetParams = GetArguments(target);
                patchParams = null;
                skipped = 0;
                if (CanCastTo(target.ReturnType, caller.ReturnType))
                {
                    var l = new List<int>();
                    int i = 0;
                    while (i < callerParams.Count || l.Count < targetParams.Count)
                        if (l.Count >= targetParams.Count)
                        {
                            skipped += callerParams.Count - i - 1;
                            break;
                        }
                        else if (i < callerParams.Count && CanCastTo(callerParams[i], targetParams[l.Count], true))
                        {
                            l.Add(i);
                            i++;
                        }
                        else
                        {
                            skipped++;
                            if (CanCastTo(targetParams[l.Count], typeof(Mod), true))
                                l.Add(-1);
                            else
                                i++;
                        }
                    if (l.Count >= targetParams.Count)
                    {
                        patchParams = l;
                        return true;
                    }
                    //Debug.LogWarning($"Argument mismatch fail\nCaller arguments: {callerParams.Join(x => x.FullName)}\nTarget arguments: {targetParams.Join(x => x.FullName)}\nSkipped: {skipped}\nArgument connections: {l.Join()}");
                }
                //else
                //Debug.LogWarning($"Return type fail");
                return false;
            }
            static bool CanCastTo(Type objType, Type targetType, bool includeCustomCast = false)
            {
                if (targetType.IsAssignableFrom(objType))
                    return true;
                if (includeCustomCast)
                {
                    var f1 = CanCastTo(objType, typeof(EventCaller)) || CanCastTo(objType, typeof(Mod));
                    var f2 = CanCastTo(targetType, typeof(EventCaller)) || CanCastTo(targetType, typeof(Mod));
                    if (f1 && f2)
                        return true;
                }
                return false;
            }

            static List<Type> GetArguments(MethodBase method)
            {
                var l = new List<Type>();
                if (!method.IsStatic)
                    l.Add(method.DeclaringType);
                foreach (var p in method.GetParameters())
                    l.Add(p.ParameterType);
                return l;
            }
        }

        public void Call(EventTypes eventType)
        {
            if (eventType == EventTypes.Button || 
                eventType == EventTypes.InputValueChange || 
                eventType == EventTypes.InputCaretMove)
                return;
            if (eventType == EventTypes.Create)
                ExtraSettingsAPI.generateSettings(parent);
            if (eventType == EventTypes.Open)
                ExtraSettingsAPI.checkSettingVisibility(parent);
            if (eventType == EventTypes.Load)
                APIBool.Value = true;
            if (eventType == EventTypes.Unload)
                APIBool.Value = false;
            if (settingsEvents[eventType].MethodExists())
                try
                {
                    settingsEvents[eventType].GetValue();
                }
                catch (Exception e)
                {
                    ExtraSettingsAPI.LogError($"An exception occured in the setting {eventType} event of the {parent.modlistEntry.jsonmodinfo.name} mod\n{e.CleanInvoke()}");
                }
        }

        public void ButtonPress(ModSetting_Button button)
        {
            modTraverse.Method(EventNames[EventTypes.Button], new Type[] { typeof(string) }, new object[] { button.name }).GetValue();
        }
        public void ButtonPress(ModSetting_MultiButton button, int index)
        {
            modTraverse.Method(EventNames[EventTypes.Button], new Type[] { typeof(string), typeof(int) }, new object[] { button.name, index }).GetValue();
        }
        
        public (string text, int caretPos) InputValueChange(ModSetting_Input input, string text, int caretPos)
        {
            try
            {
                var method = modTraverse.Method(EventNames[EventTypes.InputValueChange],
                    new[] { typeof(string), typeof(string), typeof(int) },
                    new object[] { input.name, text, caretPos });

                if (!method.MethodExists())
                    return (text, caretPos);
                var result = method.GetValue();
                if (result is ValueTuple<string, int> t)
                    return t;
                return (text, caretPos);
            } 
            catch (Exception e)
            {
                Debug.LogError(e);
                return (text, caretPos);
            }
        }

        public (int caret, int anchor) InputCaretMove(ModSetting_Input input, string text, int caretPos, int anchorPos)
        {
            try
            {
                var method = modTraverse.Method(EventNames[EventTypes.InputCaretMove],
                    new[] { typeof(string), typeof(string), typeof(int), typeof(int) },
                    new object[]{ input.name, text, caretPos, anchorPos });
                
                if (!method.MethodExists())
                    return (caretPos, anchorPos);
                var result = method.GetValue();
                if (result is ValueTuple<int, int> t)
                    return t;
                return (caretPos, anchorPos);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                return (caretPos, anchorPos);
            }
        }

        public string GetSliderText(ModSetting_Slider slider, float value)
        {
            try
            {
                var t = modTraverse.Method(EventNames[EventTypes.Slider], slider.name, value);
                if (!t.MethodExists())
                {
                    ExtraSettingsAPI.LogWarning($"{parent.name} does not contain an appropriate definition for {EventNames[EventTypes.Slider]}. Setting {slider.nameText} requires this because its display mode is {slider.valueType}");
                    return "{null}";
                }
                var r = t.GetValue();
                if (r is string s)
                    return s;
                if (r != null)
                    return r.ToString();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            return "{null}";
        }

        public bool GetSettingVisible(ModSetting setting)
        {
            try
            {
                var t = modTraverse.Method(EventNames[EventTypes.Access], setting.name, ExtraSettingsAPI.IsInWorld);
                if (!t.MethodExists())
                    t = modTraverse.Method(EventNames[EventTypes.Access], setting.name);
                if (!t.MethodExists())
                {
                    ExtraSettingsAPI.LogWarning($"{parent.name} does not contain an appropriate definition for {EventNames[EventTypes.Access]}. Setting {setting.nameText} requires this because its access mode is {setting.access}");
                    return false;
                }
                var r = t.GetValue();
                if (r is bool b)
                    return b;
                ExtraSettingsAPI.LogWarning($"Return value of {EventNames[EventTypes.Access]} must be a bool. Mod {parent.name} returned {r?.GetType().ToString() ?? "null"}");
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            return false;
        }

        public bool Equals(Mod obj)
        {
            if (!obj || GetType() != obj.GetType())
            {
                return false;
            }
            return parent == obj;
        }

        public enum EventTypes
        {
            Open,
            Close,
            Load,
            Unload,
            Button,
            Create,
            Slider,
            Access,
            WorldLoad,
            WorldExit,
            InputValueChange,
            InputCaretMove
        }
        public static Dictionary<EventTypes, string> EventNames = new Dictionary<EventTypes, string>
    {
        { EventTypes.Open, "ExtraSettingsAPI_SettingsOpen" },
        { EventTypes.Close, "ExtraSettingsAPI_SettingsClose" },
        { EventTypes.Load, "ExtraSettingsAPI_Load" },
        { EventTypes.Unload, "ExtraSettingsAPI_Unload" },
        { EventTypes.Button, "ExtraSettingsAPI_ButtonPress" },
        { EventTypes.Create, "ExtraSettingsAPI_SettingsCreate" },
        { EventTypes.Slider, "ExtraSettingsAPI_HandleSliderText" },
        { EventTypes.Access, "ExtraSettingsAPI_HandleSettingVisible" },
        { EventTypes.WorldLoad, "ExtraSettingsAPI_WorldLoad" },
        { EventTypes.WorldExit, "ExtraSettingsAPI_WorldUnload" },
        { EventTypes.InputValueChange, "ExtraSettingsAPI_InputValueChange" },
        { EventTypes.InputCaretMove, "ExtraSettingsAPI_InputCaretMove" }
    };
    }
}