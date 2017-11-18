using System;
using System.Linq;
using System.Xml;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace WorkflowParsing
{
    public class XamlParsing
    {
        private Dictionary<string, XNode> xNodeDict; //节点字典，在生成代码时，每个节点在访问后即删除
        private Queue<string> funcNodeQueue; //汇聚节点（有多个父节点）的队列，需要用这些节点建立函数
        private int funcCnt;
        //private Dictionary<string, int> cnvrgNodeVisitCntDict; //记录非环中汇聚节点（有多个父节点）被访问的次数
        private string basicIndent; //缩进单位
        private class XNode //自定义节点的类
        {
            public List<XNode> preNodeList; //父节点
            public string id; //节点id
            public string name; //节点名字
            public string attrib; //节点属性：step, decision, switch
            public string codeStr; //节点包含的代码文本
            public Dictionary<string, XNode> childNodeDict; //子节点字典

            public XNode(string id = "", string attrib = "step", string codeStr = "") //构造节点类，默认为flowstep
            {
                this.id = id;
                this.attrib = attrib;
                childNodeDict = new Dictionary<string, XNode>();
                this.codeStr = codeStr;
                preNodeList = new List<XNode>();
            }
            public void AddPreNode(XNode preNode) //添加父节点
            {
                preNodeList.Add(preNode);
            }
            public void AddChildNode(XNode childNode, string key = "next") //默认添加的节点为flowstep的下一个节点
            {
                childNodeDict.Add(key, childNode);
            }
            public void AddCodeStr(string codeStr) //添加代码文本
            {
                this.codeStr = codeStr;
            }
        }
        private class ArgCls
        {
            public string ioType;
            public string type;
            public string name;
        }
        public XamlParsing() //构造函数，变量初始化
        {
            basicIndent = "    ";
            funcCnt = 0;
            xNodeDict = new Dictionary<string, XNode>();
            funcNodeQueue = new Queue<string>();
            //cnvrgNodeVisitCntDict = new Dictionary<string, int>();
        }
        public void parseXamlToCs(XmlDocument xDoc, ref string outCodeStr) //入口函数，将Xaml解析为Cs，并赋值给outCodeStr
        {
            string codeStr = "";
            var rootActNode = xDoc["Activity"];
            var fcRootNode = rootActNode["Flowchart"];
            if (fcRootNode.HasAttribute("sap2010:Annotation.AnnotationText")) //获取注释
            {
                string globalAnnotation = fcRootNode.Attributes["sap2010:Annotation.AnnotationText"].Value;
                codeStr += "/*\n" + globalAnnotation + "\n*/\n";
            }
            codeStr += getNameSpace(rootActNode);
            var rootClassName = rootActNode.Attributes["x:Class"].Value;
            string localClassName = rootClassName.Split('.').Last();
            codeStr += "namespace " + rootClassName.Substring(0, rootClassName.IndexOf("." + localClassName)) + "\n{\n";
            codeStr += "public class " + localClassName + "\n{\n";
            codeStr += getConstructedFunction(rootActNode); //获取成员变量和构造函数

            string varInitStr = "";
            foreach (XmlNode node in fcRootNode.ChildNodes)
            {
                string fcChildName = node.Name;
                XNode isolateNode = null;
                switch (node.Name)
                {
                    case "Flowchart.Variables":
                        varInitStr += getVarInit(node); //初始化成员变量
                        break;
                    case "Flowchart.StartNode": //从起始节点开始遍历
                        codeStr += "\n" + "public void Flowchart(" + ")\n";
                        codeStr += "{\n";
                        codeStr += varInitStr;
                        XNode firstNode = getFcInnerNode(node); //解析遍历的各个节点，并返回根节点
                        generateCodeFromNode(firstNode, ref codeStr); //生成代码
                        codeStr += "}\n";
                        codeStr += generateCodeFunctionFromNodeQueue(); //生成函数代码
                        break;
                    case "FlowStep": //孤立起始节点下的流程将被注释
                        codeStr += "/*\n";
                        isolateNode = getFcStep(node); //解析遍历的各个节点，并返回根节点
                        generateCodeFromNode(isolateNode, ref codeStr);
                        codeStr += generateCodeFunctionFromNodeQueue();
                        codeStr += "*/\n";
                        break;
                    case "FlowDecision": //孤立起始节点下的流程将被注释
                        codeStr += "/*\n";
                        isolateNode = getFcDec(node); //解析遍历的各个节点，并返回根节点
                        generateCodeFromNode(isolateNode, ref codeStr); //生成代码
                        codeStr += generateCodeFunctionFromNodeQueue(); //生成函数代码
                        codeStr += "*/\n";
                        break;
                    case "FlowSwitch": //孤立起始节点下的流程将被注释
                        codeStr += "/*\n";
                        isolateNode = getFcSwitch(node);
                        generateCodeFromNode(isolateNode, ref codeStr);
                        codeStr += generateCodeFunctionFromNodeQueue();
                        codeStr += "*/\n";
                        break;
                    default:
                        break;

                }
            }
            codeStr += "}\n}\n";
            setIndent(codeStr, ref outCodeStr);
            //Console.Write(outCodeStr);
        }
        private string getNameSpace(XmlNode rootActNode)
        {
            string codeStr = "";
            XmlNode collectionNode = rootActNode["TextExpression.NamespacesForImplementation"]["sco:Collection"];
            foreach (XmlNode node in collectionNode.ChildNodes)
            {
                codeStr += "using " + node.InnerText + ";\n";
            }
            codeStr += codeStr == "" ? "" : "\n";
            return codeStr;
        }
        private string getConstructedFunction(XmlElement rootActNode)
        {
            string codeStr = "";
            List<ArgCls> argObjList = new List<ArgCls>();
            foreach (XmlNode node in rootActNode["x:Members"].ChildNodes)
            {
                ArgCls argObj = new ArgCls();
                argObj.ioType = Regex.Match(node.Attributes["Type"].Value, @".*(?=\(.*:)").Groups[0].Value;
                argObj.name = node.Attributes["Name"].Value;
                argObj.type = Regex.Match(node.Attributes["Type"].Value, @"(?<=\(.*:).*(?=\))").Groups[0].Value;
                argObjList.Add(argObj);
                codeStr += "public " + argObj.type + " " + argObj.name + "; //" + argObj.ioType + "\n";
            }
            if (rootActNode["Flowchart"].HasAttribute("Flowchart.Variables"))
            {
                codeStr += getVarDef(rootActNode["Flowchart"]["Flowchart.Variables"]);
            }

            var rootClassName = rootActNode.Attributes["x:Class"].Value;
            codeStr += "public " + rootClassName.Split('.').Last() + "(";
            string formalParaStr = "";
            foreach (ArgCls argObj in argObjList)
            {
                if (argObj.ioType == "InArgument")
                {
                    formalParaStr += argObj.type + " " + argObj.name + ", ";
                }

            }
            codeStr += formalParaStr.Length > 2 ? formalParaStr.TrimEnd(' ').TrimEnd(',') : "";
            codeStr += ")\n{\n";
            foreach (ArgCls argObj in argObjList)
            {
                if (argObj.ioType == "InArgument")
                {
                    codeStr += "this." + argObj.name + " = " + argObj.name + ";\n";
                }
            }
            codeStr += "Flowchart();\n}\n";
            return codeStr;
        }
        private XNode getFcInnerNode(XmlNode rootNode)
        {
            XNode xNode = null;
            foreach (XmlNode node in rootNode.ChildNodes)
            {
                xNode = getFcNode(node);
            }
            return xNode;
        }
        private XNode getFcNode(XmlNode node)
        {
            XNode xNode = null;
            //if (((XmlElement)node).HasAttribute("x:Name")){
            //    Console.WriteLine(node.Attributes["x:Name"].Value);
            //}
            //else
            //{
            //    Console.WriteLine(node.InnerText);
            //}
            switch (node.Name)
            {
                case "FlowStep":
                    xNode = getFcStep(node);
                    break;
                case "FlowDecision":
                    xNode = getFcDec(node);
                    break;
                case "FlowSwitch":
                    xNode = getFcSwitch(node);
                    break;
                case "x:Reference":
                    if (xNodeDict.ContainsKey(node.InnerText))
                    {
                        xNode = xNodeDict[node.InnerText];
                    }
                    else
                    {
                        xNode = new XNode(node.InnerText);
                        xNodeDict.Add(node.InnerText, xNode);
                    }

                    break;
                default:
                    break;
            }
            return xNode;
        }
        private string getStatement(XmlNode node)
        {
            string codeStr = "";
            switch (node.Name)
            {
                case "Sequence":
                    codeStr += "// Sequence Beginning";
                    codeStr += (getAnnotation(node) == "") ? "" : ": " + getAnnotation(node, prefix: "");
                    codeStr += "\n";
                    codeStr += getSeq(node);
                    codeStr += "// Sequence End";
                    codeStr += (getAnnotation(node) == "") ? "" : ": " + getAnnotation(node, prefix: "");
                    codeStr += "\n";
                    break;
                case "Assign":
                    codeStr += getAssign(node);
                    break;
                case "WriteLine":
                    codeStr += getWriteLine(node);
                    break;
                case "If":
                    codeStr += getIf(node);
                    break;
                case "While":
                    codeStr += getWhile(node);
                    break;
                case "DoWhile":
                    codeStr += getDoWhile(node);
                    break;
                case "Parallel":
                    codeStr += getParallel(node);
                    break;
                case "ForEach":
                    codeStr += getForEach(node);
                    break;
                case "Switch":
                    codeStr += getSwitch(node);
                    break;
                default:
                    codeStr += getActivity(node);
                    break;
            }
            return codeStr;
        }
        private string getSeq(XmlNode rootNode)
        {
            var codeStr = "";
            foreach (XmlNode node in rootNode.ChildNodes)
            {
                if (node.Name == "Sequence.Variables")
                {
                    codeStr += getVarInit(node);
                }
                else
                {
                    codeStr += getStatement(node);
                }
            }
            return codeStr;
        }
        private XNode getFcStep(XmlNode rootNode)
        {
            string id =
                ((XmlElement)rootNode).HasAttribute("x:Name")
                ? rootNode.Attributes["x:Name"].Value
                : rootNode.Attributes["sap2010:WorkflowViewState.IdRef"].Value;
            if (xNodeDict.ContainsKey(id))
            {
                return xNodeDict[id];
            }

            XNode xNode = new XNode(id);
            var codeStr = "";
            foreach (XmlNode node in rootNode.ChildNodes)
            {
                if (node.Name == "FlowStep.Next")
                {
                    XNode childNode = getFcInnerNode(node);
                    xNodeDict[childNode.id].AddPreNode(xNode);
                    xNode.AddChildNode(xNodeDict[childNode.id]);
                }
                else
                {
                    codeStr += getStatement(node);
                }
            }
            xNode.AddCodeStr(codeStr);
            if (xNodeDict.ContainsKey(xNode.id))
            {
                xNode.preNodeList = xNodeDict[xNode.id].preNodeList;
                xNodeDict[xNode.id] = xNode;
            }
            else
            {
                xNodeDict.Add(xNode.id, xNode);
            }
            return xNode;
        }
        private XNode getFcDec(XmlNode rootNode)
        {
            string id =
                ((XmlElement)rootNode).HasAttribute("x:Name")
                ? rootNode.Attributes["x:Name"].Value
                : rootNode.Attributes["sap2010:WorkflowViewState.IdRef"].Value;
            if (xNodeDict.ContainsKey(id))
            {
                return xNodeDict[id];
            }
            XNode xNode = new XNode(id, "decision");
            var codeStr = "";
            if (((XmlElement)rootNode).HasAttribute("Condition"))
            {
                codeStr += "if(" + rootNode.Attributes["Condition"].Value + ")" + getAnnotation(rootNode) + "\n";
            }
            foreach (XmlNode node in rootNode.ChildNodes)
            {
                switch (node.Name)
                {
                    case "FlowDecision.Condition":
                        codeStr += "if(" + node.InnerText + ")" + getAnnotation(rootNode) + "\n";
                        break;
                    case "FlowDecision.True":
                        XNode childNodeT = getFcInnerNode(node);
                        xNodeDict[childNodeT.id].AddPreNode(xNode);
                        xNode.AddChildNode(xNodeDict[childNodeT.id], "true");
                        break;
                    case "FlowDecision.False":
                        XNode childNodeF = getFcInnerNode(node);
                        xNodeDict[childNodeF.id].AddPreNode(xNode);
                        xNode.AddChildNode(xNodeDict[childNodeF.id], "false");
                        break;
                    default:
                        break;
                }
            }
            xNode.AddCodeStr(codeStr);
            if (xNodeDict.ContainsKey(xNode.id))
            {
                xNode.preNodeList = xNodeDict[xNode.id].preNodeList;
                xNodeDict[xNode.id] = xNode;
            }
            else
            {
                xNodeDict.Add(xNode.id, xNode);
            }
            return xNode;
        }
        private XNode getFcSwitch(XmlNode rootNode)
        {
            string id =
                ((XmlElement)rootNode).HasAttribute("x:Name")
                ? rootNode.Attributes["x:Name"].Value
                : rootNode.Attributes["sap2010:WorkflowViewState.IdRef"].Value;
            if (xNodeDict.ContainsKey(id))
            {
                return xNodeDict[id];
            }

            XNode xNode = new XNode(id, "switch"); //属性赋值
            string switchType = null;
            var codeStr = "";
            foreach (XmlNode node in rootNode.ChildNodes)
            {
                XNode childNode = null;
                switch (node.Name)
                {
                    case "FlowSwitch.Expression":
                        codeStr += "switch(" + node.InnerText + ")" + getAnnotation(rootNode) + "\n";
                        switchType = Regex.Replace(node["mca:CSharpValue"].Attributes["x:TypeArguments"].Value, @".*:", "");
                        break;
                    case "FlowSwitch.Default":
                        childNode = getFcInnerNode(node);
                        xNodeDict[childNode.id].AddPreNode(xNode);
                        xNode.AddChildNode(xNodeDict[childNode.id], "default");
                        break;
                    case "x:Reference":
                        string childId = node.FirstChild.Value;
                        if (xNodeDict.ContainsKey(childId))
                        {
                            xNodeDict[childId].AddPreNode(xNode);
                        }
                        else
                        {
                            xNodeDict.Add(childId, new XNode(childId));
                        }
                        xNode.AddChildNode(xNodeDict[childId], formattingValue(node["x:Key"].InnerText, switchType));

                        break;
                    default:
                        childNode = getFcNode(node);
                        xNodeDict[childNode.id].AddPreNode(xNode);
                        xNode.AddChildNode(xNodeDict[childNode.id], formattingValue(node.Attributes["x:Key"].Value, switchType));
                        break;
                }
            }
            xNode.AddCodeStr(codeStr);
            if (xNodeDict.ContainsKey(xNode.id))
            {
                xNode.preNodeList = xNodeDict[xNode.id].preNodeList;
                xNodeDict[xNode.id] = xNode;
            }
            else
            {
                xNodeDict.Add(xNode.id, xNode);
            }
            return xNode;
        }
        private string getAssign(XmlNode rootNode)
        {
            var codeStr = "";
            //var varType = rootNode["Assign.To"]["OutArgument"].Attributes["x:TypeArguments"].Value;
            //varType = Regex.Replace(varType, @".*:", "");
            var varName = rootNode["Assign.To"]["OutArgument"]["mca:CSharpReference"].InnerText;
            var varVal = "";
            var inArgNode = rootNode["Assign.Value"]["InArgument"];
            XmlNode inArgValNode = inArgNode.FirstChild;
            switch (inArgValNode.Name)
            {
                case "Literal":
                    varVal = formattingValue(
                        inArgValNode.Attributes["Value"].Value,
                        Regex.Replace(inArgValNode.Attributes["x:TypeArguments"].Value, ".*:", "")
                        );
                    break;
                case "mca:CSharpValue":
                    varVal = inArgValNode.InnerText;
                    break;
                default:
                    varVal = inArgValNode.InnerText;
                    break;
            }
            //codeStr += varType + " " + varName + " = " + varVal + ";" + getAnnotation(rootNode) + "\n";
            codeStr += varName + " = " + varVal + ";" + getAnnotation(rootNode) + "\n";
            return codeStr;
        }
        private string getWriteLine(XmlNode rootNode)
        {
            var codeStr = "";
            codeStr += "Console.WriteLine(";
            if (((XmlElement)rootNode).HasAttribute("Text"))
            {
                codeStr += formattingValue(rootNode.Attributes["Text"].Value, "string");
            }
            else
            {
                codeStr += rootNode["InArgument"].FirstChild.InnerText;
            }
            codeStr += ");" + getAnnotation(rootNode) + "\n";
            return codeStr;
        }
        private string getIf(XmlNode rootNode)
        {
            var codeStr = "";
            foreach (XmlNode node in rootNode.ChildNodes)
            {
                switch (node.Name)
                {
                    case "If.Condition":
                        codeStr += "if(" + node["InArgument"].FirstChild.InnerText + ")\n{\n";
                        break;
                    case "If.Then":
                        foreach (XmlNode childNode in node.ChildNodes)
                        {
                            codeStr += getStatement(childNode);
                        }
                        break;
                    case "If.Else":
                        string elseStr = "";
                        foreach (XmlNode childNode in node.ChildNodes)
                        {
                            elseStr += "}\nelse\n{\n" + getStatement(childNode);
                        }
                        break;
                    default:
                        break;
                }
            }
            codeStr += "}\n";
            return codeStr;
        }
        private string getWhile(XmlNode rootNode)
        {
            string codeStr = "";
            foreach (XmlNode node in rootNode.ChildNodes)
            {
                switch (node.Name)
                {
                    case "While.Condition":
                        codeStr += "while(" + node.FirstChild.InnerText + ")\n" + getAnnotation(rootNode) + "{\n";
                        break;
                    default:
                        codeStr += getStatement(node);
                        break;
                }
            }
            codeStr += "}\n";
            return codeStr;
        }
        private string getDoWhile(XmlNode rootNode)
        {
            string codeStr = "";
            string conditionStr = "";
            codeStr += "do\n{\n";
            foreach (XmlNode node in rootNode.ChildNodes)
            {
                switch (node.Name)
                {
                    case "DoWhile.Condition":
                        conditionStr += "}\nwhile(" + node.FirstChild.InnerText + ");" + getAnnotation(rootNode) + "\n";
                        break;
                    default:
                        codeStr += getStatement(node);
                        break;
                }
            }
            codeStr += conditionStr;
            return codeStr;
        }
        private string getSwitch(XmlNode rootNode)
        {
            string codeStr = "";
            string switchType = null;
            foreach (XmlNode node in rootNode.ChildNodes)
            {
                if (node.Name == "Switch.Expression")
                {
                    codeStr += "switch(" + node.InnerText + ")" + getAnnotation(rootNode) + "\n{\n"; ;
                    switchType = Regex.Replace(node["InArgument"]["mca:CSharpValue"].Attributes["x:TypeArguments"].Value, @".*:", "");
                }
                else if (((XmlElement)node).HasAttribute("x:Key"))
                {
                    string caseKeyStr = formattingValue(node.Attributes["x:Key"].Value, switchType);
                    codeStr += "case " + caseKeyStr + ":\n"
                        + getStatement(node)
                        + "break;\n";
                }
            }
            codeStr += "}\n";
            return codeStr;
        }
        private string getForEach(XmlNode rootNode)
        {
            string codeStr = "";
            var EnumVarStr = "";
            var IterItemStr = "";
            EnumVarStr = rootNode["ForEach.Values"].InnerText;
            foreach (XmlNode node in rootNode["ActivityAction"].ChildNodes)
            {
                switch (node.Name)
                {
                    case "ActivityAction.Argument":
                        IterItemStr =
                            Regex.Replace(node["DelegateInArgument"].Attributes["x:TypeArguments"].Value, @".*:", "")
                            + " "
                            + node["DelegateInArgument"].Attributes["Name"].Value;
                        codeStr += "foreach (" + IterItemStr + " in " + EnumVarStr + ");" + getAnnotation(rootNode) + "\n{\n";
                        break;
                    default:
                        codeStr += getStatement(node);
                        break;
                }
            }
            codeStr += "}\n";
            return codeStr;
        }
        private string getActivity(XmlNode rootNode)
        {
            var codeStr = "";
            if (((XmlElement)rootNode).HasAttribute("sap2010:WorkflowViewState.IdRef"))
            {
                var actVarName = rootNode.Attributes["sap2010:WorkflowViewState.IdRef"].Value;
                codeStr += "var " + actVarName + " = new " + rootNode.LocalName + "();" + getAnnotation(rootNode) + "\n";
                foreach (XmlAttribute attr in rootNode.Attributes)
                {
                    if (Array.IndexOf(
                        new string[] {
                            "sap2010:WorkflowViewState.IdRef",
                            "sap2010:Annotation.AnnotationText",
                            "DisplayName"
                        }, attr.Name) != -1)
                    {
                        continue;
                    }
                    codeStr += actVarName + "." + attr.Name + " = \"" + attr.Value + "\";\n";
                }
                foreach (XmlNode node in rootNode.ChildNodes)
                {
                    if (node.FirstChild.Name == "InArgument")
                    {
                        XmlNode inArgValNode = node["InArgument"].FirstChild;
                        string varVal = "";
                        switch (inArgValNode.Name)
                        {
                            case "Literal":
                                varVal = formattingValue(
                                    inArgValNode.Attributes["Value"].Value,
                                    Regex.Replace(inArgValNode.Attributes["x:TypeArguments"].Value, ".*:", "")
                                    );
                                break;
                            case "mca:CSharpValue":
                                varVal = inArgValNode.InnerText;
                                break;
                            default:
                                varVal = inArgValNode.InnerText;
                                break;
                        }
                        codeStr += actVarName + Regex.Replace(node.Name, @".*\.", ".") + " = " + varVal + ";\n";
                    }
                    else if (node.FirstChild.Name == "OutArgument")
                    {
                        codeStr += "var " + node["OutArgument"].FirstChild.InnerText + " = " + actVarName + Regex.Replace(node.Name, @".*\.", ".") + ";\n";
                    }
                }
            }
            return codeStr;
        }
        private string getVarInit(XmlNode rootNode)
        {
            var codeStr = "";
            foreach (XmlNode varNode in rootNode.ChildNodes)
            {
                string varType = varNode.Attributes["x:TypeArguments"].Value;
                varType = Regex.Replace(varType, @".*:", "");
                string varName = varNode.Attributes["Name"].Value;
                string varVal = null;
                if (varNode.HasChildNodes)
                {
                    var firstChild = varNode["Variable.Default"].FirstChild;
                    switch (firstChild.Name)
                    {
                        case "Literal":
                            varVal = firstChild.Attributes["Value"].Value;
                            if (varVal.GetType() == typeof(string))
                            {
                                varVal = "\"" + varVal + "\"";
                            }
                            break;
                        case "mca:CSharpValue":
                            varVal = firstChild.InnerText;
                            break;
                        default:
                            break;
                    }
                    if (rootNode.Name == "Sequence.Variables")
                    {
                        codeStr += varType + " ";
                    }
                    codeStr += varName + " = " + varVal + ";\n";
                }
                else if (rootNode.Name != "Flowchart.Variables")
                {
                    codeStr += varType + " " + varName + ";\n";
                }
            }
            return codeStr;
        }
        private string getVarDef(XmlNode rootNode)
        {
            var codeStr = "";
            foreach (XmlNode varNode in rootNode.ChildNodes)
            {
                string varType = varNode.Attributes["x:TypeArguments"].Value;
                varType = Regex.Replace(varType, @".*:", "");
                if (rootNode.Name == "Flowchart.Variables")
                {
                    varType = "private " + varType;
                }
                string varName = varNode.Attributes["Name"].Value;
                codeStr += varType + " " + varName + ";\n";
            }
            return codeStr;
        }
        private string getParallel(XmlNode rootNode)
        {
            var codeStr = "";
            codeStr += "Parallel.Invoke(" + getAnnotation(rootNode) + "\n";
            int cnt = 0;
            foreach (XmlNode node in rootNode.ChildNodes)
            {
                codeStr += "()=>\n{\n" + getStatement(node) + "}";
                codeStr += (cnt == rootNode.ChildNodes.Count - 1) ? "\n" : ",\n";
                cnt += 1;
            }
            codeStr += ");\n";
            return codeStr;
        }
        private void generateCodeFromNode(XNode xNode, ref string codeStr) //根据节点生成代码
        {
            //string codeStr = "";

            if (xNode.preNodeList.Count > 1 && xNodeDict.ContainsKey(xNode.id))
            {
                //if (isInLoop(xNode)) // xNode为环的交叉点，需要加入函数队列
                //{
                if (!funcNodeQueue.Contains(xNode.id))
                {
                    xNode.name = "function_" + funcCnt.ToString();
                    funcCnt += 1;
                    funcNodeQueue.Enqueue(xNode.id);
                }
                codeStr += xNode.name + "();\n";
                return;
                //}
                //else // xNode为多条分支的汇聚点，需要将该节点缓存在汇聚点字典中
                //{
                //    if(cnvrgNodeVisitCntDict.ContainsKey(xNode.id) == false)
                //    {
                //        cnvrgNodeVisitCntDict.Add(xNode.id, 1);
                //    }
                //    else
                //    {
                //        cnvrgNodeVisitCntDict[xNode.id] += 1;
                //    }
                //    return;
                //}
            }
            else if (xNodeDict.ContainsKey(xNode.id)) //只有一个父节点的节点，将会正常访问，并从节点字典中删除
            {
                xNodeDict.Remove(xNode.id);
            }

            //Dictionary<string, int> curCnvrgNodeVisitCntDict = new Dictionary<string, int>();
            //Console.WriteLine(xNode.id);
            codeStr += xNode.codeStr;
            switch (xNode.attrib)
            {
                case "step":
                    if (xNode.childNodeDict.ContainsKey("next"))
                    {
                        XNode nextNode = xNode.childNodeDict["next"];
                        generateCodeFromNode(nextNode, ref codeStr);
                    }
                    break;
                case "decision":
                case "switch":
                    codeStr += "{\n";
                    foreach (KeyValuePair<string, XNode> kv in xNode.childNodeDict)
                    {
                        string decStr = "";
                        generateCodeFromNode(kv.Value, ref decStr);
                        if (xNode.attrib == "decision")
                        {
                            if (kv.Key == "false" && decStr != "")
                            {
                                codeStr += "}\nelse\n{\n";
                            }
                            codeStr += decStr;
                        }
                        else if (xNode.attrib == "switch")
                        {
                            codeStr += kv.Key == "default" ? "" : "case ";
                            codeStr += kv.Key + ":\n";
                            codeStr += decStr + "break;\n";
                        }
                        //foreach (string key in cnvrgNodeVisitCntDict.Keys) //获取子流程中汇聚节点的访问次数
                        //{
                        //    if (curCnvrgNodeVisitCntDict.ContainsKey(key))
                        //    {
                        //        curCnvrgNodeVisitCntDict[key] += cnvrgNodeVisitCntDict[key];
                        //    }
                        //    else
                        //    {
                        //        curCnvrgNodeVisitCntDict.Add(key, cnvrgNodeVisitCntDict[key]);
                        //    }
                        //}
                        //cnvrgNodeVisitCntDict = new Dictionary<string, int>();
                    }
                    codeStr += "}\n";
                    //foreach (string key in curCnvrgNodeVisitCntDict.Keys)
                    //{
                    //    if (xNodeDict.ContainsKey(key) && curCnvrgNodeVisitCntDict[key] == xNodeDict[key].preNodeList.Count)
                    //    {
                    //        Console.WriteLine(key);
                    //        XNode cnvrgNode = xNodeDict[key];
                    //        xNodeDict.Remove(key); //在字典节点中删除要访问的节点
                    //        generateCodeFromNode(cnvrgNode, ref codeStr);                           
                    //    }
                    //}
                    //cnvrgNodeVisitCntDict = curCnvrgNodeVisitCntDict;
                    break;
                default:
                    break;
            }
            if (xNode.childNodeDict.Count == 0)
            {
                codeStr += "return;\n";
            }
            return;
        }
        private string generateCodeFunctionFromNodeQueue() //根据节点队列生成代码函数
        {
            string codeStr = "";
            while (funcNodeQueue.Count != 0)
            {
                XNode xNode = xNodeDict[funcNodeQueue.Dequeue()];
                xNodeDict.Remove(xNode.id); //在字典节点中删除要访问的节点
                codeStr += "private void " + xNode.name + "()\n{\n";
                generateCodeFromNode(xNode, ref codeStr);
                codeStr += "}\n";
            }
            return codeStr;
        }
        private string getAnnotation(XmlNode node, string option = "ad", string prefix = " //", string suffix = "") //获取注释文字，默认同时获取展示名称和注释
        {
            string codeStr = "";
            string displayNameStr = "";
            string annotationStr = "";
            string seperator = " | ";
            if (((XmlElement)node).HasAttribute("DisplayName") && option.IndexOf('d') != -1) //获取展示名称
            {
                displayNameStr = "Disp:" + node.Attributes["DisplayName"].Value;
            }
            if (((XmlElement)node).HasAttribute("sap2010:Annotation.AnnotationText") && option.IndexOf('a') != -1) //获取注释文字
            {
                annotationStr = "Anno:" + node.Attributes["sap2010:Annotation.AnnotationText"].Value;
                if (annotationStr.Contains('\n'))
                {
                    annotationStr = "\n/* " + annotationStr + " */";
                }
            }
            codeStr += ((displayNameStr != "" || annotationStr != "") && !annotationStr.Contains('\n')) ? prefix : "";
            codeStr += displayNameStr;
            codeStr += (displayNameStr != "" && annotationStr != "" && !annotationStr.Contains('\n')) ? seperator : "";
            codeStr += annotationStr;
            return codeStr;
        }
        private void setIndent(string codeStr, ref string outCodeStr) //设置缩进
        {
            var lastIndentCnt = 0;
            var curIndentCnt = 0;
            foreach (var line in codeStr.Split('\n'))
            {
                foreach (var c in line)
                {
                    if (c == '{')
                    {
                        curIndentCnt += 1;
                    }
                    else if (c == '}')
                    {
                        curIndentCnt -= 1;
                    }
                }
                var indent = "";
                if (Regex.IsMatch(line, @"^case\s.*:$") || Regex.IsMatch(line, @"^default:$"))
                {
                    curIndentCnt += 1;
                }
                if (curIndentCnt < lastIndentCnt)
                {
                    lastIndentCnt = curIndentCnt;
                }
                for (int i = 0; i < lastIndentCnt; i++)
                {
                    indent += basicIndent;
                }
                outCodeStr += indent + line + "\n";
                if (Regex.IsMatch(line, @"^break;$"))
                {
                    curIndentCnt -= 1;
                }
                lastIndentCnt = curIndentCnt;
            }
        }
        private bool isInLoop(XNode rootNode, XNode xNode = null) //判断节点rootNode是否在环中
        {
            if (xNode == null)
            {
                foreach (string key in rootNode.childNodeDict.Keys)
                {
                    if (isInLoop(rootNode, rootNode.childNodeDict[key]))
                    {
                        return true;
                    }
                }
            }
            else if (xNode.id == rootNode.id)
            {
                return true;
            }
            else if (xNode.childNodeDict.Count > 0)
            {
                foreach (string key in xNode.childNodeDict.Keys)
                {
                    if (isInLoop(rootNode, xNode.childNodeDict[key]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        private string formattingValue(string value, string type) //根据类型格式化值
        {
            string fmtVal = value;
            switch (type)
            {
                case "String":
                case "string":
                    fmtVal = "\"" + value.Replace("\"", "\\\"") + "\"";
                    break;
                case "Char":
                case "char":
                    fmtVal = "'" + value + "'";
                    break;
                default:
                    fmtVal = value;
                    break;
            }
            return fmtVal;
        }
    }
}
