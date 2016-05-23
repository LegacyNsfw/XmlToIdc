using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace NSFW.XmlToIdc
{
    class Program
    {
        private static HashSet<string> names = new HashSet<string>();

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Usage();
                return;
            }

            if (CategoryIs(args, "tables"))
            {
                if (args.Length != 2)
                {
                    UsageTables();
                }

                DefineTables(args[1]);
            }
            else if (CategoryIs(args, "stdparam"))
            {
                if (args.Length != 3)
                {
                    UsageStdParam();
                }

                DefineStandardParameters(args[1], args[2]);
            }
            else if (CategoryIs(args, "extparam"))
            {
                if (args.Length != 2)
                {
                    UsageExtParam();
                }

                DefineExtendedParameters(args[1]);
            }
        }

        #region DefineXxxx functions

        private static void DefineTables(string calId)
        {
            if (!File.Exists("ecu_defs.xml"))
            {
                Console.Write("Error: ecu_defs.xml must be in the current directory.");
                return;
            }
            calId = calId.ToUpper();

            WriteHeader("Tables_" + calId, "Table definitions for " + calId);
            WriteTableNames(calId);
            WriteFooter();
        }

        private static void DefineStandardParameters(string calId, string ssmBaseString)
        {
            if (!File.Exists("logger.xml"))
            {
                Console.Write("Error: logger.xml must be in the current directory.");
                return;
            }

            if (!File.Exists("logger.dtd"))
            {
                Console.Write("Error: logger.dtd must be in the current directory.");
                return;
            }

            calId = calId.ToUpper();
            ssmBaseString = ssmBaseString.ToUpper();
            uint ssmBase = uint.Parse(ssmBaseString, System.Globalization.NumberStyles.HexNumber);

            WriteHeader("StdParams_" + calId, "Standard parameter definitions for " + calId + " with SSM read vector base " + ssmBaseString);
            WriteStandardParameters(calId, ssmBase);
            WriteFooter();
        }

        private static void DefineExtendedParameters(string ecuId)
        {
            if (!File.Exists("logger.xml"))
            {
                Console.Write("Error: logger.xml must be in the current directory.");
                return;
            }

            if (!File.Exists("logger.dtd"))
            {
                Console.Write("Error: logger.dtd must be in the current directory.");
                return;
            }

            ecuId = ecuId.ToUpper();

            WriteHeader("ExtParams_" + ecuId, "Extended parameter definitions for " + ecuId);
            WriteExtendedParameters(ecuId);
            WriteFooter();
        }

        #endregion

        private static string WriteTableNames(string xmlId)
        {
            Console.WriteLine("auto referenceAddress;");

            string ecuid = null;
            using (Stream stream = File.OpenRead("ecu_defs.xml"))
            {
                XPathDocument doc = new XPathDocument(stream);
                XPathNavigator nav = doc.CreateNavigator();
                string path = "/roms/rom/romid[xmlid='" + xmlId + "']";
                XPathNodeIterator iter = nav.Select(path);
                iter.MoveNext();
                nav = iter.Current;
                nav.MoveToChild(XPathNodeType.Element);
                
                while (nav.MoveToNext())
                {
                    if (nav.Name == "ecuid")
                    {
                        ecuid = nav.InnerXml;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(ecuid))
                {
                    Console.WriteLine("Could not find definition for " + xmlId);
                    return null;
                }
                
                nav.MoveToParent();
                while (nav.MoveToNext())
                {
                    if (nav.Name == "table")
                    {
                        Console.WriteLine();

                        string name = nav.GetAttribute("name", "");
                        string storageAddress = nav.GetAttribute("storageaddress", "");

                        name = ConvertName(name);
                        MakeName(storageAddress, name);

                        List<string> axes = new List<string>();
                        if (nav.HasChildren)
                        {
                            nav.MoveToChild(XPathNodeType.Element);

                            do
                            {
                                string axis = nav.GetAttribute("type", "");
                                axes.Add(axis);
                                string axisAddress = nav.GetAttribute("storageaddress", "");

                                axis = ConvertName(name + "_" + axis);
                                MakeName(axisAddress, axis);
                            } while (nav.MoveToNext());

                            if (axes.Count == 2 &&
                                ((axes[0] == "Y Axis" &&
                                axes[1] == "X Axis") ||
                                ((axes[0] == "X Axis" &&
                                axes[1] == "Y Axis"))))
                            {
                                Console.WriteLine("referenceAddress = DfirstB(" + storageAddress + ");");
                                Console.WriteLine("if (referenceAddress > 0)");
                                Console.WriteLine("{");
                                Console.WriteLine("    referenceAddress = referenceAddress - 12;");
                                string tableName = ConvertName("Table" + name);
                                string command = string.Format("    MakeNameEx(referenceAddress, \"{0}\", SN_CHECK);", tableName);
                                Console.WriteLine(command);
                                Console.WriteLine("}");
                                Console.WriteLine("else");
                                Console.WriteLine("{");
                                Console.WriteLine("    Message(\"No reference to " + name + "\\n\");");
                                Console.WriteLine("}");
                            }
                            else if (axes.Count == 1 &&
                                axes[0] == "Y Axis")
                            {
                                Console.WriteLine("referenceAddress = DfirstB(" + storageAddress + ");");
                                Console.WriteLine("if (referenceAddress > 0)");
                                Console.WriteLine("{");
                                Console.WriteLine("    referenceAddress = referenceAddress - 8;");
                                string tableName = ConvertName("Table" + name);
                                string command = string.Format("    MakeNameEx(referenceAddress, \"{0}\", SN_CHECK);", tableName);
                                Console.WriteLine(command);
                                Console.WriteLine("}");
                                Console.WriteLine("else");
                                Console.WriteLine("{");
                                Console.WriteLine("    Message(\"No reference to " + name + "\\n\");");
                                Console.WriteLine("}");
                            }

                            nav.MoveToParent();
                        }
                    }
                }                                
            }

            return ecuid;
        }

        private static void WriteStandardParameters(string ecuid, uint ssmBase)
        {
            // Can this really go inside the function definition?
            Console.WriteLine("auto addr;");
            Console.WriteLine("");

            using (Stream stream = File.OpenRead("logger.xml"))
            {
                XPathDocument doc = new XPathDocument(stream);
                XPathNavigator nav = doc.CreateNavigator();
                string path = "/logger/protocols/protocol[@id='SSM']/parameters/parameter";
                XPathNodeIterator iter = nav.Select(path);
                while (iter.MoveNext())
                {
                    XPathNavigator navigator = iter.Current;
                    string name = navigator.GetAttribute("name", "");
                    string pointerName = ConvertName("PtrSsmGet" + name);
                    string functionName = ConvertName("SsmGet" + name);

                    if (!navigator.MoveToChild("address", ""))
                    {
                        break;
                    }

                    string addressString = iter.Current.InnerXml;
                    addressString = addressString.Substring(2);

                    uint address = uint.Parse(addressString, System.Globalization.NumberStyles.HexNumber);
                    address = address * 4;
                    address = address + ssmBase;
                    addressString = "0x" + address.ToString("X8");

                    MakeName(addressString, pointerName);

                    string getAddress = string.Format("addr = Dword({0});", addressString);
                    Console.WriteLine(getAddress);
                    MakeName("addr", functionName);
                    Console.WriteLine();
                }
            }
        }

        private static void WriteExtendedParameters(string ecuid)
        {
            using (Stream stream = File.OpenRead("logger.xml"))
            {
                XPathDocument doc = new XPathDocument(stream);
                XPathNavigator nav = doc.CreateNavigator();
                string path = "/logger/protocols/protocol[@id='SSM']/ecuparams/ecuparam/ecu[@id='" + ecuid + "']/address";
                XPathNodeIterator iter = nav.Select(path);
                while (iter.MoveNext())
                {
                    string addressString = iter.Current.InnerXml;
                    addressString = addressString.Substring(2);
                    uint address = uint.Parse(addressString, System.Globalization.NumberStyles.HexNumber);
                    address |= 0xFF000000;
                    addressString = "0x" + address.ToString("X8");

                    XPathNavigator n = iter.Current;
                    n.MoveToParent();
                    n.MoveToParent();
                    string name = n.GetAttribute("name", "");
                    name = ConvertName(name);

                    MakeName(addressString, name);
                }
            }
        }

        #region Utility functions

        private static void WriteHeader(string functionName, string description)
        {
            Console.WriteLine("///////////////////////////////////////////////////////////////////////////////");
            Console.WriteLine("// " + description);
            Console.WriteLine("///////////////////////////////////////////////////////////////////////////////");
            Console.WriteLine("#include <idc.idc>");
            Console.WriteLine("static " + functionName + "()");
            Console.WriteLine("{");
        }

        private static void WriteFooter()
        {
            Console.WriteLine("}");
        }

        private static void MakeName(string address, string name)
        {
            string command = string.Format("MakeNameEx({0}, \"{1}\", SN_CHECK);",
                address,
                name);
            Console.WriteLine(command);
        }

        private static string ConvertName(string original)
        {
            StringBuilder builder = new StringBuilder(original.Length);
            foreach (char c in original)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                    continue;
                }

                if (c == '_')
                {
                    builder.Append(c);
                    continue;
                }

                if (c == '*')
                {
                    builder.Append("Ext");
                    continue;
                }
            }

            // Make sure it's unique
            string name = builder.ToString();
            while (names.Contains(name))
            {
                name = name + "_";
            }
            names.Add(name);

            return name;
        }

        private static bool CategoryIs(string[] args, string category)
        {
            return string.Compare(args[0], category, StringComparison.OrdinalIgnoreCase) == 0;
        }

        #endregion

        #region Usage instructions

        private static void Usage()
        {
            Console.WriteLine("XmlToIdc Usage:");
            Console.WriteLine("XmlToIdc.exe <category> ...");
            Console.WriteLine();
            Console.WriteLine("Where <category> is one of the following:");
            Console.WriteLine("    tables <cal-id>");
            Console.WriteLine("    stdparam <cal-id> <ssm-base>");
            Console.WriteLine("    extparam <ecu-id>");
            Console.WriteLine();
            Console.WriteLine("ecu-id: ECU identifier, e.g. 2F12785606");
            Console.WriteLine("cal-id: Calibration id, e.g. A2WC522N");
            Console.WriteLine("ssm-base: Base address of the SSM 'read' vector, e.g. 4EDDC");
            Console.WriteLine();
            Console.WriteLine("And you'll want to redirect stdout to a file, like:");
            Console.WriteLine("XmlToIdc.exe ... > Whatever.idc");
        }

        private static void UsageTables()
        {
            Console.WriteLine("XmlToIdc Usage:");
            Console.WriteLine("XmlToIdc.exe tables <cal-id>");
            Console.WriteLine();
            Console.WriteLine("cal-id: Calibration id, e.g. A2WC522N");
            Console.WriteLine();
            Console.WriteLine("And you'll want to redirect stdout to a file, like:");
            Console.WriteLine("XmlToIdc.exe tables A2WC522N > Tables.idc");
        }

        private static void UsageStdParam()
        {
            Console.WriteLine("StdParam Usage:");
            Console.WriteLine("XmlToIdc.exe stdparam <cal-id> <ssm-base>");
            Console.WriteLine();
            Console.WriteLine("cal-id: Calibration id, e.g. A2WC522N");
            Console.WriteLine("ssm-base: Base address of the SSM 'read' vector, e.g. 4EDDC");
            Console.WriteLine();
            Console.WriteLine("And you'll want to redirect stdout to a file, like:");
            Console.WriteLine("XmlToIdc.exe stdparam A2WC522N 4EDDC > StdParam.idc");
        }

        private static void UsageExtParam()
        {
            Console.WriteLine("ExtParam Usage:");
            Console.WriteLine("XmlToIdc.exe extparam <ecu-id>");
            Console.WriteLine();
            Console.WriteLine("ecu-id: ECU identifier, e.g. 2F12785606");
            Console.WriteLine();
            Console.WriteLine("And you'll want to redirect stdout to a file, like:");
            Console.WriteLine("XmlToIdc.exe extparam 2F12785606 > ExtParam.idc");
        }

        #endregion
    }
}
