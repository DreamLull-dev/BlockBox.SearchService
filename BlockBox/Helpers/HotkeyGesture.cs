using System;
using System.Collections.Generic;
using Windows.System;

namespace BlockBox.Helpers
{
    /// <summary>
    /// 快捷键手势的统一解析/格式化：设置页显示、INI 存储、菜单加速键共用同一套规则，
    /// 避免出现 “Ctrl+Number1” 这类显示 / 解析不一致的问题。
    /// </summary>
    internal static class HotkeyGesture
    {
        /// <summary>把 “Ctrl+1 / Ctrl+NumPad1 / Delete / F5” 这类文本解析成键和修饰键。</summary>
        public static bool TryParse(string text, out VirtualKey key, out VirtualKeyModifiers modifiers)
        {
            key = VirtualKey.None;
            modifiers = VirtualKeyModifiers.None;
            if (string.IsNullOrWhiteSpace(text)) return false;

            text = text.Replace('＋', '+').Replace('　', ' ');
            string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].ToUpperInvariant())
                {
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= VirtualKeyModifiers.Control;
                        break;
                    case "ALT":
                        modifiers |= VirtualKeyModifiers.Menu;
                        break;
                    case "SHIFT":
                        modifiers |= VirtualKeyModifiers.Shift;
                        break;
                    case "WIN":
                    case "WINDOWS":
                    case "META":
                        modifiers |= VirtualKeyModifiers.Windows;
                        break;
                    default:
                        return false;
                }
            }

            string keyName = parts[^1].ToUpperInvariant();
            if (keyName.Length == 1 && keyName[0] is >= 'A' and <= 'Z')
            {
                key = (VirtualKey)keyName[0];
            }
            else if (keyName.Length == 1 && keyName[0] is >= '0' and <= '9')
            {
                key = (VirtualKey)((int)VirtualKey.Number0 + (keyName[0] - '0'));
            }
            else if (TryParseNumpad(keyName, out key))
            {
                // 已解析
            }
            else if (keyName.StartsWith('F') && int.TryParse(keyName[1..], out int function)
                     && function is >= 1 and <= 24)
            {
                key = (VirtualKey)((int)VirtualKey.F1 + function - 1);
            }
            else
            {
                key = keyName switch
                {
                    "ESC" or "ESCAPE" => VirtualKey.Escape,
                    "BACKSPACE" or "BACK" => VirtualKey.Back,
                    "DEL" => VirtualKey.Delete,
                    "INS" => VirtualKey.Insert,
                    "RETURN" or "ENTER" => VirtualKey.Enter,
                    _ => VirtualKey.None
                };
                if (key == VirtualKey.None
                    && (!Enum.TryParse(keyName, ignoreCase: true, out key) || !IsSupportedKey(key)))
                {
                    return false;
                }
            }

            return key != VirtualKey.None;
        }

        private static bool TryParseNumpad(string keyName, out VirtualKey key)
        {
            key = VirtualKey.None;
            bool numpad;
            string digits;
            if (keyName.StartsWith("NUMPAD", StringComparison.Ordinal))
            {
                numpad = true;
                digits = keyName["NUMPAD".Length..];
            }
            else if (keyName.StartsWith("NUMBERPAD", StringComparison.Ordinal))
            {
                numpad = true;
                digits = keyName["NUMBERPAD".Length..];
            }
            else if (keyName.StartsWith("NUMBER", StringComparison.Ordinal))
            {
                // 旧版设置页把主键盘数字显示成 “Number1”，解析回主键盘数字。
                numpad = false;
                digits = keyName["NUMBER".Length..];
            }
            else
            {
                return false;
            }

            if (digits is not { Length: 1 } || digits[0] is < '0' or > '9')
                return false;

            int baseKey = numpad ? (int)VirtualKey.NumberPad0 : (int)VirtualKey.Number0;
            key = (VirtualKey)(baseKey + (digits[0] - '0'));
            return true;
        }

        /// <summary>是否是我们能格式化、也能被键盘加速键识别的按键。</summary>
        public static bool IsSupportedKey(VirtualKey key)
        {
            if (key is >= VirtualKey.A and <= VirtualKey.Z) return true;
            if (key is >= VirtualKey.Number0 and <= VirtualKey.Number9) return true;
            if (key is >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9) return true;
            if (key is >= VirtualKey.F1 and <= VirtualKey.F24) return true;

            return key is VirtualKey.Back or VirtualKey.Tab or VirtualKey.Enter or VirtualKey.Escape
                or VirtualKey.Space or VirtualKey.Delete or VirtualKey.Insert
                or VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown
                or VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right
                or VirtualKey.Add or VirtualKey.Subtract or VirtualKey.Multiply
                or VirtualKey.Divide or VirtualKey.Decimal;
        }

        /// <summary>设置页的合法手势规则：必须有修饰键；无修饰键时只允许功能键和导航键；禁止 Win 键。</summary>
        public static bool IsValidGesture(VirtualKey key, VirtualKeyModifiers modifiers)
        {
            if ((modifiers & VirtualKeyModifiers.Windows) != 0) return false;
            if (!IsSupportedKey(key)) return false;
            if (modifiers != VirtualKeyModifiers.None) return true;
            return key is >= VirtualKey.F1 and <= VirtualKey.F24
                or VirtualKey.Delete or VirtualKey.Insert
                or VirtualKey.Home or VirtualKey.End
                or VirtualKey.PageUp or VirtualKey.PageDown;
        }

        /// <summary>解析后是否带 Win 修饰键（用于清理旧配置里不允许的组合）。</summary>
        public static bool HasWindowsModifier(string text)
            => TryParse(text, out _, out VirtualKeyModifiers modifiers)
                && (modifiers & VirtualKeyModifiers.Windows) != 0;

        public static string FormatModifiers(VirtualKeyModifiers modifiers)
        {
            var parts = new List<string>(4);
            if ((modifiers & VirtualKeyModifiers.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & VirtualKeyModifiers.Menu) != 0) parts.Add("Alt");
            if ((modifiers & VirtualKeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((modifiers & VirtualKeyModifiers.Windows) != 0) parts.Add("Win");
            return string.Join("+", parts);
        }

        public static string Format(VirtualKey key, VirtualKeyModifiers modifiers)
        {
            string name = key switch
            {
                >= VirtualKey.Number0 and <= VirtualKey.Number9
                    => ((char)('0' + ((int)key - (int)VirtualKey.Number0))).ToString(),
                >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9
                    => "NumPad" + ((int)key - (int)VirtualKey.NumberPad0),
                VirtualKey.Back => "Backspace",
                VirtualKey.Escape => "Esc",
                VirtualKey.Space => "Space",
                VirtualKey.Enter => "Enter",
                VirtualKey.Tab => "Tab",
                VirtualKey.Delete => "Delete",
                VirtualKey.Insert => "Insert",
                VirtualKey.Home => "Home",
                VirtualKey.End => "End",
                VirtualKey.PageUp => "PageUp",
                VirtualKey.PageDown => "PageDown",
                VirtualKey.Up => "Up",
                VirtualKey.Down => "Down",
                VirtualKey.Left => "Left",
                VirtualKey.Right => "Right",
                _ => key.ToString()
            };

            string prefix = FormatModifiers(modifiers);
            return prefix.Length == 0 ? name : prefix + "+" + name;
        }

        /// <summary>把任意可识别的写法统一成规范文本；识别失败时原样返回。</summary>
        public static string Normalize(string text)
            => TryParse(text, out VirtualKey key, out VirtualKeyModifiers modifiers) && IsSupportedKey(key)
                ? Format(key, modifiers)
                : text;
    }
}
