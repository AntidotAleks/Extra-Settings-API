using ICSharpCode.SharpZipLib;
using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using HMLLibrary;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace _ExtraSettingsAPI
{
    public class ModSetting_Input : ModSetting
    {
        public InputField input = null;
        public Value<string> value;
        public int maxLength;
        public InputField.ContentType contentType;
        public ModSetting_Input(JObject source, ModSettingContainer parent) : base(source, parent)
        {
            if (source.TryGetValue<JValue>("mode", out var modeField, JTokenType.String))
                Enum.TryParse(modeField.Value.EnsureType<string>(), true, out contentType);
            TrySetupMember(source);
            string defaultValue = null;
            if (source.TryGetValue<JValue>("default", out var defaultField, JTokenType.String))
                defaultValue = defaultField.Value.EnsureType<string>();
            else if (TryGetMemberValue(out var mem))
                defaultValue = mem?.ToString();
            if (defaultValue == null)
                defaultValue = string.Empty;
            if (!source.TryGetValue<JValue>("max", out var maxField) || !maxField.Value.TryConvert(out maxLength))
                maxLength = 0;
            if (maxLength > 0 && defaultValue.Length > maxLength)
                defaultValue = defaultValue.Remove(maxLength);
            value = NewValue(defaultValue);
            LoadSettings();
        }

        protected override bool IsMemberTypeValid(Type type)
        {
            if (type == typeof(string))
                return true;
            if ((contentType == InputField.ContentType.IntegerNumber || contentType == InputField.ContentType.Pin) && type.IsNumber())
                return true;
            if (contentType == InputField.ContentType.DecimalNumber && (type == typeof(float) || type == typeof(double)))
                return true;
            if (contentType == InputField.ContentType.Alphanumeric && type.IsNumber(false))
                return true;
            return false;
        }

        protected override bool ConvertValueForMember(object value, Type targetType, out object result)
        {
            if (contentType != InputField.ContentType.Alphanumeric)
                return base.ConvertValueForMember(value, targetType, out result);
            if (value is string str && ulong.TryParse(str, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var l))
                return base.ConvertValueForMember(l, targetType, out result);
            result = null;
            return false;

        }

        public override string GetTooltip() => noSave ? base.GetTooltip() : JoinParagraphs(base.GetTooltip(), $"Allowed Values: {contentType.ToString().CamelToWords()}\nMax Length: {(maxLength > 0 ? maxLength.ToString() : "no limit")}\nDefault Value: {value.Default}{(ExtraSettingsAPI.IsInWorld && save.IsSplit() ? $"\nGlobal Value: {value[false]}" : "")}");

        protected override bool DoRealtimeMemberCheck()
        {
            if (!TryGetMemberValue(out var mem))
                return false;
            object curr = value.current;
            if (TryEnsureValueForMember(ref curr, out _) && !Equals(mem, curr))
            {
                SetValue(mem?.ToString() ?? "", ExtraSettingsAPI.IsInWorld, SetFlags.All ^ SetFlags.Member);
                return true;
            }
            return false;
        }

        public string LastText = "";
        public static readonly ConditionalWeakTable<InputField, ModSetting_Input> InputCache = new ConditionalWeakTable<InputField, ModSetting_Input>();

        public override void SetGameObject(GameObject go)
        {
            base.SetGameObject(go);
            input = control.GetComponentInChildren<InputField>(true);
            input.characterLimit = maxLength > 0 ? maxLength : int.MaxValue;
            input.contentType = contentType;
            input.text = LastText = value.current;
            input.onEndEdit.AddListener(t => SetValue(t, ExtraSettingsAPI.IsInWorld, SetFlags.All ^ SetFlags.Control));
            
            if (contentType != InputField.ContentType.Custom) return;
            var eventCaller = ExtraSettingsAPI.mods[parent.parent];
            input.onValueChanged.AddListener(t =>
            {
                var result = eventCaller.InputValueChange(this, t, input.caretPosition);
                var listener = input.onValueChanged;
                input.onValueChanged = new InputField.OnChangeEvent();
                input.text = LastText = result.text;
                input.caretPosition = result.caretPos;
                input.selectionFocusPosition = result.caretPos;
                input.onValueChanged = listener;
            });
            InputCache.Add(input, this);
        }

        public void SetValue(string newValue, bool local, SetFlags flags = SetFlags.All)
        {
            flags = FilterFlags(flags, local);
            if (maxLength > 0 && newValue?.Length > maxLength)
                newValue = newValue.Remove(maxLength);
            if (newValue == null)
                newValue = "";
            if (flags.HasFlag(SetFlags.Storage))
                value[local] = newValue;
            if (flags.HasFlag(SetFlags.Control) && input)
                input.SetTextWithoutNotify(newValue);
            if (flags.HasFlag(SetFlags.Member))
                SetMemberValue(newValue);
        }

        public override void Create()
        {
            SetGameObject(Object.Instantiate(ExtraSettingsAPI.inputPrefab));
        }

        public override void Destroy()
        {
            base.Destroy();
            input = null;
        }

        protected override bool ShouldTryGenerateSave(bool local) => value.ShouldSave(local);
        public override JToken GenerateSaveJson(bool local) => value[local];
        public override void LoadSettings(bool local)
        {
            JToken saved = parent.GetSavedSettings(this, local);
            if (saved is JValue val)
                SetValue(val.Value?.EnsureType<string>(), local);
            else
                ResetValue(local);
        }

        public override void ResetValue(bool local)
        {
            value.Reset(local);
            SetValue(value.current, local, SetFlags.All ^ SetFlags.Storage);
        }

        public override string CurrentValue() => value.current;
        public override bool TrySetValue(string str)
        {
            if (str.Length > maxLength)
                return false;
            if (contentType == InputField.ContentType.Alphanumeric)
            {
                foreach (var c in str)
                    if (!char.IsLetterOrDigit(c))
                        return false;
            }
            else if (contentType == InputField.ContentType.DecimalNumber)
            {
                var dec = false;
                var exp = false;
                var min = false;
                var num = false;
                foreach (var c in str)
                    if (char.IsDigit(c))
                    {
                        num = true;
                        min = true;
                    }
                    else if (!exp && !dec && c == '.')
                    {
                        dec = true;
                        min = true;
                    }
                    else if (!exp && num && c == 'e')
                    {
                        exp = true;
                        min = false;
                        num = false;
                    }
                    else if (!min && c == '-')
                        min = true;
                    else
                        return false;
                return num;
            }
            else if (contentType == InputField.ContentType.IntegerNumber)
            {
                foreach (var c in str)
                    if (!char.IsDigit(c))
                        return false;
            }
            else if (contentType == InputField.ContentType.EmailAddress)
            {
                var oth = false;
                var at = false;
                foreach (var c in str)
                    if ("!#$%&'*+-/=?^_`{|}~ \n".IndexOf(c) != -1)
                        return false;
                    else if (c == '.')
                    {
                        if (!oth)
                            return false;
                        oth = false;
                    }
                    else if (c == '@')
                    {
                        if (!oth || at)
                            return false;
                        oth = false;
                        at = true;
                    }
                    else
                        oth = true;
                return oth;
            }
            else if (contentType == InputField.ContentType.Name)
            {
                var spa = true;
                foreach (var c in str)
                    if (!spa && c == ' ')
                        spa = true;
                    else if (!char.IsLetter(c) && c != '\'' && c != '-')
                        return false;
                    else
                        spa = false;
            }
            SetValue(str, ExtraSettingsAPI.IsInWorld);
            return true;
        }
        public override string[] PossibleValues() => new[] { $"any {contentType.ToString().CamelToWords().ToLowerInvariant()} text" };
        public override string DisplayType() => contentType.ToString().CamelToWords() + " " + base.DisplayType();

        public override void OnExitWorld()
        {
            if (save.IsSplit())
                SetValue(value.current, false, SetFlags.All ^ SetFlags.Storage);
        }
    }
}