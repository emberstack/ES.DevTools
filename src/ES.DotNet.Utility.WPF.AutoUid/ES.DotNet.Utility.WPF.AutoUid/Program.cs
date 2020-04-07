using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Xml;

namespace ES.DotNet.Utility.WPF.AutoUid
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            if (args is null || args.Length == 0)
            {
                Console.WriteLine("Error: First argument must be a path.");
                return 1;
            }

            if (!Directory.Exists(args.First()))
            {
                Console.WriteLine($"Error: No directory found at '{args.First()}'");
                return 1;
            }

            var directory = new DirectoryInfo(args.First());
            var files = directory.GetFiles(@"*.xaml", SearchOption.AllDirectories).OrderBy(s => s.FullName).ToList();

            Console.WriteLine($"Found {files.Count} file(s)");


            var _ = typeof(ButtonBase);
            var primitiveDependencyObjects = (
                from domainAssembly in AppDomain.CurrentDomain.GetAssemblies()
                from assemblyType in domainAssembly.GetTypes()
                where typeof(DependencyObject).IsAssignableFrom(assemblyType)
                select assemblyType).ToArray();
            var primitiveNames = primitiveDependencyObjects.Select(s => s.Name).ToList();


            foreach (var file in files)
            {
                Console.WriteLine($"Processing: {file.FullName}");

                var originalContent = File.ReadAllText(file.FullName);
                var doc = new XmlDocument();
                doc.LoadXml(originalContent);


                //get all elements except those for WPF element attribute like 'Grid.Rows'
                var allElements = doc.SelectNodes("//*");
                if (allElements != null && doc.DocumentElement != null)
                {
                    if (!doc.DocumentElement.HasAttribute("xmlns:x"))
                        doc.DocumentElement.SetAttribute("xmlns:x", "http://schemas.microsoft.com/winfx/2006/xaml");
                    var xamlNameSpace = doc.DocumentElement.Attributes["xmlns:x"].Value;
                    foreach (XmlElement element in allElements)
                    {
                        Debug.Assert(element.ParentNode != null, "element.ParentNode != null");
                        if (element.Name.Contains(".") || element.ParentNode.Name.Contains(".")) continue;

                        //standardize @Name to @x:Name
                        if (element.Attributes["x:Name"] == null && element.Attributes["Name"] != null)
                        {
                            var xmlNameAttribute = doc.CreateAttribute("x", "Name", xamlNameSpace);
                            xmlNameAttribute.Value = element.Attributes["Name"].Value;
                            element.Attributes.Append(xmlNameAttribute);
                            element.RemoveAttribute("Name");
                        }

                        var uidAttribute = element.Attributes["x:Uid"];
                        if (uidAttribute != null && Guid.TryParseExact(uidAttribute.Value, "N", out var uniqueId))
                            element.Attributes.Remove(uidAttribute);
                        else
                            uniqueId = Guid.NewGuid();

                        var automationIdAttribute = element.Attributes["AutomationProperties.AutomationId"];
                        element.Attributes.Remove(automationIdAttribute);

                        if (!primitiveNames.Contains(element.LocalName)) continue;

                        var xmlUidAttribute = doc.CreateAttribute("x", "Uid", xamlNameSpace);
                        xmlUidAttribute.Value = uniqueId.ToString("N");
                        element.Attributes.Append(xmlUidAttribute);

                        var automationIdXmlAttribute = doc.CreateAttribute("AutomationProperties.AutomationId");
                        automationIdXmlAttribute.Value = uniqueId.ToString("N");
                        element.Attributes.Append(automationIdXmlAttribute);
                    }
                }

                var contentBuilder = new StringBuilder();
                XamlFormat(doc.DocumentElement, ref contentBuilder, string.Empty);
                var newContent = contentBuilder.ToString();
                File.WriteAllText(file.FullName, newContent);
            }

            return 0;
        }

        public static void XamlFormat(XmlElement xmlElement, ref StringBuilder sb, string indent)
        {
            var xmlNodeList = xmlElement.SelectNodes("@*[substring(.,1,1)='{']");
            var hasBindingAttributes = xmlNodeList != null && xmlNodeList.Count > 0;

            if ((xmlElement.ChildNodes.Count == 0 ||
                 xmlElement.ChildNodes.Count == 1 && xmlElement.ChildNodes[0].GetType() == typeof(XmlText)) &&
                !hasBindingAttributes)
            {
                //one liner element
                var attributeString = xmlElement.Attributes.Cast<XmlAttribute>().Aggregate(string.Empty,
                    (current, attribute) => $"{current}{attribute.Name}=\"{attribute.Value}\" ");
                if (string.IsNullOrEmpty(xmlElement.InnerText))
                    sb.AppendFormat("{0}<{1} {2}/>{3}", indent, xmlElement.Name, attributeString, Environment.NewLine);
                else
                    sb.AppendFormat("{0}<{1} {2}>{3}</{1}>{4}", indent, xmlElement.Name, attributeString,
                        xmlElement.InnerText, Environment.NewLine);
            }
            else
            {
                var elem = $"{indent}<{xmlElement.Name}";
                sb.Append(elem);

                var attributeIndent = string.Empty;
                foreach (XmlAttribute attribute in xmlElement.Attributes)
                    if (string.IsNullOrEmpty(attributeIndent))
                    {
                        sb.AppendFormat(" {0}=\"{1}\"", attribute.Name, attribute.Value);
                        attributeIndent = string.Empty.PadLeft(elem.Length, ' ');
                    }
                    else
                    {
                        sb.AppendFormat("{0}{1} {2}=\"{3}\"", Environment.NewLine, attributeIndent, attribute.Name,
                            attribute.Value);
                    }

                sb.Append($">{Environment.NewLine}");

                foreach (XmlNode subElem in xmlElement.ChildNodes)
                {
                    var subElemIndent = $"{indent}{string.Empty.PadLeft(4, ' ')}";
                    if (subElem.GetType() == typeof(XmlElement))
                        XamlFormat((XmlElement) subElem, ref sb, subElemIndent);
                    else if (subElem.GetType() == typeof(XmlComment))
                        sb.Append($"{subElemIndent}{subElem.OuterXml}{Environment.NewLine}");
                    else if (subElem.GetType() == typeof(XmlText))
                        sb.Append($"{subElemIndent}{subElem.OuterXml}{Environment.NewLine}");
                    else
                        throw new Exception("Unexpected element in XAML file: " + subElem.GetType());
                }

                sb.AppendFormat("{0}</{1}>{2}", indent, xmlElement.Name, Environment.NewLine);
            }
        }
    }
}