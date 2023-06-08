using System;
using System.IO;
using UnityEngine;

namespace TASMod
{

    public class Customize : MonoBehaviour
    {
        public void Start()
        {
            if (File.Exists(path))
            {
                ReadConfig();
            }
            else
            {
                color = new Color32(255, 128, 0, 255);
                keyColor = Color.white;
            }
            Apply();

            curColor = "FFFF8000";
            curKeyColor = "FFFFFFFF";
            windowRect = new Rect(Screen.width - 210, 200, 200, 200);
        }

        public void OnGUI()
        {
            if (TASMod.customize)
            {
                windowRect = GUI.Window(202, windowRect, WindowFunction, "Customize");
            }
        }

        public void WindowFunction(int windowID)
        {
            GUI.Label(new Rect(10, 30, 50, 30), "Color: ");
            curColor = GUI.TextField(new Rect(60, 30, 80, 20), curColor);
            GUI.Label(new Rect(10, 60, 80, 30), "Key: ");
            curKeyColor = GUI.TextField(new Rect(60, 60, 80, 20), curKeyColor);
            Color c = color;
            Color keyC = keyColor;
            if (!string.IsNullOrWhiteSpace(curColor))
            {
                c = HexConverter.HexToColor(curColor);
                GUIStyle style = new GUIStyle();
                style.normal.textColor = c;
                GUI.Label(new Rect(150, 30, 20, 20), "■", style);
            }
            if (!string.IsNullOrWhiteSpace(curKeyColor))
            {
                keyC = HexConverter.HexToColor(curKeyColor);
                GUIStyle style = new GUIStyle();
                style.normal.textColor = keyC;
                GUI.Label(new Rect(150, 60, 20, 20), "■", style);
            }
            if (GUI.Button(new Rect(10, windowRect.height - 40, 90, 30), "Apply"))
            {
                color = c;
                keyColor = keyC;
                Apply();
            }
            if (GUI.Button(new Rect(100, windowRect.height - 40, 90, 30), "Save"))
            {
                WriteConfig();
            }

            GUI.DragWindow(new Rect(0, 0, Screen.width, Screen.height));
        }

        public void Apply()
        {
            TASMod.color = color;
            TASMod.keyColor = keyColor;
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBAFloat, false);
            texture.SetPixel(0, 0, TASMod.keyColor);
            texture.Apply();
            TASMod.styleKey.normal.background = texture;
        }

        public string ReadUInt(string s, out uint num)
        {
            int num2 = s.IndexOf(',');
            num = (uint)Convert.ToInt64(s.Substring(0, num2));
            return s.Substring(num2 + 1);
        }

        public string WriteUInt(uint num, string s)
        {
            return s + num.ToString() + ",";
        }

        public void ReadConfig()
        {
            string s = File.ReadAllText(path);
            s = ReadUInt(s, out uint num);
            color = new Color32((byte)(num / 16777216), (byte)(num / 65536 % 256), (byte)(num / 256 % 256), (byte)(num % 256));
            s = ReadUInt(s, out num);
            keyColor = new Color32((byte)(num / 16777216), (byte)(num / 65536 % 256), (byte)(num / 256 % 256), (byte)(num % 256));
        }

        public void WriteConfig()
        {
            string s = "";
            s = WriteUInt((uint)(color.r * 16777216 + color.g * 65536 + color.b * 256 + color.a), s);
            s = WriteUInt((uint)(keyColor.r * 16777216 + keyColor.g * 65536 + keyColor.b * 256 + keyColor.a), s);
            File.WriteAllText(path, s);
        }

        public readonly string path = "BepInEx/plugins/TASMod Config.txt";
        public Color32 color;
        public Color32 keyColor;
        public string curColor;
        public string curKeyColor;

        public Rect windowRect;
    }
}

