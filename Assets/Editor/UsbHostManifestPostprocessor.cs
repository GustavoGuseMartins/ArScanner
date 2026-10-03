#if UNITY_EDITOR
using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace ArScanner.EditorTools
{
    public sealed class UsbHostManifestPostprocessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var document = new XmlDocument();
            document.Load(manifestPath);
            const string ns = "http://schemas.android.com/apk/res/android";
            XmlElement root = document.DocumentElement;
            foreach (XmlNode node in root.ChildNodes)
                if (node is XmlElement element && element.LocalName == "uses-feature" &&
                    element.GetAttribute("name", ns) == "android.hardware.usb.host") return;
            XmlElement feature = document.CreateElement("uses-feature");
            feature.SetAttribute("name", ns, "android.hardware.usb.host");
            feature.SetAttribute("required", ns, "false");
            root.AppendChild(feature);
            document.Save(manifestPath);
        }
    }
}
#endif
