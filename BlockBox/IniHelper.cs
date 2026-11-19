using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BlockBox.Helpers
{
    public class IniHelper
    {
        [DllImport("kernel32")]
        private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);

        [DllImport("kernel32")]
        private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        private string _filePath;

        public IniHelper(string filePath)
        {
            _filePath = filePath;
        }

        // 写入 INI
        public void WriteValue(string section, string key, string value)
        {
            WritePrivateProfileString(section, key, value, _filePath);
        }

        // 读取 INI
        public string ReadValue(string section, string key, string defaultValue = "")
        {
            StringBuilder temp = new StringBuilder(255);
            GetPrivateProfileString(section, key, defaultValue, temp, 255, _filePath);
            return temp.ToString();
        }

        // 读取布尔值
        public bool ReadBool(string section, string key, bool defaultValue = false)
        {
            string val = ReadValue(section, key, defaultValue.ToString());
            return val.ToLower() == "true" || val == "1";
        }

        // 读取颜色
        public string ReadColor(string section, string key, string defaultValue = "#000000")
        {
            return ReadValue(section, key, defaultValue);
        }
    }
}